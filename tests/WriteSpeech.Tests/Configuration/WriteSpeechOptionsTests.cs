using System.IO;
using FluentAssertions;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;

namespace WriteSpeech.Tests.Configuration;

public class WriteSpeechOptionsTests
{
    /// <summary>
    /// Creates options that pass all validator checks (Local provider with model name).
    /// Use as base for tests that need valid options but test a specific field.
    /// </summary>
    private static WriteSpeechOptions CreateValidOptions() => new()
    {
        Provider = TranscriptionProvider.Local,
        Local = new LocalWhisperOptions { ModelName = "ggml-small.bin" }
    };
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new WriteSpeechOptions();

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
        options.Model.Should().Be("gpt-4.1-mini");
        options.SystemPrompt.Should().BeNull();
        options.AutoAddToDictionary.Should().BeTrue();
    }

    [Fact]
    public void AnthropicCorrectionOptions_DefaultValues()
    {
        var options = new AnthropicCorrectionOptions();

        options.ApiKey.Should().BeNull();
        options.Model.Should().Be("claude-sonnet-4-6");
    }

    [Fact]
    public void GoogleCorrectionOptions_DefaultValues()
    {
        var options = new TextCorrectionOptions();

        options.Google.Endpoint.Should().Be("https://generativelanguage.googleapis.com/v1beta/openai/");
        options.Google.Model.Should().Be("gemini-3-flash-preview");
        options.Google.ApiKey.Should().BeNull();
    }

    [Fact]
    public void GroqCorrectionOptions_DefaultValues()
    {
        var options = new TextCorrectionOptions();

        options.Groq.Endpoint.Should().Be("https://api.groq.com/openai/v1");
        options.Groq.Model.Should().Be("qwen/qwen3-32b");
        options.Groq.ApiKey.Should().BeNull();
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
            "WriteSpeech", "models");

        options.GetModelDirectory().Should().Be(expected);
    }

    [Fact]
    public void GetModelDirectory_WithEmpty_ReturnsFallback()
    {
        var options = new LocalWhisperOptions { ModelDirectory = "" };
        options.GetModelDirectory().Should().Contain("WriteSpeech");
    }

    // --- WriteSpeechOptionsValidator ---

    [Fact]
    public void Validator_DefaultOptions_RequiresApiKey()
    {
        // Default Provider is OpenAI, which requires an API key
        var validator = new WriteSpeechOptionsValidator();
        var result = validator.Validate(null, new WriteSpeechOptions());
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ApiKey");
    }

    [Fact]
    public void Validator_ValidOptions_Succeeds()
    {
        var validator = new WriteSpeechOptionsValidator();
        var result = validator.Validate(null, CreateValidOptions());
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(7999)]
    [InlineData(48001)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_InvalidSampleRate_Fails(int sampleRate)
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.Audio.SampleRate = sampleRate;

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
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.Audio.SampleRate = sampleRate;

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void Validator_InvalidMaxRecordingSeconds_Fails(int seconds)
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = new WriteSpeechOptions { Audio = new AudioOptions { MaxRecordingSeconds = seconds } };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("MaxRecordingSeconds");
    }

    [Fact]
    public void Validator_InvalidAutoDismissSeconds_Fails()
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = new WriteSpeechOptions { Overlay = new OverlayOptions { AutoDismissSeconds = 0 } };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("AutoDismissSeconds");
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(3.1)]
    public void Validator_InvalidOverlayScale_Fails(double scale)
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = new WriteSpeechOptions { Overlay = new OverlayOptions { Scale = scale } };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Scale");
    }

    [Fact]
    public void Validator_InvalidEndpointUrl_Fails()
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = new WriteSpeechOptions { OpenAI = new OpenAiOptions { Endpoint = "not-a-url" } };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Endpoint");
    }

    [Fact]
    public void Validator_ValidEndpointUrl_Succeeds()
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.OpenAI.Endpoint = "https://api.openai.com/v1";

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validator_NullEndpoint_Succeeds()
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.OpenAI.Endpoint = null;

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validator_MultipleFailures_ReportsAll()
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = new WriteSpeechOptions
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

    // --- HotkeyBinding ---

    [Fact]
    public void HotkeyBinding_IsMouseBinding_True_WhenMouseButtonSet()
    {
        var binding = new HotkeyBinding { MouseButton = "XButton1" };
        binding.IsMouseBinding.Should().BeTrue();
    }

    [Fact]
    public void HotkeyBinding_IsMouseBinding_False_WhenMouseButtonNull()
    {
        var binding = new HotkeyBinding { Key = "Space" };
        binding.IsMouseBinding.Should().BeFalse();
    }

    [Fact]
    public void HotkeyBinding_IsMouseBinding_False_WhenMouseButtonEmpty()
    {
        var binding = new HotkeyBinding { MouseButton = "" };
        binding.IsMouseBinding.Should().BeFalse();
    }

    [Fact]
    public void HotkeyOptions_DefaultMethod_IsRegisterHotKey()
    {
        var options = new HotkeyOptions();
        options.Method.Should().Be("RegisterHotKey");
    }

    // --- Hotkey method validation ---

    [Fact]
    public void Validator_InvalidHotkeyMethod_Fails()
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.Hotkey.Method = "InvalidMethod";

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Hotkey.Method");
    }

    [Theory]
    [InlineData("RegisterHotKey")]
    [InlineData("LowLevelHook")]
    public void Validator_ValidHotkeyMethod_Succeeds(string method)
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.Hotkey.Method = method;

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validator_RegisterHotKey_WithMouseBinding_Fails()
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.Hotkey.Method = "RegisterHotKey";
        options.Hotkey.Toggle = new HotkeyBinding { Modifiers = "Control", MouseButton = "XButton1" };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Mouse button");
    }

    [Fact]
    public void Validator_RegisterHotKey_WithPttMouseBinding_Fails()
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.Hotkey.Method = "RegisterHotKey";
        options.Hotkey.PushToTalk = new HotkeyBinding { MouseButton = "XButton2" };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Mouse button");
    }

    [Fact]
    public void Validator_LowLevelHook_WithMouseBinding_Succeeds()
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.Hotkey.Method = "LowLevelHook";
        options.Hotkey.PushToTalk = new HotkeyBinding { Modifiers = "Control", MouseButton = "XButton1" };

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    // --- Per-provider validation (API keys validated at service usage time, not startup) ---

    [Theory]
    [InlineData(TextCorrectionProvider.Anthropic)]
    [InlineData(TextCorrectionProvider.Google)]
    [InlineData(TextCorrectionProvider.Groq)]
    public void Validator_CloudProvider_WithoutApiKey_Succeeds(TextCorrectionProvider provider)
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.TextCorrection.Provider = provider;

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validator_OpenAIProvider_RequiresApiKey()
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.TextCorrection.Provider = TextCorrectionProvider.OpenAI;
        options.OpenAI.ApiKey = null;

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ApiKey");
    }

    // --- Parakeet ---

    [Fact]
    public void ParakeetOptions_DefaultValues()
    {
        var options = new ParakeetOptions();

        options.ModelName.Should().Be("sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8");
        options.ModelDirectory.Should().BeNull();
        options.GpuAcceleration.Should().BeTrue();
        options.NumThreads.Should().Be(4);
    }

    [Fact]
    public void ParakeetOptions_GetModelDirectory_WithNull_ReturnsAppDataPath()
    {
        var options = new ParakeetOptions { ModelDirectory = null };
        var result = options.GetModelDirectory();
        result.Should().Contain("WriteSpeech");
        result.Should().Contain("parakeet-models");
    }

    [Fact]
    public void ParakeetOptions_GetModelDirectory_WithCustomDir_ReturnsCustom()
    {
        var options = new ParakeetOptions { ModelDirectory = @"C:\models\parakeet" };
        options.GetModelDirectory().Should().Be(@"C:\models\parakeet");
    }

    [Fact]
    public void Validator_ParakeetProvider_RequiresModelName()
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.Provider = TranscriptionProvider.Parakeet;
        options.Parakeet.ModelName = "";

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Parakeet");
    }

    [Fact]
    public void Validator_ParakeetProvider_WithModelName_Succeeds()
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.Provider = TranscriptionProvider.Parakeet;

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validator_InvalidParakeetNumThreads_Fails()
    {
        var validator = new WriteSpeechOptionsValidator();
        var options = CreateValidOptions();
        options.Parakeet.NumThreads = 0;

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("NumThreads");
    }
}
