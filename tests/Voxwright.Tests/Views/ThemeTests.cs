using System.Windows;
using FluentAssertions;
using Voxwright.Tests.TestHelpers;

namespace Voxwright.Tests.Views;

public class ThemeTests
{
    private static readonly string[] ExpectedBrushKeys =
    [
        "BgBrush",
        "SidebarBgBrush",
        "CardBgBrush",
        "ButtonBgBrush",
        "ButtonBorderBrush",
        "TextPrimaryBrush",
        "TextSecondaryBrush",
        "SidebarActiveBrush",
        "SidebarHoverBrush",
        "DividerBrush",
        "SelectedBorderBrush",
        "BadgeBgBrush",
        "BadgeBorderBrush",
        "DialogBgBrush",
        "OverlayBrush"
    ];

    public ThemeTests()
    {
        WpfTestHelper.EnsureApplication();
    }

    [Fact]
    public void DarkTheme_LoadsSuccessfully()
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri("/Voxwright.App;component/Themes/SettingsDarkTheme.xaml", UriKind.Relative)
        };

        dict.Should().NotBeNull();
        dict.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void LightTheme_LoadsSuccessfully()
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri("/Voxwright.App;component/Themes/SettingsLightTheme.xaml", UriKind.Relative)
        };

        dict.Should().NotBeNull();
        dict.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DarkTheme_ContainsAllExpectedKeys()
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri("/Voxwright.App;component/Themes/SettingsDarkTheme.xaml", UriKind.Relative)
        };

        foreach (var key in ExpectedBrushKeys)
        {
            dict.Contains(key).Should().BeTrue($"DarkTheme should contain brush key '{key}'");
        }
    }

    [Fact]
    public void LightTheme_ContainsAllExpectedKeys()
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri("/Voxwright.App;component/Themes/SettingsLightTheme.xaml", UriKind.Relative)
        };

        foreach (var key in ExpectedBrushKeys)
        {
            dict.Contains(key).Should().BeTrue($"LightTheme should contain brush key '{key}'");
        }
    }

    [Fact]
    public void BothThemes_HaveMatchingKeys()
    {
        var darkDict = new ResourceDictionary
        {
            Source = new Uri("/Voxwright.App;component/Themes/SettingsDarkTheme.xaml", UriKind.Relative)
        };
        var lightDict = new ResourceDictionary
        {
            Source = new Uri("/Voxwright.App;component/Themes/SettingsLightTheme.xaml", UriKind.Relative)
        };

        var darkKeys = darkDict.Keys.Cast<object>().OrderBy(k => k.ToString()).ToList();
        var lightKeys = lightDict.Keys.Cast<object>().OrderBy(k => k.ToString()).ToList();

        darkKeys.Should().BeEquivalentTo(lightKeys,
            "both themes must define the same set of resource keys");
    }
}
