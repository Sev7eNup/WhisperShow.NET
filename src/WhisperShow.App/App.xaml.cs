using System.Drawing;
using System.IO;
using System.Windows;
using H.NotifyIcon;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WhisperShow.App.Services;
using WhisperShow.App.ViewModels;
using WhisperShow.App.Views;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Services.Audio;
using WhisperShow.Core.Services.Hotkey;
using WhisperShow.Core.Services.ModelManagement;
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
                services.AddSingleton<ITranscriptionService, OpenAiTranscriptionService>();
                services.AddSingleton<ITranscriptionService, LocalTranscriptionService>();
                services.AddSingleton<TranscriptionProviderFactory>();
                services.AddSingleton<ITextInsertionService, TextInsertionService>();
                services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();
                services.AddSingleton<IModelManager, ModelManager>();

                // ViewModels
                services.AddSingleton<OverlayViewModel>();

                // Windows
                services.AddSingleton<OverlayWindow>();
            })
            .Build();

        await _host.StartAsync();

        // Show overlay window
        var overlayWindow = _host.Services.GetRequiredService<OverlayWindow>();
        overlayWindow.Show();

        // Setup system tray
        SetupTrayIcon(overlayWindow);
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

        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(hideItem);
        contextMenu.Items.Add(new System.Windows.Controls.Separator());
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;

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
