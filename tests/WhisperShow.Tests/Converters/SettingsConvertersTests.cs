using System.Globalization;
using System.Windows;
using FluentAssertions;
using WhisperShow.App.Converters;
using WhisperShow.Core.Models;
using WhisperShow.Tests.TestHelpers;

namespace WhisperShow.Tests.Converters;

public class SettingsConvertersTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public SettingsConvertersTests()
    {
        WpfTestHelper.EnsureApplication();
    }

    // --- InverseBoolToVisibilityConverter ---

    [Fact]
    public void InverseBool_True_ReturnsCollapsed()
    {
        var converter = new InverseBoolToVisibilityConverter();
        var result = converter.Convert(true, typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void InverseBool_False_ReturnsVisible()
    {
        var converter = new InverseBoolToVisibilityConverter();
        var result = converter.Convert(false, typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Visible);
    }

    // --- BoolToEnabledDisabledConverter ---

    [Fact]
    public void BoolToEnabled_True_ReturnsEnabled()
    {
        var converter = new BoolToEnabledDisabledConverter();
        var result = converter.Convert(true, typeof(string), null!, Culture);
        result.Should().Be("Enabled");
    }

    [Fact]
    public void BoolToEnabled_False_ReturnsDisabled()
    {
        var converter = new BoolToEnabledDisabledConverter();
        var result = converter.Convert(false, typeof(string), null!, Culture);
        result.Should().Be("Disabled");
    }

    [Fact]
    public void BoolToEnabled_Capturing_True_ReturnsListening()
    {
        var converter = new BoolToEnabledDisabledConverter();
        var result = converter.Convert(true, typeof(string), "capturing", Culture);
        result.Should().Be("Listening for keys...");
    }

    [Fact]
    public void BoolToEnabled_Capturing_False_ReturnsRebind()
    {
        var converter = new BoolToEnabledDisabledConverter();
        var result = converter.Convert(false, typeof(string), "capturing", Culture);
        result.Should().Be("Rebind");
    }

    // --- SecondsToMinutesConverter ---

    [Fact]
    public void SecondsToMinutes_120_Returns2()
    {
        var converter = new SecondsToMinutesConverter();
        var result = converter.Convert(120, typeof(string), null!, Culture);
        result.Should().Be("2");
    }

    [Fact]
    public void SecondsToMinutes_NonInt_Returns0()
    {
        var converter = new SecondsToMinutesConverter();
        var result = converter.Convert("not a number", typeof(string), null!, Culture);
        result.Should().Be("0");
    }

    // --- ProviderToVisibilityConverter ---

    [Fact]
    public void Provider_Matching_ReturnsVisible()
    {
        var converter = new ProviderToVisibilityConverter();
        var result = converter.Convert(TranscriptionProvider.OpenAI, typeof(Visibility), "OpenAI", Culture);
        result.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void Provider_NotMatching_ReturnsCollapsed()
    {
        var converter = new ProviderToVisibilityConverter();
        var result = converter.Convert(TranscriptionProvider.Local, typeof(Visibility), "OpenAI", Culture);
        result.Should().Be(Visibility.Collapsed);
    }

    // --- StringEqualsToVisibilityConverter ---

    [Fact]
    public void StringEquals_Match_ReturnsVisible()
    {
        var converter = new StringEqualsToVisibilityConverter();
        var result = converter.Convert("General", typeof(Visibility), "General", Culture);
        result.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void StringEquals_NoMatch_ReturnsCollapsed()
    {
        var converter = new StringEqualsToVisibilityConverter();
        var result = converter.Convert("General", typeof(Visibility), "Models", Culture);
        result.Should().Be(Visibility.Collapsed);
    }

    // --- CapturingHotkeyTextConverter ---

    [Fact]
    public void CapturingHotkey_Match_ReturnsListening()
    {
        var converter = new CapturingHotkeyTextConverter();
        var result = converter.Convert("Toggle", typeof(string), "Toggle", Culture);
        result.Should().Be("Listening for keys...");
    }

    [Fact]
    public void CapturingHotkey_NoMatch_ReturnsRebind()
    {
        var converter = new CapturingHotkeyTextConverter();
        var result = converter.Convert("Toggle", typeof(string), "PushToTalk", Culture);
        result.Should().Be("Rebind");
    }

    // --- ModelActionVisibilityConverter ---

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

    // --- ModelDeleteVisibilityConverter ---

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

    // --- EmptyStringToVisibilityConverter ---

    [Fact]
    public void EmptyString_Null_ReturnsVisible()
    {
        var converter = new EmptyStringToVisibilityConverter();
        var result = converter.Convert(null!, typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void EmptyString_Empty_ReturnsVisible()
    {
        var converter = new EmptyStringToVisibilityConverter();
        var result = converter.Convert("", typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void EmptyString_NonEmpty_ReturnsCollapsed()
    {
        var converter = new EmptyStringToVisibilityConverter();
        var result = converter.Convert("hello", typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Collapsed);
    }

    // --- NonEmptyStringToVisibilityConverter ---

    [Fact]
    public void NonEmptyString_Null_ReturnsCollapsed()
    {
        var converter = new NonEmptyStringToVisibilityConverter();
        var result = converter.Convert(null!, typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void NonEmptyString_Empty_ReturnsCollapsed()
    {
        var converter = new NonEmptyStringToVisibilityConverter();
        var result = converter.Convert("", typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void NonEmptyString_NonEmpty_ReturnsVisible()
    {
        var converter = new NonEmptyStringToVisibilityConverter();
        var result = converter.Convert("hello", typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Visible);
    }

    // --- ModelUseVisibilityConverter ---

    [Fact]
    public void ModelUse_DownloadedNotActiveNotDownloading_Visible()
    {
        var converter = new ModelUseVisibilityConverter();
        var result = converter.Convert([true, false, false], typeof(Visibility), null!, Culture);
        result.Should().Be(Visibility.Visible);
    }
}
