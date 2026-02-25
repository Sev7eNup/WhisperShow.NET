using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WriteSpeech.App.ViewModels;
using WriteSpeech.App.ViewModels.Settings;
using WriteSpeech.Core.Services.Hotkey;

namespace WriteSpeech.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly IGlobalHotkeyService _hotkeyService;
    private HwndSource? _hwndSource;

    public SettingsWindow(SettingsViewModel viewModel, IGlobalHotkeyService hotkeyService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _hotkeyService = hotkeyService;
        DataContext = viewModel;

        ClipBorder.SizeChanged += OnClipBorderSizeChanged;
        _viewModel.General.PropertyChanged += OnGeneralPropertyChanged;
        _viewModel.System.PropertyChanged += OnSystemPropertyChanged;
        _hotkeyService.MouseButtonCaptured += OnMouseButtonCaptured;
        IsVisibleChanged += OnIsVisibleChanged;

        // Apply initial theme
        ApplyTheme(_viewModel.System.IsDarkMode);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = (HwndSource)PresentationSource.FromVisual(this)!;
    }

    private void OnClipBorderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ClipBorder.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 12, 12);
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) _viewModel.General.StopMicTest();
    }

    public void Cleanup()
    {
        ClipBorder.SizeChanged -= OnClipBorderSizeChanged;
        _viewModel.General.PropertyChanged -= OnGeneralPropertyChanged;
        _viewModel.System.PropertyChanged -= OnSystemPropertyChanged;
        _hotkeyService.MouseButtonCaptured -= OnMouseButtonCaptured;
        IsVisibleChanged -= OnIsVisibleChanged;
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
        if (Application.Current?.ShutdownMode == ShutdownMode.OnExplicitShutdown
            || Application.Current?.MainWindow == null)
        {
            Cleanup();
            return;
        }

        e.Cancel = true;
        Hide();
    }

    // --- Theme management ---

    private void ApplyTheme(bool isDark)
    {
        var themePath = isDark
            ? "/Themes/SettingsDarkTheme.xaml"
            : "/Themes/SettingsLightTheme.xaml";

        var themeUri = new Uri(themePath, UriKind.Relative);
        var themeDict = new ResourceDictionary { Source = themeUri };

        var merged = Resources.MergedDictionaries;
        if (merged.Count > 0)
            merged[0] = themeDict;
        else
            merged.Insert(0, themeDict);
    }

    private void OnSystemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemSettingsViewModel.IsDarkMode))
        {
            Dispatcher.Invoke(() => ApplyTheme(_viewModel.System.IsDarkMode));
        }
    }

    // --- Dialog highlight management ---

    private void OnGeneralPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GeneralSettingsViewModel.ActiveDialog) or nameof(GeneralSettingsViewModel.IsDialogOpen))
        {
            if (_viewModel.General.IsDialogOpen)
            {
                if (_viewModel.General.ActiveDialog == SettingsDialogType.Microphone)
                    Dispatcher.BeginInvoke(UpdateMicrophoneHighlight, System.Windows.Threading.DispatcherPriority.Loaded);
                else if (_viewModel.General.ActiveDialog == SettingsDialogType.Language)
                    Dispatcher.BeginInvoke(UpdateLanguageHighlight, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        if (e.PropertyName is nameof(GeneralSettingsViewModel.PendingLanguageCode) or nameof(GeneralSettingsViewModel.IsAutoDetectLanguage))
        {
            if (_viewModel.General.IsDialogOpen && _viewModel.General.ActiveDialog == SettingsDialogType.Language)
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
            border.BorderBrush = mic.DeviceIndex == _viewModel.General.SelectedMicrophoneIndex
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
            border.BorderBrush = (!_viewModel.General.IsAutoDetectLanguage && lang.Code == _viewModel.General.PendingLanguageCode)
                ? selectedBrush
                : Brushes.Transparent;
        }
    }

    // --- Hotkey capture ---

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.General.CapturingHotkey == HotkeyCaptureTarget.None) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore modifier-only presses
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        // Escape cancels
        if (key == Key.Escape)
        {
            _viewModel.General.CapturingHotkey = HotkeyCaptureTarget.None;
            _hotkeyService.SuppressActions = false;
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var modList = ParseModifiers(modifiers);

        // Require at least one modifier
        if (modList.Count == 0) return;

        _viewModel.General.ApplyNewHotkey(string.Join(", ", modList), key.ToString());
        e.Handled = true;
    }

    internal static List<string> ParseModifiers(ModifierKeys modifiers)
    {
        var modList = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) modList.Add("Control");
        if (modifiers.HasFlag(ModifierKeys.Shift)) modList.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) modList.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) modList.Add("Windows");
        return modList;
    }

    // --- Mouse button capture via low-level hook ---

    private void OnMouseButtonCaptured(object? sender, MouseButtonCapturedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_viewModel.General.CapturingHotkey == HotkeyCaptureTarget.None) return;

            var modList = ParseModifiers(Keyboard.Modifiers);
            _viewModel.General.ApplyNewHotkey(
                modList.Count > 0 ? string.Join(", ", modList) : "",
                null,
                e.Button);
        });
    }

    // --- Dialog overlay ---

    private void DialogOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.General.CloseDialogCommand.Execute(null);
    }

    private void RebindToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.General.StartCapturingToggleHotkeyCommand.Execute(null);
    }

    private void RebindPtt_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.General.StartCapturingPttHotkeyCommand.Execute(null);
    }

    private void MicrophoneItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: MicrophoneInfo mic })
            _viewModel.General.SelectMicrophone(mic.DeviceIndex);
    }

    private void LanguageCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string code)
            _viewModel.General.SelectLanguageCommand.Execute(code);
    }
}
