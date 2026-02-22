using System.Windows;
using System.Windows.Controls;
using WhisperShow.App.ViewModels;

namespace WhisperShow.App.Views;

public class SettingsPageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? GeneralTemplate { get; set; }
    public DataTemplate? SystemTemplate { get; set; }
    public DataTemplate? TranscriptionTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is SettingsPage page)
        {
            return page switch
            {
                SettingsPage.General => GeneralTemplate,
                SettingsPage.System => SystemTemplate,
                SettingsPage.Transcription => TranscriptionTemplate,
                _ => base.SelectTemplate(item, container)
            };
        }
        return base.SelectTemplate(item, container);
    }
}
