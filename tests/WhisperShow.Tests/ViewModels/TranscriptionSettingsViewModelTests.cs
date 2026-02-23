using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WhisperShow.App.ViewModels.Settings;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services.ModelManagement;

namespace WhisperShow.Tests.ViewModels;

public class TranscriptionSettingsViewModelTests
{
    private readonly IModelManager _modelManager = Substitute.For<IModelManager>();
    private readonly ICorrectionModelManager _correctionModelManager = Substitute.For<ICorrectionModelManager>();
    private readonly IModelPreloadService _preloadService = Substitute.For<IModelPreloadService>();
    private bool _saveCalled;

    private TranscriptionSettingsViewModel CreateViewModel(
        TranscriptionProvider provider = TranscriptionProvider.OpenAI,
        string openAiEndpoint = "",
        string openAiApiKey = "sk-test1234567890abcdef",
        string openAiModelName = "whisper-1",
        string localModelName = "ggml-small.bin",
        bool gpuAcceleration = true,
        TextCorrectionProvider correctionProvider = TextCorrectionProvider.Off,
        string correctionCloudModel = "gpt-4o-mini",
        bool correctionGpuAcceleration = true,
        string correctionLocalModelName = "",
        bool useCombinedAudioModel = false,
        string combinedAudioModel = "gpt-4o-mini-audio-preview")
    {
        _saveCalled = false;
        return new TranscriptionSettingsViewModel(
            _modelManager, _correctionModelManager, _preloadService,
            NullLogger<TranscriptionSettingsViewModel>.Instance,
            () => _saveCalled = true,
            provider, openAiEndpoint, openAiApiKey,
            openAiModelName, localModelName, gpuAcceleration,
            correctionProvider, correctionCloudModel, correctionGpuAcceleration,
            correctionLocalModelName, useCombinedAudioModel, combinedAudioModel);
    }

    // --- Initialization ---

    [Fact]
    public void Constructor_OpenAiProvider_SetsOpenAiModel()
    {
        var vm = CreateViewModel(provider: TranscriptionProvider.OpenAI, openAiModelName: "whisper-1");

        vm.TranscriptionModel.Should().Be("whisper-1");
        vm.Provider.Should().Be(TranscriptionProvider.OpenAI);
    }

    [Fact]
    public void Constructor_LocalProvider_SetsLocalModel()
    {
        var vm = CreateViewModel(provider: TranscriptionProvider.Local, localModelName: "ggml-large.bin");

        vm.TranscriptionModel.Should().Be("ggml-large.bin");
        vm.Provider.Should().Be(TranscriptionProvider.Local);
    }

    [Fact]
    public void Constructor_SetsApiKeyDisplay()
    {
        var vm = CreateViewModel(openAiApiKey: "sk-test1234567890abcdef");

        vm.OpenAiApiKeyDisplay.Should().Be("sk-...cdef");
    }

    [Fact]
    public void Constructor_EmptyApiKey_ShowsNotConfigured()
    {
        var vm = CreateViewModel(openAiApiKey: "");

        vm.OpenAiApiKeyDisplay.Should().Be("Not configured");
    }

    // --- ShowCloudUsageHint ---

    [Fact]
    public void ShowCloudUsageHint_LocalProviderCloudCorrection_ReturnsTrue()
    {
        var vm = CreateViewModel(
            provider: TranscriptionProvider.Local,
            correctionProvider: TextCorrectionProvider.Cloud);

        vm.ShowCloudUsageHint.Should().BeTrue();
    }

    [Fact]
    public void ShowCloudUsageHint_OpenAiProviderCloudCorrection_ReturnsFalse()
    {
        var vm = CreateViewModel(
            provider: TranscriptionProvider.OpenAI,
            correctionProvider: TextCorrectionProvider.Cloud);

        vm.ShowCloudUsageHint.Should().BeFalse();
    }

    // --- Provider switching ---

