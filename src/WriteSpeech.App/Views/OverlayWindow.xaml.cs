using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.App.ViewModels;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.Hotkey;

namespace WriteSpeech.App.Views;

public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly ILogger<OverlayWindow> _logger;
    private readonly WriteSpeechOptions _options;
    private readonly IDisposable? _optionsChangeRegistration;
    private const int ViewModelWaveformCount = 20;

    // Storyboards
    private Storyboard? _breathingStoryboard;
    private Storyboard? _sweepStoryboard;

    // Cached brushes
    private Brush? _idleBackground;
    private Brush? _glassBackground;
    private Brush? _idleBorderBrush;
    private Brush? _recordingWaveStroke;
    private Brush? _commandWaveStroke;
    private SolidColorBrush? _animatedBorderBrush;

    public OverlayWindow(OverlayViewModel viewModel,
        IGlobalHotkeyService hotkeyService,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor,
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

    private void OnWaveformUpdated(object? sender, EventArgs e) => Dispatcher.Invoke(UpdateWaveformPath);

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

    private void OnOptionsChanged(WriteSpeechOptions options, string? name)
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

        var handle = new WindowInteropHelper(this).Handle;
        ApplyTaskbarVisibility(handle, _options.Overlay.ShowInTaskbar);

        _hotkeyService.Register(handle);

        // Cache storyboards
        _breathingStoryboard = (Storyboard)FindResource("IdleBreathingAnimation");
        _sweepStoryboard = (Storyboard)FindResource("SweepAnimation");

        // Cache brushes
        _idleBackground = (Brush)FindResource("IdleBackground");
        _glassBackground = (Brush)FindResource("GlassBackground");
        _idleBorderBrush = (Brush)FindResource("IdleBorderBrush");
        _recordingWaveStroke = (Brush)FindResource("RecordingWaveStroke");
        _commandWaveStroke = (Brush)FindResource("CommandWaveStroke");

        // Setup idle hover effect
        SetupIdleHoverEffect();

        // Apply initial overlay scale
        ApplyOverlayScale(_options.Overlay.Scale);

        // Force correct visual state
        UpdateVisualState(_viewModel.State);

        // Force SizeToContent recalculation
        SizeToContent = SizeToContent.Manual;
        SizeToContent = SizeToContent.WidthAndHeight;

        // Position: defer to after layout pass
        Dispatcher.BeginInvoke(() =>
        {
            RestorePosition();
            _logger.LogInformation("Overlay positioned at ({Left}, {Top})", Left, Top);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // Wave bar original heights for hover animation
    private static readonly (string Name, double Height)[] WaveBarSpecs =
    [
        ("WaveL3", 4), ("WaveL2", 7), ("WaveL1", 10),
        ("WaveR1", 10), ("WaveR2", 7), ("WaveR3", 4),
    ];

    private void SetupIdleHoverEffect()
    {
        MouseEnter += (_, _) =>
        {
            if (_viewModel.State != RecordingState.Idle) return;
            _breathingStoryboard?.Stop(this);
            var scaleUp = new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
            BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);
            AnimateWaveBars(pulse: true);
        };

        MouseLeave += (_, _) =>
        {
            if (_viewModel.State != RecordingState.Idle) return;
            var scaleDown = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            scaleDown.Completed += (_, _) =>
            {
                if (_viewModel.State == RecordingState.Idle)
                    _breathingStoryboard?.Begin(this, true);
            };
            BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleDown);
            BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDown);
            AnimateWaveBars(pulse: false);
        };
    }

    private void AnimateWaveBars(bool pulse)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        for (int i = 0; i < WaveBarSpecs.Length; i++)
        {
            var (name, baseHeight) = WaveBarSpecs[i];
            if (FindName(name) is not Rectangle bar) continue;

            if (pulse)
            {
                // Staggered pulse: grow then return
                var grow = new DoubleAnimationUsingKeyFrames();
                double peakHeight = baseHeight + 5;
                grow.KeyFrames.Add(new EasingDoubleKeyFrame(peakHeight,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150 + i * 40)),
                    ease));
                grow.KeyFrames.Add(new EasingDoubleKeyFrame(baseHeight,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(350 + i * 40)),
                    ease));
                bar.BeginAnimation(FrameworkElement.HeightProperty, grow);
            }
            else
            {
                // Snap back to base height
                var reset = new DoubleAnimation(baseHeight, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = ease
                };
                bar.BeginAnimation(FrameworkElement.HeightProperty, reset);
            }
        }
    }

    // --- Waveform Path Generation ---

    private void UpdateWaveformPath()
    {
        var levels = _viewModel.GetWaveformLevels();
        var (line, fill) = GenerateWaveformPaths(levels, 80, 30);
        WaveformLine.Data = line;
        WaveformFill.Data = fill;
    }

    internal static (double x, double y)[] ComputeWaveformPoints(
        float[] levels, double canvasWidth, double canvasHeight)
    {
        int count = levels.Length;
        var points = new (double x, double y)[count];
        double centerY = canvasHeight / 2.0;
        double maxAmplitude = canvasHeight / 2.0 - 1.0;

        for (int i = 0; i < count; i++)
        {
            double x = count > 1 ? i * canvasWidth / (count - 1) : canvasWidth / 2.0;
            float scaled = MathF.Min(MathF.Sqrt(levels[i] * 5.0f), 1.0f);
            double amplitude = scaled * maxAmplitude;
            int sign = (i % 2 == 0) ? 1 : -1;
            points[i] = (x, centerY - sign * amplitude);
        }
        return points;
    }

    internal static (StreamGeometry line, StreamGeometry fill) GenerateWaveformPaths(
        float[] levels, double canvasWidth, double canvasHeight)
    {
        var points = ComputeWaveformPoints(levels, canvasWidth, canvasHeight);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            if (points.Length > 0)
            {
                ctx.BeginFigure(new Point(points[0].x, points[0].y), false, false);
                for (int i = 0; i < points.Length - 1; i++)
                {
                    var p0 = i > 0 ? points[i - 1] : points[i];
                    var p1 = points[i];
                    var p2 = points[i + 1];
                    var p3 = i + 2 < points.Length ? points[i + 2] : points[i + 1];

                    var cp1 = new Point(p1.x + (p2.x - p0.x) / 6.0, p1.y + (p2.y - p0.y) / 6.0);
                    var cp2 = new Point(p2.x - (p3.x - p1.x) / 6.0, p2.y - (p3.y - p1.y) / 6.0);
                    ctx.BezierTo(cp1, cp2, new Point(p2.x, p2.y), true, true);
                }
            }
        }
        line.Freeze();

        var fill = new StreamGeometry();
        using (var ctx = fill.Open())
        {
            if (points.Length > 0)
            {
                ctx.BeginFigure(new Point(points[0].x, points[0].y), true, true);
                for (int i = 0; i < points.Length - 1; i++)
                {
                    var p0 = i > 0 ? points[i - 1] : points[i];
                    var p1 = points[i];
                    var p2 = points[i + 1];
                    var p3 = i + 2 < points.Length ? points[i + 2] : points[i + 1];

                    var cp1 = new Point(p1.x + (p2.x - p0.x) / 6.0, p1.y + (p2.y - p0.y) / 6.0);
                    var cp2 = new Point(p2.x - (p3.x - p1.x) / 6.0, p2.y - (p3.y - p1.y) / 6.0);
                    ctx.BezierTo(cp1, cp2, new Point(p2.x, p2.y), true, true);
                }
                ctx.LineTo(new Point(canvasWidth, canvasHeight), false, false);
                ctx.LineTo(new Point(0, canvasHeight), false, false);
            }
        }
        fill.Freeze();

        return (line, fill);
    }

    // --- Legacy waveform interpolation (kept for existing tests) ---

    internal static double[] InterpolateWaveformLevels(float[] levels, int barCount)
    {
        var heights = new double[barCount];
        InterpolateWaveformLevels(levels, heights);
        return heights;
    }

    internal static void InterpolateWaveformLevels(float[] levels, double[] heights)
    {
        int srcCount = levels.Length;
        int barCount = heights.Length;
        for (int i = 0; i < barCount; i++)
        {
            double srcIndex = i * (srcCount - 1.0) / (barCount - 1.0);
            int lo = (int)srcIndex;
            int hi = Math.Min(lo + 1, srcCount - 1);
            double frac = srcIndex - lo;
            float interpolated = (float)(levels[lo] * (1 - frac) + levels[hi] * frac);

            float level = MathF.Min(MathF.Sqrt(interpolated * 5.0f), 1.0f);
            heights[i] = Math.Max(2, level * 28);
        }
    }

    // --- Visual State Management ---

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

        // Stop all running animations
        _breathingStoryboard?.Stop(this);
        _sweepStoryboard?.Stop(this);

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
                return;
            }
        }

        // Update glass appearance for current state
        UpdateGlassAppearance(state);

        switch (state)
        {
            case RecordingState.Idle:
                IdlePanel.Visibility = Visibility.Visible;
                _viewModel.ClearWaveform();
                var shrink = new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                shrink.Completed += (_, _) =>
                {
                    if (_viewModel.State == RecordingState.Idle)
                        _breathingStoryboard?.Begin(this, true);
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
                _sweepStoryboard?.Begin(this, true);
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

    private void UpdateGlassAppearance(RecordingState state)
    {
        // Stop any running border color animation
        _animatedBorderBrush?.BeginAnimation(SolidColorBrush.ColorProperty, null);

        switch (state)
        {
            case RecordingState.Recording when _viewModel.IsCommandModeActive:
                GlassPill.Background = _glassBackground;
                StartBorderGlow(
                    Color.FromArgb(0xCC, 0x5C, 0x6B, 0xC0),
                    Color.FromArgb(0x80, 0x5C, 0x6B, 0xC0),
                    Color.FromArgb(0x40, 0x5C, 0x6B, 0xC0));
                WaveformLine.Stroke = _commandWaveStroke;
                WaveFillTopStop.Color = Color.FromArgb(0x40, 0x5C, 0x6B, 0xC0);
                break;
            case RecordingState.Recording:
                GlassPill.Background = _glassBackground;
                StartBorderGlow(
                    Color.FromArgb(0xCC, 0xEF, 0x53, 0x50),
                    Color.FromArgb(0x80, 0xEF, 0x53, 0x50),
                    Color.FromArgb(0x40, 0xEF, 0x53, 0x50));
                WaveformLine.Stroke = _recordingWaveStroke;
                WaveFillTopStop.Color = Color.FromArgb(0x40, 0xEF, 0x53, 0x50);
                break;
            case RecordingState.Idle:
                GlassPill.Background = _idleBackground;
                GlassPill.BorderBrush = _idleBorderBrush;
                GlassPill.BorderThickness = new Thickness(1);
                ResetShadowToDefault();
                break;
            default: // Transcribing, Result, Error
                GlassPill.Background = _glassBackground;
                GlassPill.BorderBrush = _idleBorderBrush;
                GlassPill.BorderThickness = new Thickness(1);
                ResetShadowToDefault();
                break;
        }
    }

    private void StartBorderGlow(Color borderFrom, Color borderTo, Color glowColor)
    {
        _animatedBorderBrush = new SolidColorBrush(borderFrom);
        GlassPill.BorderBrush = _animatedBorderBrush;
        GlassPill.BorderThickness = new Thickness(1.5);

        // Pulsing border color
        var pulse = new ColorAnimation(borderFrom, borderTo, TimeSpan.FromMilliseconds(800))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        _animatedBorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, pulse);

        // Glow shadow
        PillShadow.BeginAnimation(DropShadowEffect.ColorProperty,
            new ColorAnimation(glowColor, TimeSpan.FromMilliseconds(300)));
        PillShadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
            new DoubleAnimation(20, TimeSpan.FromMilliseconds(300)));
        PillShadow.BeginAnimation(DropShadowEffect.OpacityProperty,
            new DoubleAnimation(0.6, TimeSpan.FromMilliseconds(300)));
        PillShadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
    }

    private void ResetShadowToDefault()
    {
        PillShadow.BeginAnimation(DropShadowEffect.ColorProperty,
            new ColorAnimation(Colors.Black, TimeSpan.FromMilliseconds(200)));
        PillShadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
            new DoubleAnimation(16, TimeSpan.FromMilliseconds(200)));
        PillShadow.BeginAnimation(DropShadowEffect.OpacityProperty,
            new DoubleAnimation(0.35, TimeSpan.FromMilliseconds(200)));
        PillShadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty,
            new DoubleAnimation(4, TimeSpan.FromMilliseconds(200)));
    }

    private void AnimateStateTransition()
    {
        var duration = TimeSpan.FromMilliseconds(250);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var scaleX = new DoubleAnimation(0.92, 1.0, duration) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(0.92, 1.0, duration) { EasingFunction = ease };

        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }

    private void AnimateResultAppear()
    {
        var duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        ResultPanel.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };
        ResultPanel.BeginAnimation(OpacityProperty, fadeIn);

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

    // --- Scale ---

    internal static double ClampOverlayScale(double scale) => Math.Clamp(scale, 0.75, 2.0);

    private void ApplyOverlayScale(double scale)
    {
        scale = ClampOverlayScale(scale);
        OverlayScaleTransform.ScaleX = scale;
        OverlayScaleTransform.ScaleY = scale;
    }

    // --- Hotkey Handlers ---

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

    // --- Taskbar Visibility ---

    private void ApplyTaskbarVisibility(IntPtr handle, bool showInTaskbar)
    {
        int exStyle = NativeMethods.GetWindowLongW(handle, NativeMethods.GWL_EXSTYLE);

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

        NativeMethods.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);

        _logger.LogDebug("Taskbar visibility set to {Value} (Win32 styles updated)", showInTaskbar);
    }

    // --- Drag Support ---

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

    // --- Tooltip Placement ---

    private CustomPopupPlacement[] CenterAbovePlacement(Size popupSize, Size targetSize, Point offset)
    {
        var x = (targetSize.Width - popupSize.Width) / 2;
        var y = -popupSize.Height - 4;
        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal)];
    }

    // --- Button Click Handlers ---

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

    // --- Position ---

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
            Cleanup();
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
