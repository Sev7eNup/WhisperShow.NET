using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WhisperShow.App.ViewModels;
using WhisperShow.Core.Models;

namespace WhisperShow.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        ClipBorder.SizeChanged += (_, e) =>
        {
            ClipBorder.Clip = new RectangleGeometry(
                new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 12, 12);
        };
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        IsVisibleChanged += (_, _) => { if (!IsVisible) _viewModel.StopMicTest(); };

        // Apply initial theme
        ApplyTheme(_viewModel.IsDarkMode);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    // --- Dialog highlight management ---

    private void ApplyTheme(bool isDark)
    {
        var themePath = isDark
            ? "/Themes/SettingsDarkTheme.xaml"
            : "/Themes/SettingsLightTheme.xaml";

        var themeUri = new Uri(themePath, UriKind.Relative);
        var themeDict = new ResourceDictionary { Source = themeUri };

        // Replace the first merged dictionary (the theme one)
        var merged = Resources.MergedDictionaries;
        if (merged.Count > 0)
            merged[0] = themeDict;
        else
            merged.Insert(0, themeDict);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsDarkMode))
        {
            Dispatcher.Invoke(() => ApplyTheme(_viewModel.IsDarkMode));
        }

        if (e.PropertyName is nameof(SettingsViewModel.ActiveDialog) or nameof(SettingsViewModel.IsDialogOpen))
        {
            if (_viewModel.IsDialogOpen)
            {
                if (_viewModel.ActiveDialog == "Microphone")
                    Dispatcher.BeginInvoke(UpdateMicrophoneHighlight, System.Windows.Threading.DispatcherPriority.Loaded);
                else if (_viewModel.ActiveDialog == "Language")
                    Dispatcher.BeginInvoke(UpdateLanguageHighlight, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        if (e.PropertyName is nameof(SettingsViewModel.PendingLanguageCode) or nameof(SettingsViewModel.IsAutoDetectLanguage))
        {
            if (_viewModel.IsDialogOpen && _viewModel.ActiveDialog == "Language")
                UpdateLanguageHighlight();
        }
    }

    private void UpdateMicrophoneHighlight()
    {
        var selectedBrush = (SolidColorBrush)FindResource("SelectedBorderBrush");
        for (int i = 0; i < MicrophoneList.Items.Count; i++)
        {
            if (MicrophoneList.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter container)
                continue;
            if (VisualTreeHelper.GetChildrenCount(container) == 0) continue;
            if (VisualTreeHelper.GetChild(container, 0) is not Border border) continue;
            var mic = (MicrophoneInfo)MicrophoneList.Items[i]!;
            border.BorderBrush = mic.DeviceIndex == _viewModel.SelectedMicrophoneIndex
                ? selectedBrush
                : Brushes.Transparent;
        }
    }

    private void UpdateLanguageHighlight()
    {
        var selectedBrush = (SolidColorBrush)FindResource("SelectedBorderBrush");
        for (int i = 0; i < LanguageList.Items.Count; i++)
        {
            if (LanguageList.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter container)
                continue;
            if (VisualTreeHelper.GetChildrenCount(container) == 0) continue;
            if (VisualTreeHelper.GetChild(container, 0) is not Border border) continue;
            var lang = (LanguageInfo)LanguageList.Items[i]!;
            border.BorderBrush = (!_viewModel.IsAutoDetectLanguage && lang.Code == _viewModel.PendingLanguageCode)
                ? selectedBrush
                : Brushes.Transparent;
        }
    }

    // --- Hotkey capture ---

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (string.IsNullOrEmpty(_viewModel.CapturingHotkey)) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore modifier-only presses
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        // Escape cancels
        if (key == Key.Escape)
        {
            _viewModel.CapturingHotkey = "";
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var modList = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) modList.Add("Control");
        if (modifiers.HasFlag(ModifierKeys.Shift)) modList.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) modList.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) modList.Add("Windows");

        // Require at least one modifier
        if (modList.Count == 0) return;

        _viewModel.ApplyNewHotkey(string.Join(", ", modList), key.ToString());
        e.Handled = true;
    }

    // --- Dialog overlay ---

    private void DialogOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.CloseDialogCommand.Execute(null);
    }

    private void RebindToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.StartCapturingToggleHotkeyCommand.Execute(null);
    }

    private void RebindPtt_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.StartCapturingPttHotkeyCommand.Execute(null);
    }

    private void MicrophoneItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: MicrophoneInfo mic })
            _viewModel.SelectMicrophone(mic.DeviceIndex);
    }

    private void LanguageCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string code)
            _viewModel.SelectLanguageCommand.Execute(code);
    }
}
