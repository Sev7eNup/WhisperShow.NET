using System.Windows.Controls;
using System.Windows.Input;
using Voxwright.App.ViewModels;

namespace Voxwright.App.Views.Settings;

public partial class DictionaryPage : UserControl
{
    public DictionaryPage()
    {
        InitializeComponent();
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    private void DictionaryTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.DictionarySnippets.AddDictionaryEntryCommand.Execute(null);
            e.Handled = true;
        }
    }
}
