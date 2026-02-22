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
    private Storyboard? _glowPulseStoryboard;
    private Storyboard? _typingDotsStoryboard;
    private const int WaveformBarCount = 16;
    private const int ViewModelWaveformCount = 20;
    private readonly Rectangle[] _waveformBars = new Rectangle[WaveformBarCount];
    private CancellationTokenSource? _saveCts;

    // Cached brushes for state changes
    private Brush? _idleGradient;
    private Brush? _recordingGradient;
    private Brush? _transcribingGradient;
    private Brush? _idleTailBrush;
    private Brush? _recordingTailBrush;
    private Brush? _transcribingTailBrush;

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
        _hotkeyService.EscapePressed += OnEscapePressed;
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
        _glowPulseStoryboard = (Storyboard)FindResource("GlowPulseAnimation");
        _typingDotsStoryboard = (Storyboard)FindResource("TypingDotsAnimation");

        // Cache brushes
        _idleGradient = (Brush)FindResource("IdleGradient");
        _recordingGradient = (Brush)FindResource("RecordingGradient");
        _transcribingGradient = (Brush)FindResource("TranscribingGradient");
        _idleTailBrush = (Brush)FindResource("IdleTailBrush");
        _recordingTailBrush = (Brush)FindResource("RecordingTailBrush");
        _transcribingTailBrush = (Brush)FindResource("TranscribingTailBrush");

        // Create waveform bars
        CreateWaveformBars();

        // Setup idle hover effect
        SetupIdleHoverEffect();

        // Apply initial overlay scale
        ApplyOverlayScale(_options.Overlay.Scale);

        // Force correct visual state (ensures proper sizing after waveform bars are created)
        UpdateVisualState(_viewModel.State);

        // Force SizeToContent recalculation — WPF caches the initial window size
        // and won't re-measure after panel visibility changes without this reset
        SizeToContent = SizeToContent.Manual;
        SizeToContent = SizeToContent.WidthAndHeight;

        // Position: restore saved or default to bottom-center
        RestorePosition();
        _logger.LogInformation("Overlay positioned at ({Left}, {Top})", Left, Top);
    }

    private void SetupIdleHoverEffect()
    {
        var idleButton = IdlePanel.Children[0] as Button;
        if (idleButton == null) return;

        idleButton.MouseEnter += (_, _) =>
        {
            var scaleUp = new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            IdleButtonScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
            IdleButtonScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);
        };

        idleButton.MouseLeave += (_, _) =>
        {
            var scaleDown = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            IdleButtonScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleDown);
            IdleButtonScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDown);
        };
    }

    private void CreateWaveformBars()
    {
        var barBrush = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
        barBrush.Freeze();
        for (int i = 0; i < WaveformBarCount; i++)
        {
            var bar = new Rectangle
            {
                Width = 3,
                Height = 2,
                Fill = barBrush,
                RadiusX = 1.5,
                RadiusY = 1.5
            };
            Canvas.SetLeft(bar, i * 4.5);
            Canvas.SetTop(bar, 14); // centered vertically in 30px canvas
            _waveformBars[i] = bar;
            WaveformCanvas.Children.Add(bar);
        }
    }

    private void UpdateWaveformBars()
    {
        var levels = _viewModel.GetWaveformLevels();
        for (int i = 0; i < WaveformBarCount; i++)
        {
            // Map 20 ViewModel levels to 16 bars via linear interpolation
            double srcIndex = i * (ViewModelWaveformCount - 1.0) / (WaveformBarCount - 1.0);
            int lo = (int)srcIndex;
            int hi = Math.Min(lo + 1, ViewModelWaveformCount - 1);
            double frac = srcIndex - lo;
            float interpolated = (float)(levels[lo] * (1 - frac) + levels[hi] * frac);

            // Amplify: typical speech is 0.0-0.3, scale up for visibility
            float level = Math.Min(interpolated * 3.5f, 1.0f);
            double height = Math.Max(2, level * 28);
            _waveformBars[i].Height = height;
            Canvas.SetTop(_waveformBars[i], (30 - height) / 2); // center vertically
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

        // Register/unregister Escape hotkey based on state
        if (state is RecordingState.Result or RecordingState.Error)
            _hotkeyService.RegisterEscapeHotkey();
        else
            _hotkeyService.UnregisterEscapeHotkey();

        // Stop all animations
        _glowPulseStoryboard?.Stop(this);
        _typingDotsStoryboard?.Stop(this);
        GlowBorder.Opacity = 0;

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

        // Update bubble colors for current state
        UpdateBubbleColors(state);

        switch (state)
        {
            case RecordingState.Idle:
                IdlePanel.Visibility = Visibility.Visible;
                _viewModel.ClearWaveform();
                break;
            case RecordingState.Recording:
                RecordingPanel.Visibility = Visibility.Visible;
                _glowPulseStoryboard?.Begin(this, true);
                AnimateStateTransition();
                break;
            case RecordingState.Transcribing:
                TranscribingPanel.Visibility = Visibility.Visible;
                _typingDotsStoryboard?.Begin(this, true);
                AnimateStateTransition();
                break;
            case RecordingState.Result:
                ResultPanel.Visibility = Visibility.Visible;
                AnimateResultAppear();
                break;
            case RecordingState.Error:
                ErrorPanel.Visibility = Visibility.Visible;
                AnimateErrorShake();
                break;
        }
    }

    private void UpdateBubbleColors(RecordingState state)
    {
        switch (state)
        {
            case RecordingState.Recording:
                BubbleBody.Background = _recordingGradient;
                BubbleTail.Fill = _recordingTailBrush;
                break;
            case RecordingState.Transcribing:
                BubbleBody.Background = _transcribingGradient;
                BubbleTail.Fill = _transcribingTailBrush;
                break;
            default: // Idle, Result, Error
                BubbleBody.Background = _idleGradient;
                BubbleTail.Fill = _idleTailBrush;
                break;
        }
    }

    private void AnimateStateTransition()
    {
        var duration = TimeSpan.FromMilliseconds(250);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var scaleX = new DoubleAnimation(0.85, 1.0, duration) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(0.85, 1.0, duration) { EasingFunction = ease };

        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }

    private void AnimateResultAppear()
    {
        var duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // Fade in
        ResultPanel.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };
        ResultPanel.BeginAnimation(OpacityProperty, fadeIn);

        // Slide up
        var slideUp = new DoubleAnimation(4, 0, duration) { EasingFunction = ease };
        ResultTranslate.BeginAnimation(TranslateTransform.YProperty, slideUp);
    }

    private void AnimateErrorShake()
    {
        var shake = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(400)
        };
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(-4, KeyTime.FromPercent(0.15)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(4, KeyTime.FromPercent(0.35)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(-3, KeyTime.FromPercent(0.55)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(3, KeyTime.FromPercent(0.75)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));

        ErrorTranslate.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    private void ApplyOverlayScale(double scale)
    {
        scale = Math.Clamp(scale, 0.75, 2.0);
        OverlayScaleTransform.ScaleX = scale;
        OverlayScaleTransform.ScaleY = scale;
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

    private void OnEscapePressed(object? sender, EventArgs e)
    {
        _logger.LogDebug("Escape hotkey event received in OverlayWindow");
        Dispatcher.Invoke(() => _viewModel.DismissResultCommand.Execute(null));
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.OverlayScale))
        {
            Dispatcher.Invoke(() => ApplyOverlayScale(_settingsViewModel.OverlayScale));
        }
        else if (e.PropertyName == nameof(SettingsViewModel.ShowInTaskbar))
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

    private void RootGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
    }

    private void RootGrid_PreviewMouseMove(object sender, MouseEventArgs e)
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

    private void RootGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
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
