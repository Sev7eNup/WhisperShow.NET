using System.Globalization;
using System.Windows;
using FluentAssertions;
using WriteSpeech.App.Converters;
using WriteSpeech.App.ViewModels.Settings;
using WriteSpeech.Core.Models;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.Converters;

public class SettingsConvertersTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public SettingsConvertersTests()
    {
        WpfTestHelper.EnsureApplication();
    }

    // --- InverseBoolToVisibilityConverter ---

    [Theory]
    [InlineData(true, Visibility.Collapsed)]
    [InlineData(false, Visibility.Visible)]
    public void InverseBool_ConvertsCorrectly(bool input, Visibility expected)
    {
        var converter = new InverseBoolToVisibilityConverter();
        var result = converter.Convert(input, typeof(Visibility), null!, Culture);
        result.Should().Be(expected);
    }

    // --- BoolToEnabledDisabledConverter ---

    [Theory]
    [InlineData(true, null, "Enabled")]
    [InlineData(false, null, "Disabled")]
    [InlineData(true, "capturing", "Listening for keys...")]
    [InlineData(false, "capturing", "Rebind")]
    public void BoolToEnabled_ConvertsCorrectly(bool input, string? parameter, string expected)
    {
        var converter = new BoolToEnabledDisabledConverter();
        var result = converter.Convert(input, typeof(string), parameter!, Culture);
        result.Should().Be(expected);
    }

    // --- SecondsToMinutesConverter ---

    [Theory]
    [InlineData(120, "2")]
    [InlineData("not a number", "0")]
    public void SecondsToMinutes_ConvertsCorrectly(object input, string expected)
    {
        var converter = new SecondsToMinutesConverter();
        var result = converter.Convert(input, typeof(string), null!, Culture);
        result.Should().Be(expected);
    }

    // --- ProviderToVisibilityConverter ---

    [Theory]
    [InlineData(TranscriptionProvider.OpenAI, "OpenAI", Visibility.Visible)]
    [InlineData(TranscriptionProvider.Local, "OpenAI", Visibility.Collapsed)]
    public void Provider_ConvertsCorrectly(TranscriptionProvider provider, string parameter, Visibility expected)
    {
        var converter = new ProviderToVisibilityConverter();
        var result = converter.Convert(provider, typeof(Visibility), parameter, Culture);
        result.Should().Be(expected);
    }

    // --- StringEqualsToVisibilityConverter ---

    [Theory]
    [InlineData("General", "General", Visibility.Visible)]
    [InlineData("General", "Models", Visibility.Collapsed)]
    public void StringEquals_String_ConvertsCorrectly(string input, string parameter, Visibility expected)
    {
        var converter = new StringEqualsToVisibilityConverter();
        var result = converter.Convert(input, typeof(Visibility), parameter, Culture);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(SettingsDialogType.Hotkey, "Hotkey", Visibility.Visible)]
    [InlineData(SettingsDialogType.Hotkey, "Microphone", Visibility.Collapsed)]
    public void StringEquals_Enum_ConvertsCorrectly(SettingsDialogType input, string parameter, Visibility expected)
    {
        var converter = new StringEqualsToVisibilityConverter();
        var result = converter.Convert(input, typeof(Visibility), parameter, Culture);
        result.Should().Be(expected);
    }

    // --- CapturingHotkeyTextConverter ---

    [Theory]
    [InlineData(HotkeyCaptureTarget.Toggle, "Toggle", "Listening for keys...")]
    [InlineData(HotkeyCaptureTarget.Toggle, "PushToTalk", "Rebind")]
    [InlineData(HotkeyCaptureTarget.None, "Toggle", "Rebind")]
    public void CapturingHotkey_ConvertsCorrectly(HotkeyCaptureTarget target, string parameter, string expected)
    {
        var converter = new CapturingHotkeyTextConverter();
        var result = converter.Convert(target, typeof(string), parameter, Culture);
        result.Should().Be(expected);
    }

    // --- EmptyStringToVisibilityConverter ---

    [Theory]
    [InlineData(null, Visibility.Visible)]
    [InlineData("", Visibility.Visible)]
    [InlineData("hello", Visibility.Collapsed)]
    public void EmptyString_ConvertsCorrectly(string? input, Visibility expected)
    {
        var converter = new EmptyStringToVisibilityConverter();
        var result = converter.Convert(input!, typeof(Visibility), null!, Culture);
        result.Should().Be(expected);
    }

    // --- NonEmptyStringToVisibilityConverter ---

    [Theory]
    [InlineData(null, Visibility.Collapsed)]
    [InlineData("", Visibility.Collapsed)]
    [InlineData("hello", Visibility.Visible)]
    public void NonEmptyString_ConvertsCorrectly(string? input, Visibility expected)
    {
        var converter = new NonEmptyStringToVisibilityConverter();
        var result = converter.Convert(input!, typeof(Visibility), null!, Culture);
        result.Should().Be(expected);
    }

    // --- Multi-value converters (complex array parameters, kept as [Fact]) ---

    [Fact]
    public void ModelAction_NotDownloadedNotDownloading_Visible()
    {
        var converter = new ModelActionVisibilityConverter();
        var result = converter.Convert([false, false], typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void ModelAction_Downloaded_Collapsed()
    {
        var converter = new ModelActionVisibilityConverter();
        var result = converter.Convert([true, false], typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void ModelDelete_DownloadedNotActiveNotDownloading_Visible()
    {
        var converter = new ModelDeleteVisibilityConverter();
        var result = converter.Convert([true, false, false], typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void ModelDelete_Active_Collapsed()
    {
        var converter = new ModelDeleteVisibilityConverter();
        var result = converter.Convert([true, false, true], typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void ModelUse_DownloadedNotActiveNotDownloading_Visible()
    {
        var converter = new ModelUseVisibilityConverter();
        var result = converter.Convert([true, false, false], typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Visible);
    }

    // --- MicLevelVisibilityConverter ---

    [Fact]
    public void MicLevel_SelectedAndTesting_ReturnsVisible()
    {
        var converter = new MicLevelVisibilityConverter();
        var result = converter.Convert([1, 1, true], typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void MicLevel_NotSelected_ReturnsCollapsed()
    {
        var converter = new MicLevelVisibilityConverter();
        var result = converter.Convert([0, 1, true], typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void MicLevel_NotTesting_ReturnsCollapsed()
    {
        var converter = new MicLevelVisibilityConverter();
        var result = converter.Convert([1, 1, false], typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void MicLevel_InvalidInputs_ReturnsCollapsed()
    {
        var converter = new MicLevelVisibilityConverter();
        var result = converter.Convert(["not", "valid"], typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Collapsed);
    }
}
