using System.Windows.Controls;
using System.Windows.Input;
using WhisperShow.App.ViewModels;
using WhisperShow.App.ViewModels.Settings;

namespace WhisperShow.App.Views.Settings;

public partial class SystemPage : UserControl
{
    public SystemPage()
    {
        InitializeComponent();
    }

    private SettingsViewModel ParentVM => (SettingsViewModel)DataContext;
    private SystemSettingsViewModel ViewModel => ParentVM.System;

    private void AutoDismissTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && int.TryParse(tb.Text, out var seconds))
        {
            ViewModel.ApplyAutoDismiss(Math.Max(1, seconds));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingAutoDismiss = false;
            e.Handled = true;
        }
    }

    private void MaxRecordingTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && int.TryParse(tb.Text, out var seconds))
        {
            ViewModel.ApplyMaxRecording(Math.Max(10, seconds));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsEditingMaxRecording = false;
            e.Handled = true;
        }
    }
}
