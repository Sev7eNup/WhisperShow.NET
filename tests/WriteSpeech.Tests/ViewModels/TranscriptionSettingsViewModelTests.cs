using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.App.ViewModels.Settings;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.ModelManagement;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.ViewModels;

public class TranscriptionSettingsViewModelTests
{
    private readonly IModelManager _modelManager = Substitute.For<IModelManager>();
    private readonly ICorrectionModelManager _correctionModelManager = Substitute.For<ICorrectionModelManager>();
    private readonly IParakeetModelManager _parakeetModelManager = Substitute.For<IParakeetModelManager>();
    private readonly IModelPreloadService _preloadService = Substitute.For<IModelPreloadService>();
    private bool _saveCalled;

    private TranscriptionSettingsViewModel CreateViewModel(Action<WriteSpeechOptions>? configure = null)
    {
        _saveCalled = false;
        var options = new WriteSpeechOptions
        {
            OpenAI = new OpenAiOptions { ApiKey = "sk-test1234567890abcdef" }
        };
        configure?.Invoke(options);
        return new TranscriptionSettingsViewModel(
            _modelManager, _correctionModelManager, _parakeetModelManager, _preloadService,
            NullLogger<TranscriptionSettingsViewModel>.Instance,
            new SynchronousDispatcherService(),
            () => _saveCalled = true,
            options);
    }

    // --- Initialization ---

    [Fact]
    public void Constructor_OpenAiProvider_SetsOpenAiModel()
    {
        var vm = CreateViewModel();

        vm.TranscriptionModel.Should().Be("whisper-1");
        vm.Provider.Should().Be(TranscriptionProvider.OpenAI);
    }

    [Fact]
    public void Constructor_LocalProvider_SetsLocalModel()
    {
        var vm = CreateViewModel(o => { o.Provider = TranscriptionProvider.Local; o.Local.ModelName = "ggml-large.bin"; });

        vm.TranscriptionModel.Should().Be("ggml-large.bin");
        vm.Provider.Should().Be(TranscriptionProvider.Local);
    }

    [Fact]
    public void Constructor_SetsApiKeyDisplay()
    {
        var vm = CreateViewModel();

        vm.OpenAiApiKeyDisplay.Should().Be("sk-...cdef");
    }

    [Fact]
    public void Constructor_EmptyApiKey_ShowsNotConfigured()
    {
        var vm = CreateViewModel(o => o.OpenAI.ApiKey = "");

        vm.OpenAiApiKeyDisplay.Should().Be("Not configured");
    }

    [Fact]
    public void Constructor_ShortApiKey_DoesNotThrow()
    {
        var vm = CreateViewModel(o => o.OpenAI.ApiKey = "ab");

        vm.OpenAiApiKeyDisplay.Should().Be("sk-...****");
    }

    [Theory]
    [InlineData("a", "sk-...****")]
    [InlineData("ab", "sk-...****")]
    [InlineData("abc", "sk-...****")]
    [InlineData("abcd", "sk-...abcd")]
    [InlineData("abcde", "sk-...bcde")]
    public void Constructor_ApiKeyDisplay_HandlesVariousLengths(string key, string expected)
    {
        var vm = CreateViewModel(o => o.OpenAI.ApiKey = key);

        vm.OpenAiApiKeyDisplay.Should().Be(expected);
    }

    [Fact]
    public void ApplyApiKey_ShortKey_DoesNotThrow()
    {
        var vm = CreateViewModel();

        vm.ApplyApiKey("xy");

        vm.OpenAiApiKeyDisplay.Should().Be("sk-...****");
    }

    // --- ShowCloudUsageHint ---

    [Fact]
    public void ShowCloudUsageHint_LocalProviderCloudCorrection_ReturnsTrue()
    {
        var vm = CreateViewModel(o =>
        {
            o.Provider = TranscriptionProvider.Local;
            o.TextCorrection.Provider = TextCorrectionProvider.Cloud;
        });

        vm.ShowCloudUsageHint.Should().BeTrue();
    }

    [Fact]
    public void ShowCloudUsageHint_OpenAiProviderCloudCorrection_ReturnsFalse()
    {
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Cloud);

