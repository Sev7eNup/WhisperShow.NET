using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhisperShow.App.ViewModels;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services.Hotkey;

namespace WhisperShow.App.Views;

public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly ILogger<OverlayWindow> _logger;
    private readonly WhisperShowOptions _options;
    private Storyboard? _pulseStoryboard;
    private Storyboard? _spinStoryboard;
    private const int WaveformBarCount = 20;
    private readonly Rectangle[] _waveformBars = new Rectangle[WaveformBarCount];
    private CancellationTokenSource? _saveCts;

    public OverlayWindow(OverlayViewModel viewModel, SettingsViewModel settingsViewModel,
        IGlobalHotkeyService hotkeyService,
        IOptions<WhisperShowOptions> options, ILogger<OverlayWindow> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _settingsViewModel = settingsViewModel;
        _hotkeyService = hotkeyService;
        _logger = logger;
        _options = options.Value;
        DataContext = _viewModel;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.WaveformUpdated += (_, _) => Dispatcher.Invoke(UpdateWaveformBars);
        _hotkeyService.ToggleHotkeyPressed += OnToggleHotkeyPressed;
        _hotkeyService.PushToTalkHotkeyPressed += OnPushToTalkHotkeyPressed;
        _hotkeyService.PushToTalkHotkeyReleased += OnPushToTalkHotkeyReleased;
        _settingsViewModel.PropertyChanged += OnSettingsChanged;

        Loaded += OverlayWindow_Loaded;
    }

    private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("OverlayWindow loaded, configuring WS_EX_NOACTIVATE");

        // Configure extended window styles via Win32 (not WPF's ShowInTaskbar, which recreates the HWND).
        var handle = new WindowInteropHelper(this).Handle;
        ApplyTaskbarVisibility(handle, _options.Overlay.ShowInTaskbar);

        // Register global hotkey
        _hotkeyService.Register(handle);

        // Cache storyboards
        _pulseStoryboard = (Storyboard)FindResource("PulseAnimation");
        _spinStoryboard = (Storyboard)FindResource("SpinAnimation");

        // Create waveform bars
        CreateWaveformBars();

        // Position: restore saved or default to bottom-center
        RestorePosition();
        _logger.LogInformation("Overlay positioned at ({Left}, {Top})", Left, Top);
    }

    private void CreateWaveformBars()
    {
        var barBrush = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
        barBrush.Freeze();
        for (int i = 0; i < WaveformBarCount; i++)
        {
            var bar = new Rectangle
            {
                Width = 2,
                Height = 2,
                Fill = barBrush,
                RadiusX = 1,
                RadiusY = 1
            };
            Canvas.SetLeft(bar, i * 3.5);
            Canvas.SetTop(bar, 13); // centered vertically in 28px canvas
            _waveformBars[i] = bar;
            WaveformCanvas.Children.Add(bar);
        }
    }

    private void UpdateWaveformBars()
    {
        var levels = _viewModel.GetWaveformLevels();
        for (int i = 0; i < WaveformBarCount; i++)
        {
            // Amplify: typical speech is 0.0-0.3, scale up for visibility
            float level = Math.Min(levels[i] * 3.5f, 1.0f);
            double height = Math.Max(2, level * 26);
            _waveformBars[i].Height = height;
            Canvas.SetTop(_waveformBars[i], (28 - height) / 2); // center vertically
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.State))
        {
            Dispatcher.Invoke(() => UpdateVisualState(_viewModel.State));
        }
    }

    private void UpdateVisualState(RecordingState state)
    {
        _logger.LogDebug("Visual state update: {State}", state);

        // Stop all animations
        _pulseStoryboard?.Stop(this);
        _spinStoryboard?.Stop(this);

        // Hide all panels
        IdlePanel.Visibility = Visibility.Collapsed;
        RecordingPanel.Visibility = Visibility.Collapsed;
        TranscribingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;

        // Auto-show/hide for non-always-visible mode
        if (!_viewModel.IsOverlayAlwaysVisible)
        {
            if (state is RecordingState.Recording or RecordingState.Transcribing
                or RecordingState.Result or RecordingState.Error)
            {
                if (!IsVisible)
                {
                    Show();
                    RestorePosition();
                }
            }
            else if (state == RecordingState.Idle)
            {
                Hide();
                return; // No need to update panels if hidden
            }
        }

        switch (state)
        {
            case RecordingState.Idle:
                IdlePanel.Visibility = Visibility.Visible;
                _viewModel.ClearWaveform();
                break;
            case RecordingState.Recording:
                RecordingPanel.Visibility = Visibility.Visible;
                _pulseStoryboard?.Begin(this, true);
                break;
            case RecordingState.Transcribing:
                TranscribingPanel.Visibility = Visibility.Visible;
                _spinStoryboard?.Begin(this, true);
                break;
            case RecordingState.Result:
                ResultPanel.Visibility = Visibility.Visible;
                break;
            case RecordingState.Error:
                ErrorPanel.Visibility = Visibility.Visible;
                break;
        }
    }

    private void OnToggleHotkeyPressed(object? sender, EventArgs e)
    {
        _logger.LogDebug("Toggle hotkey event received in OverlayWindow");
        Dispatcher.Invoke(async () => await _viewModel.ToggleRecordingCommand.ExecuteAsync(null));
    }

    private void OnPushToTalkHotkeyPressed(object? sender, EventArgs e)
    {
        _logger.LogDebug("Push-to-Talk pressed event received in OverlayWindow");
        Dispatcher.Invoke(async () => await _viewModel.HotkeyStartRecordingAsync());
    }

    private void OnPushToTalkHotkeyReleased(object? sender, EventArgs e)
    {
        _logger.LogDebug("Push-to-Talk released event received in OverlayWindow");
        Dispatcher.Invoke(async () => await _viewModel.HotkeyStopRecordingAsync());
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.ShowInTaskbar))
        {
            Dispatcher.Invoke(() =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                    ApplyTaskbarVisibility(handle, _settingsViewModel.ShowInTaskbar);
            });
        }
    }

    private void ApplyTaskbarVisibility(IntPtr handle, bool showInTaskbar)
    {
        int exStyle = NativeMethods.GetWindowLongW(handle, NativeMethods.GWL_EXSTYLE);

        // Always set WS_EX_NOACTIVATE so the overlay doesn't steal focus
        exStyle |= NativeMethods.WS_EX_NOACTIVATE;

        if (showInTaskbar)
        {
            exStyle &= ~NativeMethods.WS_EX_TOOLWINDOW;
            exStyle |= NativeMethods.WS_EX_APPWINDOW;
        }
        else
        {
            exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            exStyle &= ~NativeMethods.WS_EX_APPWINDOW;
        }

        NativeMethods.SetWindowLongW(handle, NativeMethods.GWL_EXSTYLE, exStyle);

        // Notify the shell to refresh the taskbar entry
        NativeMethods.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);

        _logger.LogDebug("Taskbar visibility set to {Value} (Win32 styles updated)", showInTaskbar);
    }

    // Drag support: track mouse start position, only DragMove if mouse actually moves
    private Point? _dragStart;

    private void MainBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
    }

    private void MainBorder_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(this);
        var diff = pos - _dragStart.Value;

        if (Math.Abs(diff.X) > 5 || Math.Abs(diff.Y) > 5)
        {
            _dragStart = null;
            if (Mouse.Captured is UIElement captured)
                captured.ReleaseMouseCapture();
            DragMove();
            _options.Overlay.PositionX = Left;
            _options.Overlay.PositionY = Top;
            SavePositionAsync();
        }
    }

    private void MainBorder_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.ToggleRecordingCommand.ExecuteAsync(null);
    }

    private void InsertButton_Click(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.InsertTextCommand.ExecuteAsync(null);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CopyTextCommand.Execute(null);
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DismissResultCommand.Execute(null);
    }

    private void RestorePosition()
    {
        if (_options.Overlay.PositionX >= 0 && _options.Overlay.PositionY >= 0)
        {
            Left = _options.Overlay.PositionX;
            Top = _options.Overlay.PositionY;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Left = (workArea.Width - ActualWidth) / 2 + workArea.Left;
            Top = workArea.Bottom - ActualHeight - 10;
        }
    }

    private void SavePositionAsync()
    {
        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;
        var left = Left;
        var top = Top;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                var json = await File.ReadAllTextAsync(path, token);
                var doc = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip
                })!;

                doc["WhisperShow"]!["Overlay"]!["PositionX"] = left;
                doc["WhisperShow"]!["Overlay"]!["PositionY"] = top;

                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(path, doc.ToJsonString(options), token);
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save overlay position");
            }
        }, token);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
    }
}