    [Fact]
    public void ApplyProvider_SwitchToLocal_SavesOpenAiModelAndRestoresLocal()
    {
        var vm = CreateViewModel(
            provider: TranscriptionProvider.OpenAI,
            openAiModelName: "whisper-1",
            localModelName: "ggml-large.bin");

        vm.ApplyProvider(TranscriptionProvider.Local);

        vm.Provider.Should().Be(TranscriptionProvider.Local);
        vm.TranscriptionModel.Should().Be("ggml-large.bin");
        vm.IsEditingProvider.Should().BeFalse();
        _saveCalled.Should().BeTrue();
        _preloadService.Received(1).PreloadTranscriptionModel("ggml-large.bin");
    }

    [Fact]
    public void ApplyProvider_SwitchToOpenAi_SavesLocalModelAndRestoresOpenAi()
    {
        var vm = CreateViewModel(
            provider: TranscriptionProvider.Local,
            openAiModelName: "whisper-1",
            localModelName: "ggml-large.bin");

        vm.ApplyProvider(TranscriptionProvider.OpenAI);

        vm.Provider.Should().Be(TranscriptionProvider.OpenAI);
        vm.TranscriptionModel.Should().Be("whisper-1");
        _preloadService.DidNotReceive().PreloadTranscriptionModel(Arg.Any<string>());
    }

    [Fact]
    public void SelectProviderCommand_ValidString_SwitchesProvider()
    {
        var vm = CreateViewModel(provider: TranscriptionProvider.OpenAI);

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
        var vm = CreateViewModel(openAiApiKey: "");

        vm.ApplyApiKey("sk-newkey12345678abcdefg");

        vm.OpenAiApiKey.Should().Be("sk-newkey12345678abcdefg");
        vm.OpenAiApiKeyDisplay.Should().Be("sk-...defg");
        vm.IsEditingApiKey.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ApplyModel_OpenAiProvider_UpdatesOpenAiModelName()
    {
        var vm = CreateViewModel(provider: TranscriptionProvider.OpenAI);

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
    public void ApplyCorrectionModel_SetsValueAndTriggersSave()
    {
        var vm = CreateViewModel();

        vm.ApplyCorrectionModel("gpt-4o");

        vm.CorrectionCloudModel.Should().Be("gpt-4o");
        vm.IsEditingCorrectionModel.Should().BeFalse();
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
        var vm = CreateViewModel(gpuAcceleration: true);

        vm.ToggleGpuAccelerationCommand.Execute(null);

        vm.GpuAcceleration.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ToggleCorrectionGpuAcceleration_TogglesAndTriggersSave()
    {
        var vm = CreateViewModel(correctionGpuAcceleration: true);

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
        var vm = CreateViewModel(correctionProvider: TextCorrectionProvider.Off);

        vm.SelectCorrectionProviderCommand.Execute("Cloud");

        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Cloud);
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void SelectCorrectionProvider_Local_PreloadsModel()
    {
        var vm = CreateViewModel(correctionLocalModelName: "llama-3.gguf");

        vm.SelectCorrectionProviderCommand.Execute("Local");

        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Local);
        _preloadService.Received(1).PreloadCorrectionModel("llama-3.gguf");
    }

    // --- WriteSettings ---

    [Fact]
    public void WriteSettings_WritesAllProperties()
    {
        var vm = CreateViewModel(
            provider: TranscriptionProvider.Local,
            openAiEndpoint: "https://custom.api.com",
            openAiApiKey: "sk-testapikey1234",
            openAiModelName: "whisper-1",
            localModelName: "ggml-large.bin",
            gpuAcceleration: false,
            correctionProvider: TextCorrectionProvider.Cloud,
            correctionCloudModel: "gpt-4o",
            correctionGpuAcceleration: false,
            correctionLocalModelName: "llama-3.gguf",
            useCombinedAudioModel: true,
            combinedAudioModel: "gpt-4o-audio-preview");

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
        var vm = CreateViewModel(openAiEndpoint: "");

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
}
