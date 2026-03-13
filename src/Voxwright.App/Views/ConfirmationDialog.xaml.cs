using System.Windows;
using System.Windows.Input;

namespace Voxwright.App.Views;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(string title, string message, string confirmText = "Confirm", Window? owner = null)
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;

        if (owner is not null)
            Owner = owner;

        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var settingsVm = (Application.Current as App)?
            .Services?.GetService(typeof(Voxwright.App.ViewModels.SettingsViewModel))
            as Voxwright.App.ViewModels.SettingsViewModel;

        ThemeHelper.Apply(this, settingsVm?.System.IsDarkMode ?? true);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
