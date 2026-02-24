using System.Windows.Input;
using FluentAssertions;
using WhisperShow.App.Views;

namespace WhisperShow.Tests.Views;

public class SettingsWindowTests
{
    // --- Modifier Parsing (Theory-based) ---

    [Theory]
    [InlineData(ModifierKeys.None, new string[] { })]
    [InlineData(ModifierKeys.Control, new[] { "Control" })]
    [InlineData(ModifierKeys.Shift, new[] { "Shift" })]
    [InlineData(ModifierKeys.Alt, new[] { "Alt" })]
    [InlineData(ModifierKeys.Windows, new[] { "Windows" })]
    public void ParseModifiers_SingleModifier_ReturnsExpected(ModifierKeys input, string[] expected)
    {
        var result = SettingsWindow.ParseModifiers(input);
        result.Should().Equal(expected);
    }

    [Fact]
    public void ParseModifiers_ControlShift_ReturnsBothInOrder()
    {
        var result = SettingsWindow.ParseModifiers(ModifierKeys.Control | ModifierKeys.Shift);
        result.Should().Equal("Control", "Shift");
    }

    [Fact]
    public void ParseModifiers_ControlAltShift_ReturnsCorrectOrder()
    {
        var result = SettingsWindow.ParseModifiers(
            ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
        result.Should().Equal("Control", "Shift", "Alt");
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
