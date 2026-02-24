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

        options.Provider.Should().Be(TextCorrectionProvider.Off);
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
    public void GetModelDirectory_WithEmpty_ReturnsFallback()
    {
        var options = new LocalWhisperOptions { ModelDirectory = "" };
        options.GetModelDirectory().Should().Contain("WhisperShow");
    }

    // --- WhisperShowOptionsValidator ---

    [Fact]
    public void Validator_DefaultOptions_Succeeds()
    {
        var validator = new WhisperShowOptionsValidator();
        var result = validator.Validate(null, new WhisperShowOptions());
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(7999)]
    [InlineData(48001)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_InvalidSampleRate_Fails(int sampleRate)
    {
        var validator = new WhisperShowOptionsValidator();
        var options = new WhisperShowOptions { Audio = new AudioOptions { SampleRate = sampleRate } };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("SampleRate");
    }

    [Theory]
    [InlineData(8000)]
    [InlineData(16000)]
    [InlineData(44100)]
    [InlineData(48000)]
    public void Validator_ValidSampleRate_Succeeds(int sampleRate)
    {
        var validator = new WhisperShowOptionsValidator();
        var options = new WhisperShowOptions { Audio = new AudioOptions { SampleRate = sampleRate } };

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void Validator_InvalidMaxRecordingSeconds_Fails(int seconds)
    {
        var validator = new WhisperShowOptionsValidator();
        var options = new WhisperShowOptions { Audio = new AudioOptions { MaxRecordingSeconds = seconds } };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("MaxRecordingSeconds");
    }

    [Fact]
    public void Validator_InvalidAutoDismissSeconds_Fails()
    {
        var validator = new WhisperShowOptionsValidator();
        var options = new WhisperShowOptions { Overlay = new OverlayOptions { AutoDismissSeconds = 0 } };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("AutoDismissSeconds");
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(3.1)]
    public void Validator_InvalidOverlayScale_Fails(double scale)
    {
        var validator = new WhisperShowOptionsValidator();
        var options = new WhisperShowOptions { Overlay = new OverlayOptions { Scale = scale } };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Scale");
    }

    [Fact]
    public void Validator_InvalidEndpointUrl_Fails()
    {
        var validator = new WhisperShowOptionsValidator();
        var options = new WhisperShowOptions { OpenAI = new OpenAiOptions { Endpoint = "not-a-url" } };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Endpoint");
    }

    [Fact]
    public void Validator_ValidEndpointUrl_Succeeds()
    {
        var validator = new WhisperShowOptionsValidator();
        var options = new WhisperShowOptions { OpenAI = new OpenAiOptions { Endpoint = "https://api.openai.com/v1" } };

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validator_NullEndpoint_Succeeds()
    {
        var validator = new WhisperShowOptionsValidator();
        var options = new WhisperShowOptions { OpenAI = new OpenAiOptions { Endpoint = null } };

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validator_MultipleFailures_ReportsAll()
    {
        var validator = new WhisperShowOptionsValidator();
        var options = new WhisperShowOptions
        {
            Audio = new AudioOptions { SampleRate = 0, MaxRecordingSeconds = 0 },
            Overlay = new OverlayOptions { AutoDismissSeconds = 0 }
        };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("SampleRate");
        result.FailureMessage.Should().Contain("MaxRecordingSeconds");
        result.FailureMessage.Should().Contain("AutoDismissSeconds");
    }
}
