using System.Windows;

namespace WriteSpeech.App.Views;

internal static class ThemeHelper
{
    public static void Apply(FrameworkElement element, bool isDark)
    {
        var themePath = isDark
            ? "/Themes/SettingsDarkTheme.xaml"
            : "/Themes/SettingsLightTheme.xaml";

        var themeUri = new Uri(themePath, UriKind.Relative);
        var themeDict = new ResourceDictionary { Source = themeUri };

        var merged = element.Resources.MergedDictionaries;
        if (merged.Count > 0)
            merged[0] = themeDict;
        else
            merged.Insert(0, themeDict);
    }
}
