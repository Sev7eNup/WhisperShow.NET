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
        if (sender is System.Windows.Controls.Border border && border.Tag is string providerName)
            ViewModel.SelectCorrectionProviderCommand.Execute(providerName);
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
}
