using System.IO;
using FluentAssertions;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;

namespace WhisperShow.Tests.Configuration;

public class WhisperShowOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new WhisperShowOptions();

        options.Provider.Should().Be(TranscriptionProvider.OpenAI);
        options.Language.Should().BeNull();
        options.OpenAI.Model.Should().Be("whisper-1");
        options.OpenAI.ApiKey.Should().BeNull();
        options.Audio.SampleRate.Should().Be(16000);
        options.Audio.DeviceIndex.Should().Be(0);
        options.Audio.MaxRecordingSeconds.Should().Be(300);
        options.Hotkey.Toggle.Modifiers.Should().Be("Control, Shift");
        options.Hotkey.Toggle.Key.Should().Be("Space");
        options.Hotkey.PushToTalk.Modifiers.Should().Be("Control");
        options.Hotkey.PushToTalk.Key.Should().Be("Space");
    }

    [Fact]
    public void AppOptions_DefaultValues()
    {
        var options = new AppOptions();

        options.LaunchAtLogin.Should().BeFalse();
        options.SoundEffects.Should().BeTrue();
    }

    [Fact]
    public void OverlayOptions_DefaultValues()
    {
        var options = new OverlayOptions();

        options.AlwaysVisible.Should().BeTrue();
        options.ShowInTaskbar.Should().BeFalse();
        options.AutoDismissSeconds.Should().Be(10);
    }

    [Fact]
    public void AudioOptions_DefaultValues()
    {
        var options = new AudioOptions();

        options.MuteWhileDictating.Should().BeTrue();
        options.CompressBeforeUpload.Should().BeTrue();
        options.SampleRate.Should().Be(16000);
    }

    [Fact]
    public void TextCorrectionOptions_DefaultValues()
    {
        var options = new TextCorrectionOptions();

        options.Enabled.Should().BeFalse();
        options.Model.Should().Be("gpt-4o-mini");
        options.SystemPrompt.Should().BeNull();
    }

    [Fact]
    public void GetModelDirectory_WithCustomDir_ReturnsCustom()
    {
        var options = new LocalWhisperOptions { ModelDirectory = @"C:\models" };
        options.GetModelDirectory().Should().Be(@"C:\models");
    }

    [Fact]
    public void GetModelDirectory_WithNull_ReturnsAppDataPath()
    {
        var options = new LocalWhisperOptions { ModelDirectory = null };

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperShow", "models");

        options.GetModelDirectory().Should().Be(expected);
    }

    [Fact]
    public void GetModelDirectory_WithEmpty_ReturnsEmptyString()
    {
        // Documents current behavior: empty string is truthy in null-coalescing,
        // so GetModelDirectory returns "" instead of the AppData fallback.
        var options = new LocalWhisperOptions { ModelDirectory = "" };
        options.GetModelDirectory().Should().BeEmpty();
    }
}