        vm.ShowCloudUsageHint.Should().BeFalse();
    }

    // --- Provider switching ---

    [Fact]
    public void ApplyProvider_SwitchToLocal_SavesOpenAiModelAndRestoresLocal()
    {
        var vm = CreateViewModel(o => o.Local.ModelName = "ggml-large.bin");

        vm.ApplyProvider(TranscriptionProvider.Local);

        vm.Provider.Should().Be(TranscriptionProvider.Local);
        vm.TranscriptionModel.Should().Be("ggml-large.bin");
        vm.IsEditingProvider.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ApplyProvider_SwitchToOpenAi_SavesLocalModelAndRestoresOpenAi()
    {
        var vm = CreateViewModel(o =>
        {
            o.Provider = TranscriptionProvider.Local;
            o.Local.ModelName = "ggml-large.bin";
        });

        vm.ApplyProvider(TranscriptionProvider.OpenAI);

        vm.Provider.Should().Be(TranscriptionProvider.OpenAI);
        vm.TranscriptionModel.Should().Be("whisper-1");
    }

    [Fact]
    public void SelectProviderCommand_ValidString_SwitchesProvider()
    {
        var vm = CreateViewModel();

        vm.SelectProviderCommand.Execute("Local");

        vm.Provider.Should().Be(TranscriptionProvider.Local);
        _saveCalled.Should().BeTrue();
    }

    // --- Apply methods trigger save ---

    [Fact]
    public void ApplyEndpoint_SetsValueAndTriggersSave()
    {
        var vm = CreateViewModel();

        vm.ApplyEndpoint("https://custom.api.com/v1");

        vm.OpenAiEndpoint.Should().Be("https://custom.api.com/v1");
        vm.IsEditingEndpoint.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ApplyApiKey_SetsValueAndUpdatesDisplay()
    {
        var vm = CreateViewModel(o => o.OpenAI.ApiKey = "");

        vm.ApplyApiKey("sk-newkey12345678abcdefg");

        vm.OpenAiApiKey.Should().Be("sk-newkey12345678abcdefg");
        vm.OpenAiApiKeyDisplay.Should().Be("sk-...defg");
        vm.IsEditingApiKey.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ApplyModel_OpenAiProvider_UpdatesOpenAiModelName()
    {
        var vm = CreateViewModel();

        vm.ApplyModel("whisper-large-v3");

        vm.TranscriptionModel.Should().Be("whisper-large-v3");
        vm.IsEditingModel.Should().BeFalse();
        _saveCalled.Should().BeTrue();

        // Switching away and back should preserve the model name
        vm.ApplyProvider(TranscriptionProvider.Local);
        vm.ApplyProvider(TranscriptionProvider.OpenAI);
        vm.TranscriptionModel.Should().Be("whisper-large-v3");
    }

    [Fact]
    public void SelectCloudModelCommand_UpdatesModelAndSaves()
    {
        var vm = CreateViewModel();

        vm.SelectCloudModelCommand.Execute("gpt-4o-transcribe");

        vm.TranscriptionModel.Should().Be("gpt-4o-transcribe");
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void CloudTranscriptionModels_ContainsExpectedModels()
    {
        TranscriptionSettingsViewModel.CloudTranscriptionModels.Should().HaveCount(3);
        TranscriptionSettingsViewModel.CloudTranscriptionModels.Select(m => m.Id)
            .Should().Contain(["gpt-4o-mini-transcribe", "gpt-4o-transcribe", "whisper-1"]);
    }

    // --- IsCustomCloudModel ---

    [Fact]
    public void IsCustomCloudModel_PredefinedModel_ReturnsFalse()
    {
        var vm = CreateViewModel();

        vm.IsCustomCloudModel.Should().BeFalse();
    }

    [Fact]
    public void IsCustomCloudModel_CustomModel_ReturnsTrue()
    {
        var vm = CreateViewModel(o => o.OpenAI.Model = "my-custom-whisper");

        vm.IsCustomCloudModel.Should().BeTrue();
    }

    [Fact]
    public void IsCustomCloudModel_LocalProvider_ReturnsFalse()
    {
        var vm = CreateViewModel(o =>
        {
            o.Provider = TranscriptionProvider.Local;
            o.Local.ModelName = "ggml-large.bin";
        });

        vm.IsCustomCloudModel.Should().BeFalse();
    }

    [Fact]
    public void ApplyModel_CustomModel_SetsIsCustomCloudModel()
    {
        var vm = CreateViewModel();

        vm.ApplyModel("my-custom-endpoint-model");

        vm.IsCustomCloudModel.Should().BeTrue();
        vm.TranscriptionModel.Should().Be("my-custom-endpoint-model");
    }

    [Fact]
    public void ApplyModel_PredefinedModel_ClearsIsCustomCloudModel()
    {
        var vm = CreateViewModel(o => o.OpenAI.Model = "my-custom-model");
        vm.IsCustomCloudModel.Should().BeTrue();

        vm.ApplyModel("whisper-1");

        vm.IsCustomCloudModel.Should().BeFalse();
    }

    [Fact]
    public void ApplyCorrectionModel_SetsValueAndTriggersSave()
    {
        var vm = CreateViewModel();

        vm.ApplyCorrectionModel("gpt-4o");

        vm.CorrectionCloudModel.Should().Be("gpt-4o");
        vm.IsEditingCustomCorrectionModel.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ApplyCombinedAudioModel_SetsValueAndTriggersSave()
    {
        var vm = CreateViewModel();

        vm.ApplyCombinedAudioModel("gpt-4o-audio-preview");

        vm.CombinedAudioModel.Should().Be("gpt-4o-audio-preview");
        vm.IsEditingCombinedAudioModel.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    // --- Toggle commands ---

    [Fact]
    public void ToggleGpuAcceleration_TogglesAndTriggersSave()
    {
        var vm = CreateViewModel();

        vm.ToggleGpuAccelerationCommand.Execute(null);

        vm.GpuAcceleration.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ToggleCorrectionGpuAcceleration_TogglesAndTriggersSave()
    {
        var vm = CreateViewModel();

        vm.ToggleCorrectionGpuAccelerationCommand.Execute(null);

        vm.CorrectionGpuAcceleration.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ToggleCombinedAudioModel_TriggersSave()
    {
        var vm = CreateViewModel();

        vm.ToggleCombinedAudioModelCommand.Execute(null);

        _saveCalled.Should().BeTrue();
    }

    // --- Correction provider ---

    [Fact]
    public void SelectCorrectionProvider_Cloud_TriggersSave()
    {
        var vm = CreateViewModel();

        vm.SelectCorrectionProviderCommand.Execute("Cloud");

        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Cloud);
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void SelectCorrectionProvider_Cloud_UnloadsLocalModel()
    {
        var vm = CreateViewModel();

        vm.SelectCorrectionProviderCommand.Execute("Cloud");

        _preloadService.Received(1).UnloadCorrectionModel();
    }

    [Fact]
    public void SelectCorrectionProvider_Off_UnloadsLocalModel()
    {
        var vm = CreateViewModel();

        vm.SelectCorrectionProviderCommand.Execute("Off");

        _preloadService.Received(1).UnloadCorrectionModel();
    }

    [Fact]
    public void SelectCorrectionProvider_Local_SetsProviderAndSaves()
    {
        var vm = CreateViewModel(o => o.TextCorrection.LocalModelName = "llama-3.gguf");

        vm.SelectCorrectionProviderCommand.Execute("Local");

        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Local);
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void SelectCorrectionProvider_Local_DoesNotUnloadModel()
    {
        var vm = CreateViewModel(o => o.TextCorrection.LocalModelName = "llama-3.gguf");

        vm.SelectCorrectionProviderCommand.Execute("Local");

        _preloadService.DidNotReceive().UnloadCorrectionModel();
    }

    // --- WriteSettings ---

    [Fact]
    public void WriteSettings_WritesAllProperties()
    {
        var vm = CreateViewModel(o =>
        {
            o.Provider = TranscriptionProvider.Local;
            o.OpenAI.Endpoint = "https://custom.api.com";
            o.OpenAI.ApiKey = "sk-testapikey1234";
            o.Local.ModelName = "ggml-large.bin";
            o.Local.GpuAcceleration = false;
            o.TextCorrection.Provider = TextCorrectionProvider.Cloud;
            o.TextCorrection.Model = "gpt-4o";
            o.TextCorrection.LocalGpuAcceleration = false;
            o.TextCorrection.LocalModelName = "llama-3.gguf";
            o.TextCorrection.UseCombinedAudioModel = true;
            o.TextCorrection.CombinedAudioModel = "gpt-4o-audio-preview";
        });

        var json = JsonNode.Parse("""
        {
            "Provider": "OpenAI",
            "OpenAI": { "ApiKey": "", "Model": "", "Endpoint": null },
            "Local": { "ModelName": "", "GpuAcceleration": true },
            "TextCorrection": {
                "Provider": "Off", "Model": "", "LocalModelName": "",
                "LocalGpuAcceleration": true, "UseCombinedAudioModel": false, "CombinedAudioModel": ""
            }
        }
        """)!;

        vm.WriteSettings(json);

        json["Provider"]!.GetValue<string>().Should().Be("Local");
        json["OpenAI"]!["ApiKey"]!.GetValue<string>().Should().Be("sk-testapikey1234");
        json["OpenAI"]!["Model"]!.GetValue<string>().Should().Be("whisper-1");
        json["OpenAI"]!["Endpoint"]!.GetValue<string>().Should().Be("https://custom.api.com");
        json["Local"]!["ModelName"]!.GetValue<string>().Should().Be("ggml-large.bin");
        json["Local"]!["GpuAcceleration"]!.GetValue<bool>().Should().BeFalse();
        json["TextCorrection"]!["Provider"]!.GetValue<string>().Should().Be("Cloud");
        json["TextCorrection"]!["Model"]!.GetValue<string>().Should().Be("gpt-4o");
        json["TextCorrection"]!["LocalModelName"]!.GetValue<string>().Should().Be("llama-3.gguf");
        json["TextCorrection"]!["LocalGpuAcceleration"]!.GetValue<bool>().Should().BeFalse();
        json["TextCorrection"]!["UseCombinedAudioModel"]!.GetValue<bool>().Should().BeTrue();
        json["TextCorrection"]!["CombinedAudioModel"]!.GetValue<string>().Should().Be("gpt-4o-audio-preview");
    }

    [Fact]
    public void WriteSettings_EmptyEndpoint_WritesNull()
    {
        var vm = CreateViewModel();

        var json = JsonNode.Parse("""
        {
            "Provider": "OpenAI",
            "OpenAI": { "ApiKey": "", "Model": "", "Endpoint": "https://old.api.com" },
            "Local": { "ModelName": "", "GpuAcceleration": true },
            "TextCorrection": {
                "Provider": "Off", "Model": "", "LocalModelName": "",
                "LocalGpuAcceleration": true, "UseCombinedAudioModel": false, "CombinedAudioModel": ""
            }
        }
        """)!;

        vm.WriteSettings(json);

        json["OpenAI"]!["Endpoint"].Should().BeNull();
    }

    [Fact]
    public void WriteSettings_MissingSections_CreatesThemAutomatically()
    {
        var vm = CreateViewModel();
        var json = JsonNode.Parse("{}")!;

        var act = () => vm.WriteSettings(json);

        act.Should().NotThrow();
        json["Provider"]!.GetValue<string>().Should().Be("OpenAI");
        json["OpenAI"]!["ApiKey"]!.GetValue<string>().Should().Be("sk-test1234567890abcdef");
        json["Local"]!["ModelName"]!.GetValue<string>().Should().Be("ggml-small.bin");
        json["TextCorrection"]!["Provider"]!.GetValue<string>().Should().Be("Off");
    }

    // --- Cloud correction model picker ---

    [Fact]
    public void CloudCorrectionModels_ContainsExpectedModels()
    {
        TranscriptionSettingsViewModel.CloudCorrectionModels.Should().HaveCount(6);
        TranscriptionSettingsViewModel.CloudCorrectionModels.Select(m => m.Id)
            .Should().Contain(["gpt-5.2", "gpt-5-mini", "gpt-5-nano", "gpt-4.1", "gpt-4.1-mini", "gpt-4.1-nano"]);
    }

    [Fact]
    public void IsCustomCorrectionModel_PredefinedModel_ReturnsFalse()
    {
        var vm = CreateViewModel(o => o.TextCorrection.Model = "gpt-4.1-mini");

        vm.IsCustomCorrectionModel.Should().BeFalse();
    }

    [Fact]
    public void IsCustomCorrectionModel_CustomModel_ReturnsTrue()
    {
        var vm = CreateViewModel(o => o.TextCorrection.Model = "my-custom-correction-model");

        vm.IsCustomCorrectionModel.Should().BeTrue();
    }

    [Fact]
    public void SelectCorrectionCloudModelCommand_UpdatesModelAndSaves()
    {
        var vm = CreateViewModel();

        vm.SelectCorrectionCloudModelCommand.Execute("gpt-5.2");

        vm.CorrectionCloudModel.Should().Be("gpt-5.2");
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ApplyCorrectionModel_ClearsEditingCustomCorrectionModel()
    {
        var vm = CreateViewModel();
        vm.IsEditingCustomCorrectionModel = true;

        vm.ApplyCorrectionModel("gpt-4.1");

        vm.IsEditingCustomCorrectionModel.Should().BeFalse();
        vm.CorrectionCloudModel.Should().Be("gpt-4.1");
    }

    [Fact]
    public void IsCustomCorrectionModel_AfterSelectingPredefined_ReturnsFalse()
    {
        var vm = CreateViewModel(o => o.TextCorrection.Model = "my-custom-model");
        vm.IsCustomCorrectionModel.Should().BeTrue();

        vm.ApplyCorrectionModel("gpt-5-mini");

        vm.IsCustomCorrectionModel.Should().BeFalse();
    }

    // --- New provider model lists ---

    [Fact]
    public void AnthropicCorrectionModels_ContainsExpectedModels()
    {
        TranscriptionSettingsViewModel.AnthropicCorrectionModels.Should().HaveCount(3);
        TranscriptionSettingsViewModel.AnthropicCorrectionModels.Select(m => m.Id)
            .Should().Contain(["claude-sonnet-4-6", "claude-opus-4-6", "claude-haiku-4-5-20251001"]);
    }

    [Fact]
    public void GoogleCorrectionModels_ContainsExpectedModels()
    {
        TranscriptionSettingsViewModel.GoogleCorrectionModels.Should().HaveCount(2);
        TranscriptionSettingsViewModel.GoogleCorrectionModels.Select(m => m.Id)
            .Should().Contain(["gemini-2.5-flash", "gemini-2.5-pro"]);
    }

    [Fact]
    public void GroqCorrectionModels_ContainsExpectedModels()
    {
        TranscriptionSettingsViewModel.GroqCorrectionModels.Should().HaveCount(3);
        TranscriptionSettingsViewModel.GroqCorrectionModels.Select(m => m.Id)
            .Should().Contain(["llama-3.3-70b-versatile", "llama-3.1-8b-instant", "mixtral-8x7b-32768"]);
    }

    // --- Per-provider initialization ---

    [Fact]
    public void Constructor_InitializesAnthropicProperties()
    {
        var vm = CreateViewModel(o =>
        {
            o.TextCorrection.Anthropic.ApiKey = "sk-ant-test1234";
            o.TextCorrection.Anthropic.Model = "claude-opus-4-6";
        });

        vm.AnthropicApiKey.Should().Be("sk-ant-test1234");
        vm.AnthropicModel.Should().Be("claude-opus-4-6");
        vm.AnthropicApiKeyDisplay.Should().Be("...1234");
    }

    [Fact]
    public void Constructor_InitializesGoogleProperties()
    {
        var vm = CreateViewModel(o =>
        {
            o.TextCorrection.Google.ApiKey = "AIzaSy-testkey1";
            o.TextCorrection.Google.Model = "gemini-2.5-pro";
        });

        vm.GoogleApiKey.Should().Be("AIzaSy-testkey1");
        vm.GoogleModel.Should().Be("gemini-2.5-pro");
        vm.GoogleApiKeyDisplay.Should().Be("...key1");
    }

    [Fact]
    public void Constructor_InitializesGroqProperties()
    {
        var vm = CreateViewModel(o =>
        {
            o.TextCorrection.Groq.ApiKey = "gsk-testgroq1234";
            o.TextCorrection.Groq.Model = "llama-3.1-8b-instant";
        });

        vm.GroqApiKey.Should().Be("gsk-testgroq1234");
        vm.GroqModel.Should().Be("llama-3.1-8b-instant");
        vm.GroqApiKeyDisplay.Should().Be("...1234");
    }

    [Fact]
    public void Constructor_EmptyProviderApiKey_ShowsNotConfigured()
    {
        var vm = CreateViewModel();

        vm.AnthropicApiKeyDisplay.Should().Be("Not configured");
        vm.GoogleApiKeyDisplay.Should().Be("Not configured");
        vm.GroqApiKeyDisplay.Should().Be("Not configured");
    }

    // --- Per-provider API key apply ---

    [Fact]
    public void ApplyAnthropicApiKey_SetsValueAndTriggersSave()
    {
        var vm = CreateViewModel();

        vm.ApplyAnthropicApiKey("sk-ant-newkey1234");

        vm.AnthropicApiKey.Should().Be("sk-ant-newkey1234");
        vm.AnthropicApiKeyDisplay.Should().Be("...1234");
        vm.IsEditingAnthropicApiKey.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ApplyGoogleApiKey_SetsValueAndTriggersSave()
    {
        var vm = CreateViewModel();

        vm.ApplyGoogleApiKey("AIzaSy-newkey5678");

        vm.GoogleApiKey.Should().Be("AIzaSy-newkey5678");
        vm.GoogleApiKeyDisplay.Should().Be("...5678");
        vm.IsEditingGoogleApiKey.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ApplyGroqApiKey_SetsValueAndTriggersSave()
    {
        var vm = CreateViewModel();

        vm.ApplyGroqApiKey("gsk-newgroqkey9012");

        vm.GroqApiKey.Should().Be("gsk-newgroqkey9012");
        vm.GroqApiKeyDisplay.Should().Be("...9012");
        vm.IsEditingGroqApiKey.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    // --- Per-provider model selection ---

    [Fact]
    public void SelectAnthropicModelCommand_UpdatesModelAndSaves()
    {
        var vm = CreateViewModel();

        vm.SelectAnthropicModelCommand.Execute("claude-opus-4-6");

        vm.AnthropicModel.Should().Be("claude-opus-4-6");
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void SelectGoogleModelCommand_UpdatesModelAndSaves()
    {
        var vm = CreateViewModel();

        vm.SelectGoogleModelCommand.Execute("gemini-2.5-pro");

        vm.GoogleModel.Should().Be("gemini-2.5-pro");
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void SelectGroqModelCommand_UpdatesModelAndSaves()
    {
        var vm = CreateViewModel();

        vm.SelectGroqModelCommand.Execute("mixtral-8x7b-32768");

        vm.GroqModel.Should().Be("mixtral-8x7b-32768");
        _saveCalled.Should().BeTrue();
    }

    // --- Per-provider correction provider selection ---

    [Fact]
    public void SelectCorrectionProvider_Anthropic_TriggersSave()
    {
        var vm = CreateViewModel();

        vm.SelectCorrectionProviderCommand.Execute("Anthropic");

        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Anthropic);
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void SelectCorrectionProvider_Google_TriggersSave()
    {
        var vm = CreateViewModel();

        vm.SelectCorrectionProviderCommand.Execute("Google");

        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Google);
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void SelectCorrectionProvider_Groq_TriggersSave()
    {
        var vm = CreateViewModel();

        vm.SelectCorrectionProviderCommand.Execute("Groq");

        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Groq);
        _saveCalled.Should().BeTrue();
    }

    // --- ShowCloudUsageHint with OpenAI enum ---

    [Fact]
    public void ShowCloudUsageHint_LocalProviderOpenAICorrection_ReturnsTrue()
    {
        var vm = CreateViewModel(o =>
        {
            o.Provider = TranscriptionProvider.Local;
            o.TextCorrection.Provider = TextCorrectionProvider.OpenAI;
        });

        vm.ShowCloudUsageHint.Should().BeTrue();
    }

    [Fact]
    public void ShowCloudUsageHint_LocalProviderAnthropicCorrection_ReturnsFalse()
    {
        var vm = CreateViewModel(o =>
        {
            o.Provider = TranscriptionProvider.Local;
            o.TextCorrection.Provider = TextCorrectionProvider.Anthropic;
        });

        vm.ShowCloudUsageHint.Should().BeFalse();
    }

    // --- WriteSettings with new providers ---

    [Fact]
    public void WriteSettings_WritesProviderSettings()
    {
        var vm = CreateViewModel(o =>
        {
            o.TextCorrection.Anthropic.ApiKey = "sk-ant-test";
            o.TextCorrection.Anthropic.Model = "claude-opus-4-6";
            o.TextCorrection.Google.ApiKey = "AIzaSy-test";
            o.TextCorrection.Google.Model = "gemini-2.5-pro";
            o.TextCorrection.Groq.ApiKey = "gsk-test";
            o.TextCorrection.Groq.Model = "mixtral-8x7b-32768";
        });

        var json = JsonNode.Parse("{}")!;
        vm.WriteSettings(json);

        json["TextCorrection"]!["Anthropic"]!["ApiKey"]!.GetValue<string>().Should().Be("sk-ant-test");
        json["TextCorrection"]!["Anthropic"]!["Model"]!.GetValue<string>().Should().Be("claude-opus-4-6");
        json["TextCorrection"]!["Google"]!["ApiKey"]!.GetValue<string>().Should().Be("AIzaSy-test");
        json["TextCorrection"]!["Google"]!["Model"]!.GetValue<string>().Should().Be("gemini-2.5-pro");
        json["TextCorrection"]!["Groq"]!["ApiKey"]!.GetValue<string>().Should().Be("gsk-test");
        json["TextCorrection"]!["Groq"]!["Model"]!.GetValue<string>().Should().Be("mixtral-8x7b-32768");
    }

    // --- Parakeet ---

    [Fact]
    public void Constructor_ParakeetProvider_SetsParakeetModel()
    {
        var vm = CreateViewModel(o =>
        {
            o.Provider = TranscriptionProvider.Parakeet;
            o.Parakeet.ModelName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";
        });

        vm.Provider.Should().Be(TranscriptionProvider.Parakeet);
        vm.TranscriptionModel.Should().Be("sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8");
    }

    [Fact]
    public void Constructor_ParakeetProvider_LoadsGpuAcceleration()
    {
        var vm = CreateViewModel(o =>
        {
            o.Provider = TranscriptionProvider.Parakeet;
            o.Parakeet.GpuAcceleration = false;
            o.Parakeet.NumThreads = 8;
        });

        vm.ParakeetGpuAcceleration.Should().BeFalse();
        vm.ParakeetNumThreads.Should().Be(8);
    }

    [Fact]
    public void SelectProvider_Parakeet_SchedulesSave()
    {
        var vm = CreateViewModel();
        vm.SelectProviderCommand.Execute("Parakeet");

        vm.Provider.Should().Be(TranscriptionProvider.Parakeet);
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ApplyProvider_SwitchingToParakeet_RestoresParakeetModel()
    {
        var vm = CreateViewModel(o =>
        {
            o.Provider = TranscriptionProvider.OpenAI;
            o.Parakeet.ModelName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";
        });

        vm.ApplyProvider(TranscriptionProvider.Parakeet);

        vm.TranscriptionModel.Should().Be("sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8");
    }

    [Fact]
    public void ToggleParakeetGpuAcceleration_TogglesAndSaves()
    {
        var vm = CreateViewModel();
        var initial = vm.ParakeetGpuAcceleration;

        vm.ToggleParakeetGpuAccelerationCommand.Execute(null);

        vm.ParakeetGpuAcceleration.Should().Be(!initial);
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void WriteSettings_IncludesParakeetSection()
    {
        var vm = CreateViewModel(o =>
        {
            o.Provider = TranscriptionProvider.Parakeet;
            o.Parakeet.ModelName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";
        });
        vm.ParakeetGpuAcceleration = false;
        vm.ParakeetNumThreads = 8;

        var json = JsonNode.Parse("{}")!;
        vm.WriteSettings(json);

        json["Provider"]!.GetValue<string>().Should().Be("Parakeet");
        json["Parakeet"]!["ModelName"]!.GetValue<string>().Should().Be("sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8");
        json["Parakeet"]!["GpuAcceleration"]!.GetValue<bool>().Should().BeFalse();
        json["Parakeet"]!["NumThreads"]!.GetValue<int>().Should().Be(8);
    }

    [Fact]
    public void ShowCloudUsageHint_True_WhenParakeetAndOpenAICorrection()
    {
        var vm = CreateViewModel(o =>
        {
            o.Provider = TranscriptionProvider.Parakeet;
            o.TextCorrection.Provider = TextCorrectionProvider.OpenAI;
        });

        vm.ShowCloudUsageHint.Should().BeTrue();
    }
}
