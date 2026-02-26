using System.Windows.Controls;
using System.Windows.Input;
using WriteSpeech.App.ViewModels;
using WriteSpeech.App.ViewModels.Settings;

namespace WriteSpeech.App.Views.Settings;

public partial class IntelligencePage : UserControl
{
    public IntelligencePage()
    {
        InitializeComponent();
    }

    private SettingsViewModel ParentVM => (SettingsViewModel)DataContext;
    private TranscriptionSettingsViewModel ViewModel => ParentVM.Transcription;

    private void CorrectionProviderCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string providerName)
            ViewModel.SelectCorrectionProviderCommand.Execute(providerName);
    }

    // --- OpenAI model selection ---

    private void CorrectionCloudModelCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string modelId)
        {
            ViewModel.SelectCorrectionCloudModelCommand.Execute(modelId);
            ViewModel.IsEditingCustomCorrectionModel = false;
        }
    }

    private void CustomCorrectionModelCard_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsEditingCustomCorrectionModel = true;
    }

    private void CustomCorrectionModelTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            if (!string.IsNullOrWhiteSpace(tb.Text))
                ViewModel.ApplyCorrectionModel(tb.Text);
            ViewModel.IsEditingCustomCorrectionModel = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingCustomCorrectionModel = false;
            e.Handled = true;
        }
    }

    // --- Anthropic ---

    private void AnthropicApiKey_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsEditingAnthropicApiKey = true;
    }

    private void AnthropicApiKeyTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyAnthropicApiKey(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingAnthropicApiKey = false;
            e.Handled = true;
        }
    }

    private void AnthropicModelCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string modelId)
            ViewModel.SelectAnthropicModelCommand.Execute(modelId);
    }

    // --- Google ---

    private void GoogleApiKey_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsEditingGoogleApiKey = true;
    }

    private void GoogleApiKeyTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyGoogleApiKey(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingGoogleApiKey = false;
            e.Handled = true;
        }
    }

    private void GoogleModelCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string modelId)
            ViewModel.SelectGoogleModelCommand.Execute(modelId);
    }

    // --- Groq ---

    private void GroqApiKey_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsEditingGroqApiKey = true;
    }

    private void GroqApiKeyTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyGroqApiKey(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingGroqApiKey = false;
            e.Handled = true;
        }
    }

    private void GroqModelCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string modelId)
            ViewModel.SelectGroqModelCommand.Execute(modelId);
    }

    // --- Custom Provider ---

    private void CustomProviderEndpoint_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsEditingCustomCorrectionEndpoint = true;
    }

    private void CustomProviderEndpointTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyCustomCorrectionEndpoint(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingCustomCorrectionEndpoint = false;
            e.Handled = true;
        }
    }

    private void CustomProviderApiKey_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsEditingCustomCorrectionApiKey = true;
    }

    private void CustomProviderApiKeyTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyCustomCorrectionApiKey(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingCustomCorrectionApiKey = false;
            e.Handled = true;
        }
    }

    private void CustomProviderModel_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel.IsEditingCustomProviderModel = true;
    }

    private void CustomProviderModelTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            if (!string.IsNullOrWhiteSpace(tb.Text))
                ViewModel.ApplyCustomCorrectionModel(tb.Text);
            ViewModel.IsEditingCustomProviderModel = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingCustomProviderModel = false;
            e.Handled = true;
        }
    }
}
