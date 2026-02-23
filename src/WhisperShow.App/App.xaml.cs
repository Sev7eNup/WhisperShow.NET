using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using WhisperShow.App.Services;
using WhisperShow.App.ViewModels;
using WhisperShow.App.Views;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services.Configuration;
using WhisperShow.Core.Services.Audio;
using WhisperShow.Core.Services.Hotkey;
using WhisperShow.Core.Services.ModelManagement;
using WhisperShow.Core.Services.History;
using WhisperShow.Core.Services.Statistics;
using WhisperShow.Core.Services.Snippets;
using WhisperShow.Core.Services;
using WhisperShow.Core.Services.TextCorrection;
using WhisperShow.Core.Services.TextInsertion;
using WhisperShow.Core.Services.Transcription;

namespace WhisperShow.App;

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
        _mutex = new Mutex(true, "WhisperShow-SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("WhisperShow is already running.", "WhisperShow",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Configure Serilog
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperShow", "logs", "log-.txt");

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
                config.AddEnvironmentVariables("WHISPERSHOW_");
            })
            .ConfigureServices((context, services) =>
            {
                // Configuration
                services.Configure<WhisperShowOptions>(
                    context.Configuration.GetSection(WhisperShowOptions.SectionName));

                // Core services
                services.AddSingleton<OpenAiClientFactory>();
                services.AddSingleton<IAudioRecordingService, AudioRecordingService>();
                services.AddSingleton<IAudioMutingService, AudioMutingService>();
                services.AddSingleton<IAudioCompressor, AudioCompressor>();
                services.AddSingleton<ITranscriptionService, OpenAiTranscriptionService>();
                services.AddSingleton<ITranscriptionService, LocalTranscriptionService>();
                services.AddSingleton<TranscriptionProviderFactory>();
                services.AddSingleton<ITextInsertionService, TextInsertionService>();
                services.AddSingleton<ITextCorrectionService, OpenAiTextCorrectionService>();
                services.AddSingleton<ITextCorrectionService, LocalTextCorrectionService>();
                services.AddSingleton<TextCorrectionProviderFactory>();
                services.AddSingleton<ICombinedTranscriptionCorrectionService, CombinedAudioTranscriptionService>();
                services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();
                services.AddHttpClient();
                services.AddSingleton<ModelDownloadHelper>();
                services.AddSingleton<IModelManager, ModelManager>();
                services.AddSingleton<ICorrectionModelManager, CorrectionModelManager>();
                services.AddSingleton<IModelPreloadService, ModelPreloadService>();
                services.AddSingleton<IDictionaryService, DictionaryService>();
                services.AddSingleton<ISnippetService, SnippetService>();
                services.AddSingleton<IUsageStatsService, UsageStatsService>();
                services.AddSingleton<ITranscriptionHistoryService, TranscriptionHistoryService>();

                // App services
                services.AddSingleton<ISoundEffectService, SoundEffectService>();
                services.AddSingleton<IDispatcherService, WpfDispatcherService>();
                services.AddSingleton<ISettingsPersistenceService, SettingsPersistenceService>();
                services.AddSingleton<IWindowFocusService, WindowFocusService>();
                services.AddSingleton<IAutoStartService, AutoStartService>();

                // ViewModels
                services.AddSingleton<OverlayViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<HistoryViewModel>();

                // Windows
                services.AddSingleton<OverlayWindow>();
                services.AddSingleton<SettingsWindow>();
                services.AddSingleton<HistoryWindow>();
            })
            .Build();

        await _host.StartAsync();

        // Preload all services that persist data to disk
        await Task.WhenAll(
            _host.Services.GetRequiredService<ITranscriptionHistoryService>().LoadAsync(),
            _host.Services.GetRequiredService<IUsageStatsService>().LoadAsync(),
            _host.Services.GetRequiredService<IDictionaryService>().LoadAsync(),
            _host.Services.GetRequiredService<ISnippetService>().LoadAsync());

        try
        {
            // Sync autostart registry with config
            var opts = _host.Services.GetRequiredService<IOptions<WhisperShowOptions>>().Value;
            _host.Services.GetRequiredService<IAutoStartService>().SetAutoStart(opts.App.LaunchAtLogin);

            // Show overlay window
            var overlayWindow = _host.Services.GetRequiredService<OverlayWindow>();
            overlayWindow.Show();

            // Setup system tray
            _trayIconManager = new TrayIconManager();
            _trayIconManager.Initialize(
                overlayWindow,
                () => _host!.Services.GetRequiredService<SettingsWindow>(),
                () => _host!.Services.GetRequiredService<HistoryWindow>(),
                Shutdown);

            // Preload local models in background (non-blocking)
            PreloadLocalModels(opts);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to start application");
            Log.CloseAndFlush();
            MessageBox.Show($"Startup error:\n\n{ex}", "WhisperShow Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void PreloadLocalModels(WhisperShowOptions opts)
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

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIconManager?.Dispose();

        if (_host is not null)
        {
            // Unsubscribe event handlers before host disposal to ensure clean shutdown
            _host.Services.GetService<OverlayWindow>()?.Cleanup();
            _host.Services.GetService<SettingsWindow>()?.Cleanup();
            _host.Services.GetService<HistoryWindow>()?.Cleanup();

            var hotkeyService = _host.Services.GetService<IGlobalHotkeyService>();
            hotkeyService?.Dispose();

            var audioService = _host.Services.GetService<IAudioRecordingService>();
            (audioService as IDisposable)?.Dispose();

            await _host.StopAsync();
            _host.Dispose();
        }

        Log.CloseAndFlush();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
