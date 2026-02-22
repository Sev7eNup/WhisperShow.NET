using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

    private void ProviderCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string providerName)
            _viewModel.SelectProviderCommand.Execute(providerName);
    }

    private void CorrectionProviderCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string providerName)
            _viewModel.SelectCorrectionProviderCommand.Execute(providerName);
    }

    // --- Inline editing handlers (System & Transcription pages) ---

    private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is TranscriptionProvider provider && _viewModel.IsEditingProvider)
            _viewModel.ApplyProvider(provider);
    }

    private void EndpointTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            _viewModel.ApplyEndpoint(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.IsEditingEndpoint = false;
            e.Handled = true;
        }
    }

    private void ApiKeyTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            _viewModel.ApplyApiKey(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.IsEditingApiKey = false;
            e.Handled = true;
        }
    }

    private void ModelTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            _viewModel.ApplyModel(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.IsEditingModel = false;
            e.Handled = true;
        }
    }

    private void CorrectionModelTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            _viewModel.ApplyCorrectionModel(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.IsEditingCorrectionModel = false;
            e.Handled = true;
        }
    }

    private void AutoDismissTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && int.TryParse(tb.Text, out var seconds))
        {
            _viewModel.ApplyAutoDismiss(Math.Max(1, seconds));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.IsEditingAutoDismiss = false;
            e.Handled = true;
        }
    }

    private void MaxRecordingTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && int.TryParse(tb.Text, out var seconds))
        {
            _viewModel.ApplyMaxRecording(Math.Max(10, seconds));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.IsEditingMaxRecording = false;
            e.Handled = true;
        }
    }

    private void CombinedAudioModelTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            _viewModel.ApplyCombinedAudioModel(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.IsEditingCombinedAudioModel = false;
            e.Handled = true;
        }
    }

    private void DictionaryTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.AddDictionaryEntryCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SnippetTriggerTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && sender is TextBox)
        {
            // Move focus to replacement textbox (next focusable element)
            var request = new TraversalRequest(FocusNavigationDirection.Next);
            (sender as UIElement)?.MoveFocus(request);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _viewModel.AddSnippetCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SnippetReplacementTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _viewModel.AddSnippetCommand.Execute(null);
            e.Handled = true;
        }
    }
}

// --- Value Converters ---

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToEnabledDisabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is string p && p == "capturing")
            return value is true ? "Listening for keys..." : "Rebind";
        return value is true ? "Enabled" : "Disabled";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class SecondsToMinutesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int seconds ? (seconds / 60).ToString() : "0";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ProviderToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TranscriptionProvider provider && parameter is string expected)
            return provider.ToString() == expected ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class StringEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var str = value?.ToString();
        if (str is not null && parameter is string expected)
            return str == expected ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class CapturingHotkeyTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string capturing && parameter is string target)
            return capturing == target ? "Listening for keys..." : "Rebind";
        return "Rebind";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Show Download button when !IsDownloaded and !IsDownloading.</summary>
public class ModelActionVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is bool isDownloaded && values[1] is bool isDownloading)
            return !isDownloaded && !isDownloading ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Show Delete button when IsDownloaded and !IsActive and !IsDownloading.</summary>
public class ModelDeleteVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 3 && values[0] is bool isDownloaded && values[1] is bool isDownloading && values[2] is bool isActive)
            return isDownloaded && !isDownloading && !isActive ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Show Use button when IsDownloaded and !IsActive and !IsDownloading.</summary>
public class ModelUseVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 3 && values[0] is bool isDownloaded && values[1] is bool isDownloading && values[2] is bool isActive)
            return isDownloaded && !isDownloading && !isActive ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
