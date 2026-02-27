using System.Windows.Controls;
using System.Windows.Input;
using WriteSpeech.App.ViewModels;
using WriteSpeech.App.ViewModels.Settings;

namespace WriteSpeech.App.Views.Settings;

public partial class ModelsPage : UserControl
{
    public ModelsPage()
    {
        InitializeComponent();
    }

    private SettingsViewModel ParentVM => (SettingsViewModel)DataContext;
    private TranscriptionSettingsViewModel ViewModel => ParentVM.Transcription;

    private void ProviderCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.Tag is string providerName)
            ViewModel.SelectProviderCommand.Execute(providerName);
    }

    private void EndpointTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyEndpoint(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingEndpoint = false;
            e.Handled = true;
        }
    }

    private void ApiKeyTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyApiKey(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingApiKey = false;
            e.Handled = true;
        }
    }

    private void CloudModelCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.Tag is string modelId)
        {
            ViewModel.SelectCloudModelCommand.Execute(modelId);
            ViewModel.IsEditingCustomCloudModel = false;
        }
    }

    private void CustomCloudModelCard_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsEditingCustomCloudModel = true;
    }

    private void CustomCloudModelTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            if (!string.IsNullOrWhiteSpace(tb.Text))
                ViewModel.ApplyModel(tb.Text);
            ViewModel.IsEditingCustomCloudModel = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingCustomCloudModel = false;
            e.Handled = true;
        }
    }

    // --- Cloud sub-provider navigation ---

    private void CloudSubProviderTab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.Tag is string provider)
            ViewModel.SelectCloudTranscriptionProviderCommand.Execute(provider);
    }

    // --- Groq Transcription ---

    private void StartEditGroqTranscriptionApiKey_Click(object sender, System.Windows.RoutedEventArgs e) =>
        ViewModel.IsEditingGroqTranscriptionApiKey = true;

    private void GroqTranscriptionApiKeyTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyGroqTranscriptionApiKey(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingGroqTranscriptionApiKey = false;
            e.Handled = true;
        }
    }

    private void GroqTranscriptionModelCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.Tag is string modelId)
            ViewModel.SelectGroqTranscriptionModelCommand.Execute(modelId);
    }

    // --- Custom Transcription ---

    private void StartEditCustomTranscriptionEndpoint_Click(object sender, System.Windows.RoutedEventArgs e) =>
        ViewModel.IsEditingCustomTranscriptionEndpoint = true;

    private void CustomTranscriptionEndpointTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyCustomTranscriptionEndpoint(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingCustomTranscriptionEndpoint = false;
            e.Handled = true;
        }
    }

    private void StartEditCustomTranscriptionApiKey_Click(object sender, System.Windows.RoutedEventArgs e) =>
        ViewModel.IsEditingCustomTranscriptionApiKey = true;

    private void CustomTranscriptionApiKeyTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyCustomTranscriptionApiKey(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingCustomTranscriptionApiKey = false;
            e.Handled = true;
        }
    }

    private void StartEditCustomTranscriptionModel_Click(object sender, System.Windows.RoutedEventArgs e) =>
        ViewModel.IsEditingCustomTranscriptionModel = true;

    private void CustomTranscriptionModelTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyCustomTranscriptionModel(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingCustomTranscriptionModel = false;
            e.Handled = true;
        }
    }

    private void CombinedAudioModelTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyCombinedAudioModel(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingCombinedAudioModel = false;
            e.Handled = true;
        }
    }
}
