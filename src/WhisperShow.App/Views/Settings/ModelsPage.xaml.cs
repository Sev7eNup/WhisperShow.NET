using System.Windows.Controls;
using System.Windows.Input;
using WhisperShow.App.ViewModels;

namespace WhisperShow.App.Views.Settings;

public partial class ModelsPage : UserControl
{
    public ModelsPage()
    {
        InitializeComponent();
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    private void ProviderCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.Tag is string providerName)
            ViewModel.SelectProviderCommand.Execute(providerName);
    }

    private void CorrectionProviderCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.Tag is string providerName)
            ViewModel.SelectCorrectionProviderCommand.Execute(providerName);
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

    private void ModelTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyModel(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingModel = false;
            e.Handled = true;
        }
    }

    private void CorrectionModelTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            ViewModel.ApplyCorrectionModel(tb.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingCorrectionModel = false;
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
