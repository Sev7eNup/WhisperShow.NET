using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using WhisperShow.App.ViewModels;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services.Hotkey;

namespace WhisperShow.App.Views;

public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private readonly IGlobalHotkeyService _hotkeyService;
    private Storyboard? _pulseStoryboard;
    private Storyboard? _spinStoryboard;

    public OverlayWindow(OverlayViewModel viewModel, IGlobalHotkeyService hotkeyService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _hotkeyService = hotkeyService;
        DataContext = _viewModel;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        Loaded += OverlayWindow_Loaded;
    }

    private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Set WS_EX_NOACTIVATE and WS_EX_TOOLWINDOW so the overlay doesn't steal focus
        var handle = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLongW(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongW(handle, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

        // Register global hotkey
        _hotkeyService.Register(handle);

        // Cache storyboards
        _pulseStoryboard = (Storyboard)FindResource("PulseAnimation");
        _spinStoryboard = (Storyboard)FindResource("SpinAnimation");
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
        // Stop all animations
        _pulseStoryboard?.Stop(this);
        _spinStoryboard?.Stop(this);

        // Hide all panels
        IdlePanel.Visibility = Visibility.Collapsed;
        RecordingPanel.Visibility = Visibility.Collapsed;
        TranscribingPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;

        switch (state)
        {
            case RecordingState.Idle:
                IdlePanel.Visibility = Visibility.Visible;
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

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(async () => await _viewModel.ToggleRecordingCommand.ExecuteAsync(null));
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

    protected override void OnClosing(CancelEventArgs e)
    {
        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
    }
}
