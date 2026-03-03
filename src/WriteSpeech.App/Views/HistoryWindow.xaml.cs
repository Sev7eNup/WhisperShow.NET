using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Options;
using WriteSpeech.App.ViewModels;
using WriteSpeech.Core.Configuration;

namespace WriteSpeech.App.Views;

public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel _viewModel;
    private readonly IDisposable? _optionsChangeRegistration;

    public HistoryWindow(HistoryViewModel viewModel, IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        ClipBorder.SizeChanged += OnClipBorderSizeChanged;

        // Apply initial theme
        ApplyTheme(string.Equals(optionsMonitor.CurrentValue.App.Theme, "Dark", StringComparison.OrdinalIgnoreCase));

        // Listen for theme changes
        _optionsChangeRegistration = optionsMonitor.OnChange(opts =>
        {
            Dispatcher.Invoke(() =>
                ApplyTheme(string.Equals(opts.App.Theme, "Dark", StringComparison.OrdinalIgnoreCase)));
        });
    }

    private void ApplyTheme(bool isDark) => ThemeHelper.Apply(this, isDark);

    private void OnClipBorderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ClipBorder.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 12, 12);
    }

    public void Cleanup()
    {
        ClipBorder.SizeChanged -= OnClipBorderSizeChanged;
        _optionsChangeRegistration?.Dispose();
    }

    public void ShowAndRefresh()
    {
        _viewModel.Refresh();
        Show();
        Activate();
        SearchBox.Focus();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SearchBorder.BorderBrush = (Brush)FindResource("SelectedBorderBrush");
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SearchBorder.BorderBrush = Brushes.Transparent;
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
