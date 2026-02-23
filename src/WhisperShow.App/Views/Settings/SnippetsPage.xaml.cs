using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WhisperShow.App.ViewModels;

namespace WhisperShow.App.Views.Settings;

public partial class SnippetsPage : UserControl
{
    public SnippetsPage()
    {
        InitializeComponent();
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

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
            ViewModel.DictionarySnippets.SaveSnippetCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SnippetReplacementTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ViewModel.DictionarySnippets.SaveSnippetCommand.Execute(null);
            e.Handled = true;
        }
    }
}
