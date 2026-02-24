using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly IOptionsMonitor<WhisperShowOptions> _optionsMonitor;
    private readonly ILogger<OverlayWindow> _logger;
    private readonly WhisperShowOptions _options;
    private readonly IDisposable? _optionsChangeRegistration;
    private Storyboard? _typingDotsStoryboard;
    private const int WaveformBarCount = 16;
    private const int ViewModelWaveformCount = 20;
    private readonly Rectangle[] _waveformBars = new Rectangle[WaveformBarCount];

    // Cached brushes for state changes
    private Brush? _idleGradient;
    private Brush? _recordingGradient;
    private Brush? _idleTailBrush;
    private Brush? _recordingTailBrush;

    public OverlayWindow(OverlayViewModel viewModel,
        IGlobalHotkeyService hotkeyService,
        IOptionsMonitor<WhisperShowOptions> optionsMonitor,
        ILogger<OverlayWindow> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _hotkeyService = hotkeyService;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
        _options = optionsMonitor.CurrentValue;
        DataContext = _viewModel;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.WaveformUpdated += OnWaveformUpdated;
        _hotkeyService.ToggleHotkeyPressed += OnToggleHotkeyPressed;
        _hotkeyService.PushToTalkHotkeyPressed += OnPushToTalkHotkeyPressed;
        _hotkeyService.PushToTalkHotkeyReleased += OnPushToTalkHotkeyReleased;
        _hotkeyService.EscapePressed += OnEscapePressed;

        _optionsChangeRegistration = _optionsMonitor.OnChange(OnOptionsChanged);

        Loaded += OverlayWindow_Loaded;
    }

    private void OnWaveformUpdated(object? sender, EventArgs e) => Dispatcher.Invoke(UpdateWaveformBars);

    public void Cleanup()
    {
        _optionsChangeRegistration?.Dispose();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.WaveformUpdated -= OnWaveformUpdated;
        _hotkeyService.ToggleHotkeyPressed -= OnToggleHotkeyPressed;
        _hotkeyService.PushToTalkHotkeyPressed -= OnPushToTalkHotkeyPressed;
        _hotkeyService.PushToTalkHotkeyReleased -= OnPushToTalkHotkeyReleased;
        _hotkeyService.EscapePressed -= OnEscapePressed;
    }

    private void OnOptionsChanged(WhisperShowOptions options, string? name)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyOverlayScale(options.Overlay.Scale);

            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
                ApplyTaskbarVisibility(handle, options.Overlay.ShowInTaskbar);
        });
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
        _typingDotsStoryboard = (Storyboard)FindResource("TypingDotsAnimation");

        // Cache brushes
        _idleGradient = (Brush)FindResource("IdleGradient");
        _recordingGradient = (Brush)FindResource("RecordingGradient");
        _idleTailBrush = (Brush)FindResource("IdleTailBrush");
        _recordingTailBrush = (Brush)FindResource("RecordingTailBrush");

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

        // Position: defer to after layout pass so ActualWidth/Height are computed
        Dispatcher.BeginInvoke(() =>
        {
            RestorePosition();
            _logger.LogInformation("Overlay positioned at ({Left}, {Top})", Left, Top);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void SetupIdleHoverEffect()
    {
        MouseEnter += (_, _) =>
        {
            if (_viewModel.State != RecordingState.Idle) return;
            var scaleUp = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
            BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);
        };

        MouseLeave += (_, _) =>
        {
            if (_viewModel.State != RecordingState.Idle) return;
            var scaleDown = new DoubleAnimation(0.85, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleDown);
            BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDown);
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
        var heights = InterpolateWaveformLevels(levels, WaveformBarCount);
        for (int i = 0; i < WaveformBarCount; i++)
        {
            _waveformBars[i].Height = heights[i];
            Canvas.SetTop(_waveformBars[i], (30 - heights[i]) / 2); // center vertically
        }
    }

    internal static double[] InterpolateWaveformLevels(float[] levels, int barCount)
    {
        var heights = new double[barCount];
        int srcCount = levels.Length;
        for (int i = 0; i < barCount; i++)
        {
            // Map barCount bars to srcCount levels via linear interpolation
            double srcIndex = i * (srcCount - 1.0) / (barCount - 1.0);
            int lo = (int)srcIndex;
            int hi = Math.Min(lo + 1, srcCount - 1);
            double frac = srcIndex - lo;
            float interpolated = (float)(levels[lo] * (1 - frac) + levels[hi] * frac);

            // Square-root scaling: compresses dynamic range so quiet speech is visible
            float level = MathF.Min(MathF.Sqrt(interpolated * 5.0f), 1.0f);
            heights[i] = Math.Max(2, level * 28);
        }
        return heights;
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
        _typingDotsStoryboard?.Stop(this);

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
                var shrink = new DoubleAnimation(0.85, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
                BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
                break;
            case RecordingState.Recording:
                RecordingPanel.Visibility = Visibility.Visible;
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
            default: // Idle, Transcribing, Result, Error
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

    internal static double ClampOverlayScale(double scale) => Math.Clamp(scale, 0.75, 2.0);

    private void ApplyOverlayScale(double scale)
    {
        scale = ClampOverlayScale(scale);
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
            _viewModel.UpdatePosition(Left, Top);
        }
    }

    private void RootGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
    }

    private CustomPopupPlacement[] CenterAbovePlacement(Size popupSize, Size targetSize, Point offset)
    {
        var x = (targetSize.Width - popupSize.Width) / 2;
        var y = -popupSize.Height - 4;
        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal)];
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
        if (_viewModel.PositionX >= 0 && _viewModel.PositionY >= 0)
        {
            Left = _viewModel.PositionX;
            Top = _viewModel.PositionY;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Left = (workArea.Width - ActualWidth) / 2 + workArea.Left;
            Top = workArea.Bottom - ActualHeight - 10;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (Application.Current?.ShutdownMode == ShutdownMode.OnExplicitShutdown
            || Application.Current?.MainWindow == null)
        {
            // App is shutting down — allow close and run cleanup
            Cleanup();
            return;
        }

        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
    }
}
