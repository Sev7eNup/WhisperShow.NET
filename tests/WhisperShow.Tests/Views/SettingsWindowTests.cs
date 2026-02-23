using System.Windows.Input;
using FluentAssertions;
using WhisperShow.App.Views;

namespace WhisperShow.Tests.Views;

public class SettingsWindowTests
{
    // --- Modifier Parsing ---

    [Fact]
    public void ParseModifiers_ControlShift_ReturnsBoth()
    {
        var result = SettingsWindow.ParseModifiers(ModifierKeys.Control | ModifierKeys.Shift);

        result.Should().Equal("Control", "Shift");
    }

    [Fact]
    public void ParseModifiers_AltOnly_ReturnsSingle()
    {
        var result = SettingsWindow.ParseModifiers(ModifierKeys.Alt);

        result.Should().Equal("Alt");
    }

    [Fact]
    public void ParseModifiers_ControlAltShift_ReturnsCorrectOrder()
    {
        var result = SettingsWindow.ParseModifiers(
            ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);

        result.Should().Equal("Control", "Shift", "Alt");
    }

    [Fact]
    public void ParseModifiers_None_ReturnsEmpty()
    {
        var result = SettingsWindow.ParseModifiers(ModifierKeys.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseModifiers_Windows_ReturnsWindows()
    {
        var result = SettingsWindow.ParseModifiers(ModifierKeys.Windows);

        result.Should().Equal("Windows");
    }

    [Fact]
    public void ParseModifiers_AllModifiers_ReturnsFour()
    {
        var result = SettingsWindow.ParseModifiers(
            ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows);

        result.Should().HaveCount(4);
        result.Should().Contain("Control");
        result.Should().Contain("Shift");
        result.Should().Contain("Alt");
        result.Should().Contain("Windows");
    }
}
