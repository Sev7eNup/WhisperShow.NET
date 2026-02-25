using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using Serilog;
using WriteSpeech.App.Services;
using WriteSpeech.App.ViewModels;
using WriteSpeech.App.Views;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.Configuration;
using WriteSpeech.Core.Services.Audio;
using WriteSpeech.Core.Services.Hotkey;
using WriteSpeech.Core.Services.ModelManagement;
using WriteSpeech.Core.Services.History;
using WriteSpeech.Core.Services.Statistics;
using WriteSpeech.Core.Services.Snippets;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.IDE;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Core.Services.TextInsertion;
using WriteSpeech.Core.Services.Modes;
using WriteSpeech.Core.Services.Transcription;

namespace WriteSpeech.App;

public partial class App : Application
{
    private static Mutex? _mutex;
    private IHost? _host;
    private TrayIconManager? _trayIconManager;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AddCudaLibraryPaths();

        // Single instance check
        _mutex = new Mutex(true, "WriteSpeech-SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("WriteSpeech is already running.", "WriteSpeech",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Migrate data from old app name before anything else
        MigrateAppDataFolder();
        CleanupOldRegistryEntry();

        // Configure Serilog
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WriteSpeech", "logs", "log-.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        // Build host
        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables("WRITESPEECH_");
            })
            .ConfigureServices((context, services) =>
            {
                // Configuration
                services.Configure<WriteSpeechOptions>(
                    context.Configuration.GetSection(WriteSpeechOptions.SectionName));
                services.AddSingleton<IValidateOptions<WriteSpeechOptions>, WriteSpeechOptionsValidator>();

                // Core services
                services.AddSingleton<OpenAiClientFactory>();
                services.AddSingleton<IAudioRecordingService, AudioRecordingService>();
                services.AddSingleton<IAudioMutingService, AudioMutingService>();
                services.AddSingleton<IAudioCompressor, AudioCompressor>();
                services.AddSingleton<IAudioFileReader, AudioFileReader>();
                services.AddSingleton<ITranscriptionService, OpenAiTranscriptionService>();
                services.AddSingleton<ITranscriptionService, LocalTranscriptionService>();
                services.AddSingleton<TranscriptionProviderFactory>();
                services.AddSingleton<ITextInsertionService, TextInsertionService>();
                services.AddSingleton<ISelectedTextService, SelectedTextService>();
                services.AddSingleton<ITextCorrectionService, OpenAiTextCorrectionService>();
                services.AddSingleton<ITextCorrectionService, LocalTextCorrectionService>();
                services.AddSingleton<TextCorrectionProviderFactory>();
                services.AddSingleton<ICombinedTranscriptionCorrectionService, CombinedAudioTranscriptionService>();
                services.AddSingleton<HotkeyServiceProxy>();
                services.AddSingleton<IGlobalHotkeyService>(sp => sp.GetRequiredService<HotkeyServiceProxy>());
                services.AddHttpClient();
                services.AddSingleton<ModelDownloadHelper>();
                services.AddSingleton<IModelManager, ModelManager>();
                services.AddSingleton<ICorrectionModelManager, CorrectionModelManager>();
                services.AddSingleton<IModelPreloadService, ModelPreloadService>();
                services.AddSingleton<IDictionaryService, DictionaryService>();
                services.AddSingleton<IIDEContextService, IDEContextService>();
                services.AddSingleton<ISnippetService, SnippetService>();
                services.AddSingleton<IModeService, ModeService>();
                services.AddSingleton<IUsageStatsService, UsageStatsService>();
                services.AddSingleton<ITranscriptionHistoryService, TranscriptionHistoryService>();

                // App services
                services.AddSingleton<ISoundEffectService, SoundEffectService>();
                services.AddSingleton<IDispatcherService, WpfDispatcherService>();
                services.AddSingleton<ISettingsPersistenceService, SettingsPersistenceService>();
                services.AddSingleton<IWindowFocusService, WindowFocusService>();
                services.AddSingleton<IIDEDetectionService, IDEDetectionService>();
                services.AddSingleton<IAutoStartService, AutoStartService>();
                services.AddSingleton<TrayIconManager>();

                // ViewModels
                services.AddSingleton<OverlayViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<HistoryViewModel>();
                services.AddSingleton<FileTranscriptionViewModel>();

                // Windows
                services.AddSingleton<OverlayWindow>();
                services.AddSingleton<SettingsWindow>();
                services.AddSingleton<HistoryWindow>();
                services.AddSingleton<FileTranscriptionWindow>();
            })
            .Build();

        await _host.StartAsync();

        // Preload all services that persist data to disk
        await Task.WhenAll(
            _host.Services.GetRequiredService<ITranscriptionHistoryService>().LoadAsync(),
            _host.Services.GetRequiredService<IUsageStatsService>().LoadAsync(),
            _host.Services.GetRequiredService<IDictionaryService>().LoadAsync(),
            _host.Services.GetRequiredService<ISnippetService>().LoadAsync(),
            _host.Services.GetRequiredService<IModeService>().LoadAsync());

        try
        {
            // Sync autostart registry with config
            var opts = _host.Services.GetRequiredService<IOptions<WriteSpeechOptions>>().Value;
            _host.Services.GetRequiredService<IAutoStartService>().SetAutoStart(opts.App.LaunchAtLogin);

            // Show overlay window
            var overlayWindow = _host.Services.GetRequiredService<OverlayWindow>();
            overlayWindow.Show();

            // Setup system tray
            _trayIconManager = _host.Services.GetRequiredService<TrayIconManager>();
            _trayIconManager.Initialize(
                overlayWindow,
                () => _host!.Services.GetRequiredService<SettingsWindow>(),
                () => _host!.Services.GetRequiredService<HistoryWindow>(),
                () => _host!.Services.GetRequiredService<FileTranscriptionWindow>(),
                Shutdown);

            // Preload local models in background (non-blocking)
            PreloadLocalModels(opts);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to start application");
            Log.CloseAndFlush();
            MessageBox.Show($"Startup error:\n\n{ex}", "WriteSpeech Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void PreloadLocalModels(WriteSpeechOptions opts)
    {
        var preloadService = _host!.Services.GetRequiredService<IModelPreloadService>();

        if (opts.Provider == TranscriptionProvider.Local)
            preloadService.PreloadTranscriptionModel();

        if (opts.TextCorrection.Provider == TextCorrectionProvider.Local)
            preloadService.PreloadCorrectionModel();
    }

    private static void AddCudaLibraryPaths()
    {
        var candidates = new List<string>();

        // 1. Versioned env vars (CUDA_PATH_V13_1 etc.) — most specific
        foreach (System.Collections.DictionaryEntry env in Environment.GetEnvironmentVariables())
        {
            var key = env.Key?.ToString() ?? "";
            if (key.StartsWith("CUDA_PATH_V13", StringComparison.OrdinalIgnoreCase)
                && env.Value is string val && !string.IsNullOrEmpty(val))
                candidates.Add(val);
        }

        // 2. Generic CUDA_PATH
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrEmpty(cudaPath))
            candidates.Add(cudaPath);

        // 3. Scan standard CUDA toolkit directory for v13.x installations
        var toolkitBase = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
        if (Directory.Exists(toolkitBase))
        {
            foreach (var dir in Directory.GetDirectories(toolkitBase, "v13.*"))
                candidates.Add(dir);
        }

        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var additions = new List<string>();

        foreach (var candidate in candidates)
        {
            var binX64 = Path.Combine(candidate, "bin", "x64");
            if (Directory.Exists(binX64) && !currentPath.Contains(binX64, StringComparison.OrdinalIgnoreCase))
                additions.Add(binX64);

            var bin = Path.Combine(candidate, "bin");
            if (Directory.Exists(bin) && !currentPath.Contains(bin, StringComparison.OrdinalIgnoreCase))
                additions.Add(bin);
        }

        if (additions.Count > 0)
        {
            Environment.SetEnvironmentVariable("PATH", string.Join(";", additions) + ";" + currentPath);
            Log.Information("Added CUDA library paths: {Paths}", string.Join(", ", additions));
        }
    }

    private static void MigrateAppDataFolder()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var oldDir = Path.Combine(appData, "WhisperShow");
        var newDir = Path.Combine(appData, "WriteSpeech");

        if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
        {
            try
            {
                Directory.Move(oldDir, newDir);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to migrate AppData folder from {OldDir} to {NewDir}", oldDir, newDir);
            }
        }
    }

    private static void CleanupOldRegistryEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key?.GetValue("WhisperShow") is not null)
                key.DeleteValue("WhisperShow", throwOnMissingValue: false);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIconManager?.Dispose();

        if (_host is not null)
        {
            // Unsubscribe event handlers before host disposal to ensure clean shutdown
            _host.Services.GetService<OverlayWindow>()?.Cleanup();
            _host.Services.GetService<SettingsWindow>()?.Cleanup();
            _host.Services.GetService<HistoryWindow>()?.Cleanup();
            _host.Services.GetService<FileTranscriptionWindow>()?.Cleanup();

            var hotkeyService = _host.Services.GetService<IGlobalHotkeyService>();
            hotkeyService?.Dispose();

            var audioService = _host.Services.GetService<IAudioRecordingService>();
            (audioService as IDisposable)?.Dispose();

            _host.Services.GetService<IModeService>()?.Dispose();

            // Dispose GPU model services to release VRAM
            foreach (var transcription in _host.Services.GetServices<ITranscriptionService>())
                (transcription as IDisposable)?.Dispose();

            foreach (var correction in _host.Services.GetServices<ITextCorrectionService>())
                (correction as IDisposable)?.Dispose();

            await _host.StopAsync();
            _host.Dispose();
        }

        Log.CloseAndFlush();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
