using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WriteSpeech.App.ViewModels;
using WriteSpeech.App.ViewModels.Settings;

namespace WriteSpeech.App.Views;

public partial class SetupWizardWindow : Window
{
    private readonly SetupWizardViewModel _viewModel;

    private readonly Ellipse[] _dots;
    private readonly Border[] _lines;

    public SetupWizardWindow(SetupWizardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _dots = [Dot0, Dot1, Dot2, Dot3];
        _lines = [Line01, Line12, Line23];

        ClipBorder.SizeChanged += OnClipBorderSizeChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        UpdateProgressIndicator();
        UpdateProviderHighlight();
    }

    private void OnClipBorderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ClipBorder.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 12, 12);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SetupWizardViewModel.CurrentStep))
        {
            UpdateProgressIndicator();
            // Defer highlight updates until the template has been applied
            Dispatcher.BeginInvoke(() =>
            {
                UpdateProviderHighlight();
                UpdateLanguageHighlight();
                UpdateMicrophoneHighlight();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        if (e.PropertyName is nameof(SetupWizardViewModel.Provider))
            Dispatcher.BeginInvoke(UpdateProviderHighlight, System.Windows.Threading.DispatcherPriority.Loaded);

        if (e.PropertyName is nameof(SetupWizardViewModel.SelectedLanguageCode)
            or nameof(SetupWizardViewModel.IsAutoDetectLanguage))
            Dispatcher.BeginInvoke(UpdateLanguageHighlight, System.Windows.Threading.DispatcherPriority.Loaded);

        if (e.PropertyName is nameof(SetupWizardViewModel.SelectedMicrophoneIndex))
            Dispatcher.BeginInvoke(UpdateMicrophoneHighlight, System.Windows.Threading.DispatcherPriority.Loaded);

        if (e.PropertyName is nameof(SetupWizardViewModel.IsCompleted) && _viewModel.IsCompleted)
        {
            DialogResult = true;
            Close();
        }
    }

    private void UpdateProgressIndicator()
    {
        var accentBrush = (SolidColorBrush)FindResource("SelectedBorderBrush");
        var inactiveBrush = (SolidColorBrush)FindResource("ButtonBgBrush");
        int step = _viewModel.CurrentStepIndex;

        for (int i = 0; i < _dots.Length; i++)
            _dots[i].Fill = i <= step ? accentBrush : inactiveBrush;

        for (int i = 0; i < _lines.Length; i++)
            _lines[i].Background = i < step ? accentBrush : inactiveBrush;
    }

    // --- Title bar ---

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // --- Language selection ---

    private void LanguageCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string code)
            _viewModel.SelectLanguageCommand.Execute(code);
    }

    private void UpdateLanguageHighlight()
    {
        var languageList = FindDescendant<ItemsControl>("LanguageList");
        if (languageList == null) return;

        var selectedBrush = (SolidColorBrush)FindResource("SelectedBorderBrush");
        for (int i = 0; i < languageList.Items.Count; i++)
        {
            if (languageList.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter container)
                continue;
            if (VisualTreeHelper.GetChildrenCount(container) == 0) continue;
            if (VisualTreeHelper.GetChild(container, 0) is not Border border) continue;
            var lang = (LanguageInfo)languageList.Items[i]!;
            border.BorderBrush = (!_viewModel.IsAutoDetectLanguage && lang.Code == _viewModel.SelectedLanguageCode)
                ? selectedBrush
                : Brushes.Transparent;
        }
    }

    // --- Provider card selection ---

    private void ProviderCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string provider)
            _viewModel.SelectProviderCommand.Execute(provider);
    }

    private void UpdateProviderHighlight()
    {
        if (_viewModel.CurrentStep != SetupStep.Transcription) return;

        var selectedBrush = (SolidColorBrush)FindResource("SelectedBorderBrush");
        var providerName = _viewModel.Provider.ToString();

        // Find the UniformGrid with provider cards
        var contentArea = FindDescendant<ScrollViewer>(this);
        if (contentArea == null) return;

        foreach (var border in FindDescendants<Border>(contentArea))
        {
            if (border.Tag is string tag && tag is "OpenAI" or "Local" or "Parakeet")
            {
                border.BorderBrush = tag == providerName ? selectedBrush : Brushes.Transparent;
            }
        }
    }

    // --- Correction provider ---

    private void CorrectionProviderPill_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string provider)
            _viewModel.SelectCorrectionProviderCommand.Execute(provider);
    }

    // --- Microphone ---

    private void MicrophoneItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: MicrophoneInfo mic })
            _viewModel.SelectMicrophone(mic.DeviceIndex);
    }

    private void UpdateMicrophoneHighlight()
    {
        var micList = FindDescendant<ItemsControl>("MicrophoneList");
        if (micList == null) return;

        var selectedBrush = (SolidColorBrush)FindResource("SelectedBorderBrush");
        for (int i = 0; i < micList.Items.Count; i++)
        {
            if (micList.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter container)
                continue;
            if (VisualTreeHelper.GetChildrenCount(container) == 0) continue;
            if (VisualTreeHelper.GetChild(container, 0) is not Border border) continue;
            var mic = (MicrophoneInfo)micList.Items[i]!;
            border.BorderBrush = mic.DeviceIndex == _viewModel.SelectedMicrophoneIndex
                ? selectedBrush
                : Brushes.Transparent;
        }
    }

    // --- API key text boxes ---

    private void ApiKeyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            _viewModel.SetOpenAiApiKey(tb.Text);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void CorrectionApiKeyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            _viewModel.SetCorrectionApiKey(tb.Text);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    // --- Cleanup ---

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.StopMicTest();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ClipBorder.SizeChanged -= OnClipBorderSizeChanged;
        base.OnClosed(e);
    }

    // --- Visual tree helpers ---

    private T? FindDescendant<T>(string name) where T : FrameworkElement
        => FindDescendant<T>(this, name);

    private static T? FindDescendant<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Name == name) return fe;
            var found = FindDescendant<T>(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindDescendant<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var desc in FindDescendants<T>(child))
                yield return desc;
        }
    }
}
