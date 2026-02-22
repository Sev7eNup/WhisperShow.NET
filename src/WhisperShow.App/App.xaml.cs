using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using H.NotifyIcon;
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
using WhisperShow.Core.Services.Audio;
using WhisperShow.Core.Services.Hotkey;
using WhisperShow.Core.Services.ModelManagement;
using WhisperShow.Core.Services.History;
using WhisperShow.Core.Services.Statistics;
using WhisperShow.Core.Services.Snippets;
using WhisperShow.Core.Services.TextCorrection;
using WhisperShow.Core.Services.TextInsertion;
using WhisperShow.Core.Services.Transcription;

namespace WhisperShow.App;

public partial class App : Application
{
    private static Mutex? _mutex;
    private IHost? _host;
    private TaskbarIcon? _trayIcon;

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
                services.AddSingleton<IModelManager, ModelManager>();
                services.AddSingleton<ICorrectionModelManager, CorrectionModelManager>();
                services.AddSingleton<IDictionaryService, DictionaryService>();
                services.AddSingleton<ISnippetService, SnippetService>();
                services.AddSingleton<IUsageStatsService, UsageStatsService>();
                services.AddSingleton<ITranscriptionHistoryService, TranscriptionHistoryService>();

                // App services
                services.AddSingleton(sp =>
                {
                    var opts = sp.GetRequiredService<IOptions<WhisperShowOptions>>();
                    var logger = sp.GetRequiredService<ILogger<SoundEffectService>>();
                    return new SoundEffectService(logger, opts.Value.App.SoundEffects);
                });

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

        try
        {
            // Sync autostart registry with config
            var opts = _host.Services.GetRequiredService<IOptions<WhisperShowOptions>>().Value;
            SyncAutoStartRegistry(opts);

            // Show overlay window
            var overlayWindow = _host.Services.GetRequiredService<OverlayWindow>();
            overlayWindow.Show();

            // Setup system tray
            SetupTrayIcon(overlayWindow);

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

    private void SetupTrayIcon(OverlayWindow overlayWindow)
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "WhisperShow - Speech to Text"
        };

        // Generate a simple icon programmatically
        _trayIcon.Icon = CreateTrayIcon();

        var contextMenu = new System.Windows.Controls.ContextMenu();

        var showItem = new System.Windows.Controls.MenuItem { Header = "Show Overlay" };
        showItem.Click += (_, _) =>
        {
            overlayWindow.Show();
            overlayWindow.Activate();
        };

        var hideItem = new System.Windows.Controls.MenuItem { Header = "Hide Overlay" };
        hideItem.Click += (_, _) => overlayWindow.Hide();

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) =>
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
            Shutdown();
        };

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Einstellungen" };
        settingsItem.Click += (_, _) =>
        {
            var settingsWindow = _host!.Services.GetRequiredService<SettingsWindow>();
            settingsWindow.Show();
            settingsWindow.Activate();
        };

        var historyItem = new System.Windows.Controls.MenuItem { Header = "History" };
        historyItem.Click += (_, _) =>
        {
            var historyWindow = _host!.Services.GetRequiredService<HistoryWindow>();
            historyWindow.ShowAndRefresh();
        };

        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(hideItem);
        contextMenu.Items.Add(new System.Windows.Controls.Separator());
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(historyItem);
        contextMenu.Items.Add(new System.Windows.Controls.Separator());
        contextMenu.Items.Add(exitItem);

        _trayIcon.TrayRightMouseDown += (_, _) =>
        {
            // Win32 KB135788 workaround: the process must own a foreground window
            // before showing a tray context menu, otherwise it closes immediately.
            // The overlay has WS_EX_NOACTIVATE, so temporarily remove it.
            var hwnd = new WindowInteropHelper(overlayWindow).Handle;
            if (hwnd == IntPtr.Zero) return;

            int exStyle = NativeMethods.GetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE,
                exStyle & ~NativeMethods.WS_EX_NOACTIVATE);
            NativeMethods.SetForegroundWindow(hwnd);

            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            contextMenu.IsOpen = true;

            void OnClosed(object s, RoutedEventArgs e)
            {
                contextMenu.Closed -= OnClosed;
                NativeMethods.SetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
            }
            contextMenu.Closed += OnClosed;
        };

        _trayIcon.TrayLeftMouseDown += (_, _) =>
        {
            if (overlayWindow.IsVisible)
                overlayWindow.Hide();
            else
            {
                overlayWindow.Show();
                overlayWindow.Activate();
            }
        };

        _trayIcon.ForceCreate();
    }

    private void PreloadLocalModels(WhisperShowOptions opts)
    {
        _ = Task.Run(() =>
        {
            try
            {
                if (opts.Provider == TranscriptionProvider.Local)
                {
                    var local = _host!.Services.GetServices<ITranscriptionService>()
                        .OfType<LocalTranscriptionService>().FirstOrDefault();
                    local?.Preload();
                }

                if (opts.TextCorrection.Provider == TextCorrectionProvider.Local)
                {
                    var local = _host!.Services.GetServices<ITextCorrectionService>()
                        .OfType<LocalTextCorrectionService>().FirstOrDefault();
                    local?.Preload();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Background model preload failed");
            }
        });
    }

    private static Icon CreateTrayIcon()
    {
        // Create a simple microphone-style icon programmatically
        var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Draw circle background
        using var bgBrush = new SolidBrush(Color.FromArgb(45, 45, 45));
        g.FillEllipse(bgBrush, 1, 1, 30, 30);

        // Draw microphone shape
        using var micPen = new Pen(Color.White, 2);
        g.DrawEllipse(micPen, 12, 6, 8, 12); // mic body
        g.DrawArc(micPen, 9, 10, 14, 12, 0, 180); // mic cup
        g.DrawLine(micPen, 16, 22, 16, 26); // mic stand

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static void SyncAutoStartRegistry(WhisperShowOptions options)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;

            var currentValue = key.GetValue("WhisperShow") as string;
            var exePath = Environment.ProcessPath;

            if (options.App.LaunchAtLogin)
            {
                // Ensure registry entry exists and points to the correct path
                if (!string.IsNullOrEmpty(exePath))
                {
                    var expected = $"\"{exePath}\"";
                    if (!string.Equals(currentValue, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        key.SetValue("WhisperShow", expected);
                        Log.Information("Updated autostart registry path to {Path}", expected);
                    }
                }
            }
            else
            {
                // Config says off — remove stale registry entry if present
                if (currentValue is not null)
                {
                    key.DeleteValue("WhisperShow", throwOnMissingValue: false);
                    Log.Information("Removed stale autostart registry entry");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to sync autostart registry");
        }
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
        _trayIcon?.Dispose();

        if (_host is not null)
        {
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
