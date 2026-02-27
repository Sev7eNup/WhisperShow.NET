using System.IO;
using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using Whisper.net.Ggml;
using WriteSpeech.App.ViewModels;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Configuration;
using WriteSpeech.Core.Services.ModelManagement;
using WriteSpeech.Tests.TestHelpers;
using Microsoft.Extensions.Logging;

namespace WriteSpeech.Tests.ViewModels;

public class SetupWizardViewModelTests
{
    private readonly ISettingsPersistenceService _persistenceService = Substitute.For<ISettingsPersistenceService>();
    private readonly ILogger<SetupWizardViewModel> _logger = Substitute.For<ILogger<SetupWizardViewModel>>();
    private readonly IModelManager _modelManager = Substitute.For<IModelManager>();
    private readonly IParakeetModelManager _parakeetModelManager = Substitute.For<IParakeetModelManager>();
    private readonly IModelPreloadService _preloadService = Substitute.For<IModelPreloadService>();

    public SetupWizardViewModelTests()
    {
        _modelManager.GetAllModels().Returns([
            new WhisperModel { Name = "Tiny", FileName = "ggml-tiny.bin", SizeBytes = 75_000_000 },
            new WhisperModel { Name = "Small", FileName = "ggml-small.bin", SizeBytes = 466_000_000 },
        ]);
        _parakeetModelManager.GetAllModels().Returns([
            new ParakeetModelInfo
            {
                Name = "Parakeet TDT 0.6B v2 (int8)",
                FileName = "model",
                SizeBytes = 631_000_000,
                DirectoryName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8",
                DownloadUrl = "https://example.com"
            },
        ]);
    }

    private SetupWizardViewModel CreateViewModel(WriteSpeechOptions? options = null)
    {
        return new SetupWizardViewModel(
            _persistenceService,
            new SynchronousDispatcherService(),
            _logger,
            _modelManager,
            _parakeetModelManager,
            _preloadService,
            options);
    }

    // --- Initialization ---

    [Fact]
    public void Constructor_SetsDefaults()
    {
        var vm = CreateViewModel();

        vm.CurrentStep.Should().Be(SetupStep.Welcome);
        vm.CurrentStepIndex.Should().Be(0);
        vm.IsAutoDetectLanguage.Should().BeTrue();
        vm.SelectedLanguageCode.Should().BeNull();
        vm.Provider.Should().Be(TranscriptionProvider.OpenAI);
        vm.CloudTranscriptionProvider.Should().Be("OpenAI");
        vm.OpenAiTranscriptionModel.Should().Be("whisper-1");
        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Off);
        vm.CorrectionModel.Should().Be("gpt-4.1-mini");
        vm.OpenAiApiKey.Should().BeEmpty();
        vm.AnthropicApiKey.Should().BeEmpty();
        vm.SelectedMicrophoneIndex.Should().Be(0);
        vm.LocalGpuAcceleration.Should().BeTrue();
        vm.ParakeetGpuAcceleration.Should().BeTrue();
        vm.IsCompleted.Should().BeFalse();
        vm.CanGoBack.Should().BeFalse();
        vm.CanGoNext.Should().BeTrue();
        vm.IsLastStep.Should().BeFalse();
    }

    [Fact]
    public void Constructor_PopulatesAvailableLanguages()
    {
        var vm = CreateViewModel();

        vm.AvailableLanguages.Should().HaveCount(SupportedLanguages.All.Count);
        vm.AvailableLanguages.Should().Contain(l => l.Code == "en");
        vm.AvailableLanguages.Should().Contain(l => l.Code == "de");
    }

    [Fact]
    public void Constructor_PrePopulatesFromExistingOptions()
    {
        var options = new WriteSpeechOptions
        {
            Provider = TranscriptionProvider.Local,
            CloudTranscriptionProvider = "Groq",
            Language = "de",
            OpenAI = new OpenAiOptions { ApiKey = "sk-existing", Model = "gpt-4o-transcribe" },
            GroqTranscription = new GroqTranscriptionOptions { ApiKey = "gsk-tx", Model = "whisper-large-v3" },
            Local = new LocalWhisperOptions { GpuAcceleration = false },
            Parakeet = new ParakeetOptions { GpuAcceleration = false },
            Audio = new AudioOptions { DeviceIndex = 2 },
            TextCorrection = new TextCorrectionOptions
            {
                Provider = TextCorrectionProvider.Anthropic,
                Model = "gpt-5-mini",
                Anthropic = new AnthropicCorrectionOptions { ApiKey = "ant-key", Model = "claude-opus-4-6" },
                Google = new OpenAiCompatibleCorrectionOptions { ApiKey = "goog-key", Model = "gemini-3.1-pro-preview" },
                Groq = new OpenAiCompatibleCorrectionOptions { ApiKey = "groq-key", Model = "llama-3.3-70b-versatile" }
            }
        };

        var vm = CreateViewModel(options);

        vm.Provider.Should().Be(TranscriptionProvider.Local);
        vm.CloudTranscriptionProvider.Should().Be("Groq");
        vm.OpenAiApiKey.Should().Be("sk-existing");
        vm.OpenAiTranscriptionModel.Should().Be("gpt-4o-transcribe");
        vm.GroqTranscriptionApiKey.Should().Be("gsk-tx");
        vm.GroqTranscriptionModel.Should().Be("whisper-large-v3");
        vm.LocalGpuAcceleration.Should().BeFalse();
        vm.ParakeetGpuAcceleration.Should().BeFalse();
        vm.SelectedLanguageCode.Should().Be("de");
        vm.IsAutoDetectLanguage.Should().BeFalse();
        vm.SelectedMicrophoneIndex.Should().Be(2);
        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Anthropic);
        vm.CorrectionModel.Should().Be("gpt-5-mini");
        vm.AnthropicApiKey.Should().Be("ant-key");
        vm.AnthropicModel.Should().Be("claude-opus-4-6");
        vm.GoogleApiKey.Should().Be("goog-key");
        vm.GoogleModel.Should().Be("gemini-3.1-pro-preview");
        vm.GroqCorrectionApiKey.Should().Be("groq-key");
        vm.GroqCorrectionModel.Should().Be("llama-3.3-70b-versatile");
    }

    [Fact]
    public void Constructor_WithNullLanguage_SetsAutoDetect()
    {
        var options = new WriteSpeechOptions { Language = null };

        var vm = CreateViewModel(options);

        vm.IsAutoDetectLanguage.Should().BeTrue();
        vm.SelectedLanguageCode.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithoutOptions_UsesDefaults()
    {
        var vm = CreateViewModel();

        vm.Provider.Should().Be(TranscriptionProvider.OpenAI);
        vm.OpenAiApiKey.Should().BeEmpty();
        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Off);
    }

    // --- Navigation ---

    [Fact]
    public void NavigateNext_FromWelcome_GoesToTranscription()
    {
        var vm = CreateViewModel();

        vm.NavigateNext();

        vm.CurrentStep.Should().Be(SetupStep.Transcription);
        vm.CurrentStepIndex.Should().Be(1);
        vm.CanGoBack.Should().BeTrue();
        vm.IsLastStep.Should().BeFalse();
    }

    [Fact]
    public void NavigateNext_FromTranscription_GoesToCorrection()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local; // No API key needed
        vm.NavigateNext(); // Welcome -> Transcription

        vm.NavigateNext(); // Transcription -> Correction

        vm.CurrentStep.Should().Be(SetupStep.Correction);
        vm.CurrentStepIndex.Should().Be(2);
    }

    [Fact]
    public void NavigateNext_FromCorrection_GoesToMicrophone()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext(); // Welcome -> Transcription
        vm.NavigateNext(); // Transcription -> Correction

        vm.NavigateNext(); // Correction -> Microphone

        vm.CurrentStep.Should().Be(SetupStep.Microphone);
        vm.CurrentStepIndex.Should().Be(3);
        vm.IsLastStep.Should().BeTrue();
    }

    [Fact]
    public void NavigateBack_FromTranscription_GoesToWelcome()
    {
        var vm = CreateViewModel();
        vm.NavigateNext(); // Welcome -> Transcription

        vm.NavigateBack();

        vm.CurrentStep.Should().Be(SetupStep.Welcome);
        vm.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void NavigateBack_FromCorrection_GoesToTranscription()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // Now at Correction

        vm.NavigateBack();

        vm.CurrentStep.Should().Be(SetupStep.Transcription);
    }

    [Fact]
    public void NavigateBack_FromMicrophone_GoesToCorrection()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext();
        vm.NavigateNext(); // Now at Microphone

        vm.NavigateBack();

        vm.CurrentStep.Should().Be(SetupStep.Correction);
    }

    [Fact]
    public void NavigateBack_OnWelcomeStep_DoesNothing()
    {
        var vm = CreateViewModel();

        vm.NavigateBack();

        vm.CurrentStep.Should().Be(SetupStep.Welcome);
    }

    // --- Validation ---

    [Fact]
    public void CanGoNext_OpenAIProvider_WithoutApiKey_IsFalse()
    {
        var vm = CreateViewModel();
        vm.NavigateNext(); // Go to Transcription step
        vm.SelectProvider("OpenAI");

        vm.CanGoNext.Should().BeFalse();
    }

    [Fact]
    public void CanGoNext_OpenAIProvider_WithApiKey_IsTrue()
    {
        var vm = CreateViewModel();
        vm.NavigateNext();
        vm.SelectProvider("OpenAI");
        vm.SetOpenAiApiKey("sk-test-key");

        vm.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void CanGoNext_LocalProvider_NoActiveModel_IsFalse()
    {
        var vm = CreateViewModel();
        vm.NavigateNext();
        vm.SelectProvider("Local");

        vm.CanGoNext.Should().BeFalse();
    }

    [Fact]
    public void CanGoNext_LocalProvider_WithActiveModel_IsTrue()
    {
        var vm = CreateViewModel();
        vm.NavigateNext();
        vm.SelectProvider("Local");
        vm.WhisperModels[0].IsDownloaded = true;
        vm.SelectWhisperModel(vm.WhisperModels[0]);

        vm.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void CanGoNext_ParakeetProvider_NoActiveModel_IsFalse()
    {
        var vm = CreateViewModel();
        vm.NavigateNext();
        vm.SelectProvider("Parakeet");

        vm.CanGoNext.Should().BeFalse();
    }

    [Fact]
    public void CanGoNext_ParakeetProvider_WithActiveModel_IsTrue()
    {
        var vm = CreateViewModel();
        vm.NavigateNext();
        vm.SelectProvider("Parakeet");
        vm.ParakeetModels[0].IsDownloaded = true;
        vm.SelectParakeetModel(vm.ParakeetModels[0]);

        vm.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void CanGoNext_CorrectionStep_OffProvider_IsTrue()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // At Correction
        vm.SelectCorrectionProvider("Off");

        vm.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void CanGoNext_CorrectionStep_OpenAIProvider_WithoutApiKey_IsFalse()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // At Correction
        vm.SelectCorrectionProvider("OpenAI");

        vm.CanGoNext.Should().BeFalse();
    }

    [Fact]
    public void CanGoNext_CorrectionStep_OpenAIProvider_WithApiKey_IsTrue()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // At Correction
        vm.SetOpenAiApiKey("sk-test");
        vm.SelectCorrectionProvider("OpenAI");

        vm.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void CanGoNext_CorrectionStep_AnthropicProvider_WithoutApiKey_IsFalse()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // At Correction
        vm.SelectCorrectionProvider("Anthropic");

        vm.CanGoNext.Should().BeFalse();
    }

    [Fact]
    public void CanGoNext_CorrectionStep_AnthropicProvider_WithApiKey_IsTrue()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // At Correction
        vm.SetAnthropicApiKey("ant-key");
        vm.SelectCorrectionProvider("Anthropic");

        vm.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void CanGoNext_CorrectionStep_GoogleProvider_WithoutApiKey_IsFalse()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // At Correction
        vm.SelectCorrectionProvider("Google");

        vm.CanGoNext.Should().BeFalse();
    }

    [Fact]
    public void CanGoNext_CorrectionStep_GoogleProvider_WithApiKey_IsTrue()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // At Correction
        vm.SetGoogleApiKey("goog-key");
        vm.SelectCorrectionProvider("Google");

        vm.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void CanGoNext_CorrectionStep_GroqProvider_WithoutApiKey_IsFalse()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // At Correction
        vm.SelectCorrectionProvider("Groq");

        vm.CanGoNext.Should().BeFalse();
    }

    [Fact]
    public void CanGoNext_CorrectionStep_GroqProvider_WithApiKey_IsTrue()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // At Correction
        vm.SetGroqCorrectionApiKey("groq-key");
        vm.SelectCorrectionProvider("Groq");

        vm.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void SetAnthropicApiKey_UpdatesCanGoNext()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // At Correction
        vm.SelectCorrectionProvider("Anthropic");
        vm.CanGoNext.Should().BeFalse();

        vm.SetAnthropicApiKey("ant-key");

        vm.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void SetGoogleApiKey_UpdatesCanGoNext()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // At Correction
        vm.SelectCorrectionProvider("Google");
        vm.CanGoNext.Should().BeFalse();

        vm.SetGoogleApiKey("goog-key");

        vm.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void SetGroqCorrectionApiKey_UpdatesCanGoNext()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // At Correction
        vm.SelectCorrectionProvider("Groq");
        vm.CanGoNext.Should().BeFalse();

        vm.SetGroqCorrectionApiKey("groq-key");

        vm.CanGoNext.Should().BeTrue();
    }

    // --- Provider selection ---

    [Fact]
    public void SelectProvider_SetsProvider()
    {
        var vm = CreateViewModel();

        vm.SelectProvider("Local");
        vm.Provider.Should().Be(TranscriptionProvider.Local);

        vm.SelectProvider("Parakeet");
        vm.Provider.Should().Be(TranscriptionProvider.Parakeet);

        vm.SelectProvider("OpenAI");
        vm.Provider.Should().Be(TranscriptionProvider.OpenAI);
    }

    [Fact]
    public void SelectProvider_InvalidValue_DoesNotChange()
    {
        var vm = CreateViewModel();
        vm.SelectProvider("Invalid");

        vm.Provider.Should().Be(TranscriptionProvider.OpenAI); // Default
    }

    [Fact]
    public void SelectCorrectionProvider_SetsProvider()
    {
        var vm = CreateViewModel();

        vm.SelectCorrectionProvider("OpenAI");
        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.OpenAI);

        vm.SelectCorrectionProvider("Anthropic");
        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Anthropic);

        vm.SelectCorrectionProvider("Off");
        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Off);
    }

    // --- Language ---

    [Fact]
    public void SelectLanguage_SetsCodeAndDisablesAutoDetect()
    {
        var vm = CreateViewModel();

        vm.SelectLanguage("de");

        vm.SelectedLanguageCode.Should().Be("de");
        vm.IsAutoDetectLanguage.Should().BeFalse();
    }

    [Fact]
    public void ToggleAutoDetect_TogglesAndClearsCode()
    {
        var vm = CreateViewModel();
        vm.SelectLanguage("en"); // Auto-detect now false

        vm.ToggleAutoDetect(); // Back to true

        vm.IsAutoDetectLanguage.Should().BeTrue();
        vm.SelectedLanguageCode.Should().BeNull();
    }

    [Fact]
    public void ToggleAutoDetect_WhenOn_TurnsOff()
    {
        var vm = CreateViewModel();
        vm.IsAutoDetectLanguage.Should().BeTrue();

        vm.ToggleAutoDetect();

        vm.IsAutoDetectLanguage.Should().BeFalse();
    }

    // --- API key ---

    [Fact]
    public void SetOpenAiApiKey_TrimsAndSets()
    {
        var vm = CreateViewModel();

        vm.SetOpenAiApiKey("  sk-test  ");

        vm.OpenAiApiKey.Should().Be("sk-test");
    }

    [Fact]
    public void SetGroqTranscriptionApiKey_TrimsAndSets()
    {
        var vm = CreateViewModel();
        vm.SetGroqTranscriptionApiKey("  gsk-123  ");
        vm.GroqTranscriptionApiKey.Should().Be("gsk-123");
    }

    [Fact]
    public void SetAnthropicApiKey_TrimsAndSets()
    {
        var vm = CreateViewModel();
        vm.SetAnthropicApiKey("  ant-key  ");
        vm.AnthropicApiKey.Should().Be("ant-key");
    }

    [Fact]
    public void SetGoogleApiKey_TrimsAndSets()
    {
        var vm = CreateViewModel();
        vm.SetGoogleApiKey("  goog-key  ");
        vm.GoogleApiKey.Should().Be("goog-key");
    }

    [Fact]
    public void SetGroqCorrectionApiKey_TrimsAndSets()
    {
        var vm = CreateViewModel();
        vm.SetGroqCorrectionApiKey("  groq-key  ");
        vm.GroqCorrectionApiKey.Should().Be("groq-key");
    }

    // --- Reusable OpenAI key ---

    [Fact]
    public void IsReusableOpenAiKey_TrueWhenCorrectionIsOpenAIAndKeyExists()
    {
        var vm = CreateViewModel();
        vm.SetOpenAiApiKey("sk-test");
        vm.SelectCorrectionProvider("OpenAI");

        vm.IsReusableOpenAiKey.Should().BeTrue();
    }

    [Fact]
    public void IsReusableOpenAiKey_FalseWhenNoKey()
    {
        var vm = CreateViewModel();
        vm.SelectCorrectionProvider("OpenAI");

        vm.IsReusableOpenAiKey.Should().BeFalse();
    }

    // --- Cloud sub-provider ---

    [Fact]
    public void SelectCloudTranscriptionProvider_SetsProvider()
    {
        var vm = CreateViewModel();
        vm.SelectCloudTranscriptionProvider("Groq");
        vm.CloudTranscriptionProvider.Should().Be("Groq");
    }

    [Fact]
    public void CanGoNext_CloudGroq_WithoutApiKey_IsFalse()
    {
        var vm = CreateViewModel();
        vm.NavigateNext(); // Go to Transcription
        vm.SelectProvider("OpenAI");
        vm.SelectCloudTranscriptionProvider("Groq");

        vm.CanGoNext.Should().BeFalse();
    }

    [Fact]
    public void CanGoNext_CloudGroq_WithApiKey_IsTrue()
    {
        var vm = CreateViewModel();
        vm.NavigateNext();
        vm.SelectProvider("OpenAI");
        vm.SelectCloudTranscriptionProvider("Groq");
        vm.SetGroqTranscriptionApiKey("gsk-test");

        vm.CanGoNext.Should().BeTrue();
    }

    // --- Model selection ---

    [Fact]
    public void SelectTranscriptionModel_OpenAI_SetsModel()
    {
        var vm = CreateViewModel();
        vm.SelectTranscriptionModel("gpt-4o-transcribe");
        vm.OpenAiTranscriptionModel.Should().Be("gpt-4o-transcribe");
    }

    [Fact]
    public void SelectTranscriptionModel_Groq_SetsModel()
    {
        var vm = CreateViewModel();
        vm.SelectCloudTranscriptionProvider("Groq");
        vm.SelectTranscriptionModel("whisper-large-v3");
        vm.GroqTranscriptionModel.Should().Be("whisper-large-v3");
    }

    [Fact]
    public void SelectCorrectionModel_OpenAI_SetsModel()
    {
        var vm = CreateViewModel();
        vm.SelectCorrectionProvider("OpenAI");
        vm.SelectCorrectionModel("gpt-5-mini");
        vm.CorrectionModel.Should().Be("gpt-5-mini");
    }

    [Fact]
    public void SelectCorrectionModel_Anthropic_SetsModel()
    {
        var vm = CreateViewModel();
        vm.SelectCorrectionProvider("Anthropic");
        vm.SelectCorrectionModel("claude-opus-4-6");
        vm.AnthropicModel.Should().Be("claude-opus-4-6");
    }

    [Fact]
    public void SelectCorrectionModel_Google_SetsModel()
    {
        var vm = CreateViewModel();
        vm.SelectCorrectionProvider("Google");
        vm.SelectCorrectionModel("gemini-3.1-pro-preview");
        vm.GoogleModel.Should().Be("gemini-3.1-pro-preview");
    }

    [Fact]
    public void SelectCorrectionModel_Groq_SetsModel()
    {
        var vm = CreateViewModel();
        vm.SelectCorrectionProvider("Groq");
        vm.SelectCorrectionModel("llama-3.3-70b-versatile");
        vm.GroqCorrectionModel.Should().Be("llama-3.3-70b-versatile");
    }

    // --- Microphone ---

    [Fact]
    public void SelectMicrophone_UpdatesIndex()
    {
        var vm = CreateViewModel();

        vm.SelectMicrophone(2);

        vm.SelectedMicrophoneIndex.Should().Be(2);
    }

    // --- FinishSetup & Persistence ---

    [Fact]
    public void FinishSetup_SetsIsCompleted()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;

        vm.FinishSetup();

        vm.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void FinishSetup_FiresPropertyChangedForIsCompleted()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        string? changedProperty = null;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SetupWizardViewModel.IsCompleted))
                changedProperty = e.PropertyName;
        };

        vm.FinishSetup();

        changedProperty.Should().Be(nameof(SetupWizardViewModel.IsCompleted));
    }

    [Fact]
    public void FinishSetup_CallsScheduleUpdate()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;

        vm.FinishSetup();

        _persistenceService.Received(1).ScheduleUpdate(Arg.Any<Action<JsonNode>>());
    }

    [Fact]
    public void FinishSetup_WritesSetupCompleted()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                var mutator = call.Arg<Action<JsonNode>>();
                capturedSection = new JsonObject();
                mutator(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["App"]!["SetupCompleted"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void FinishSetup_WritesProvider()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Parakeet;

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["Provider"]!.GetValue<string>().Should().Be("Parakeet");
    }

    [Fact]
    public void FinishSetup_WritesLanguage_WhenSelected()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.SelectLanguage("de");

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["Language"]!.GetValue<string>().Should().Be("de");
    }

    [Fact]
    public void FinishSetup_WritesNullLanguage_WhenAutoDetect()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        // Setting JsonNode to null removes the key from the object
        capturedSection!["Language"].Should().BeNull();
    }

    [Fact]
    public void FinishSetup_WritesOpenAiApiKey()
    {
        var vm = CreateViewModel();
        vm.SetOpenAiApiKey("sk-test-key");

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["OpenAI"]!["ApiKey"]!.GetValue<string>().Should().Be("sk-test-key");
    }

    [Fact]
    public void FinishSetup_WritesCorrectionProvider()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.SelectCorrectionProvider("Anthropic");

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["TextCorrection"]!["Provider"]!.GetValue<string>().Should().Be("Anthropic");
    }

    [Fact]
    public void FinishSetup_WritesAnthropicApiKeyAndModel()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.SelectCorrectionProvider("Anthropic");
        vm.SetAnthropicApiKey("ant-key-123");
        vm.SelectCorrectionModel("claude-opus-4-6");

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["TextCorrection"]!["Anthropic"]!["ApiKey"]!.GetValue<string>().Should().Be("ant-key-123");
        capturedSection!["TextCorrection"]!["Anthropic"]!["Model"]!.GetValue<string>().Should().Be("claude-opus-4-6");
    }

    [Fact]
    public void FinishSetup_WritesCloudTranscriptionProvider()
    {
        var vm = CreateViewModel();
        vm.SelectCloudTranscriptionProvider("Groq");
        vm.SetGroqTranscriptionApiKey("gsk-key");

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["CloudTranscriptionProvider"]!.GetValue<string>().Should().Be("Groq");
        capturedSection!["GroqTranscription"]!["ApiKey"]!.GetValue<string>().Should().Be("gsk-key");
    }

    [Fact]
    public void FinishSetup_WritesOpenAiTranscriptionModel()
    {
        var vm = CreateViewModel();
        vm.SetOpenAiApiKey("sk-test");
        vm.SelectTranscriptionModel("gpt-4o-transcribe");

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["OpenAI"]!["Model"]!.GetValue<string>().Should().Be("gpt-4o-transcribe");
    }

    [Fact]
    public void FinishSetup_WritesLocalGpuAcceleration()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.LocalGpuAcceleration = false;

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["Local"]!["GpuAcceleration"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void FinishSetup_WritesParakeetGpuAcceleration()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Parakeet;
        vm.ParakeetGpuAcceleration = false;

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["Parakeet"]!["GpuAcceleration"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void FinishSetup_WritesCorrectionModel_ForOpenAI()
    {
        var vm = CreateViewModel();
        vm.SetOpenAiApiKey("sk-test");
        vm.SelectCorrectionProvider("OpenAI");
        vm.SelectCorrectionModel("gpt-5-mini");

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["TextCorrection"]!["Model"]!.GetValue<string>().Should().Be("gpt-5-mini");
    }

    [Fact]
    public void FinishSetup_WritesMicrophoneIndex()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.SelectMicrophone(3);

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["Audio"]!["DeviceIndex"]!.GetValue<int>().Should().Be(3);
    }

    [Fact]
    public void FinishSetup_ReusesOpenAiKey_ForOpenAICorrection()
    {
        var vm = CreateViewModel();
        vm.SetOpenAiApiKey("sk-shared-key");
        vm.SelectCorrectionProvider("OpenAI");

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["OpenAI"]!["ApiKey"]!.GetValue<string>().Should().Be("sk-shared-key");
    }

    // --- NavigateNext on last step calls FinishSetup ---

    [Fact]
    public void NavigateNext_OnLastStep_CallsFinishSetup()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext(); // Welcome -> Transcription
        vm.NavigateNext(); // Transcription -> Correction
        vm.NavigateNext(); // Correction -> Microphone

        vm.NavigateNext(); // Should call FinishSetup

        vm.IsCompleted.Should().BeTrue();
        _persistenceService.Received(1).ScheduleUpdate(Arg.Any<Action<JsonNode>>());
    }

    // --- Local model download ---

    [Fact]
    public void Constructor_LoadsWhisperModels()
    {
        var vm = CreateViewModel();

        vm.WhisperModels.Should().HaveCount(2);
        vm.WhisperModels[0].Name.Should().Be("Tiny");
        vm.WhisperModels[1].Name.Should().Be("Small");
    }

    [Fact]
    public void Constructor_LoadsParakeetModels()
    {
        var vm = CreateViewModel();

        vm.ParakeetModels.Should().HaveCount(1);
        vm.ParakeetModels[0].Name.Should().Be("Parakeet TDT 0.6B v2 (int8)");
    }

    [Fact]
    public void Constructor_PrePopulatesActiveWhisperModel_WhenDownloaded()
    {
        // WhisperModel.IsDownloaded checks File.Exists, so use a real file
        var tempFile = Path.GetTempFileName();
        try
        {
            _modelManager.GetAllModels().Returns([
                new WhisperModel { Name = "Tiny", FileName = "ggml-tiny.bin", SizeBytes = 75_000_000 },
                new WhisperModel { Name = "Small", FileName = "ggml-small.bin", SizeBytes = 466_000_000,
                    FilePath = tempFile },
            ]);

            var options = new WriteSpeechOptions
            {
                Provider = TranscriptionProvider.Local,
                Local = new LocalWhisperOptions { ModelName = "ggml-small.bin" }
            };

            var vm = CreateViewModel(options);

            vm.WhisperModels[1].IsActive.Should().BeTrue();
            vm.WhisperModels[1].StatusText.Should().Be("Active");
            vm.WhisperModels[0].IsActive.Should().BeFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void SelectWhisperModel_ActivatesModel()
    {
        var vm = CreateViewModel();
        vm.WhisperModels[0].IsDownloaded = true;
        vm.WhisperModels[1].IsDownloaded = true;

        vm.SelectWhisperModel(vm.WhisperModels[1]);

        vm.WhisperModels[1].IsActive.Should().BeTrue();
        vm.WhisperModels[1].StatusText.Should().Be("Active");
        vm.WhisperModels[0].IsActive.Should().BeFalse();
        vm.LocalModelName.Should().Be("ggml-small.bin");
    }

    [Fact]
    public void SelectWhisperModel_DeactivatesParakeetModels()
    {
        var vm = CreateViewModel();
        vm.ParakeetModels[0].IsDownloaded = true;
        vm.ParakeetModels[0].IsActive = true;
        vm.WhisperModels[0].IsDownloaded = true;

        vm.SelectWhisperModel(vm.WhisperModels[0]);

        vm.ParakeetModels[0].IsActive.Should().BeFalse();
        vm.WhisperModels[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public void SelectWhisperModel_DoesNothing_WhenNotDownloaded()
    {
        var vm = CreateViewModel();

        vm.SelectWhisperModel(vm.WhisperModels[0]);

        vm.WhisperModels[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public void SelectWhisperModel_CallsPreloadService()
    {
        var vm = CreateViewModel();
        vm.WhisperModels[0].IsDownloaded = true;

        vm.SelectWhisperModel(vm.WhisperModels[0]);

        _preloadService.Received(1).PreloadTranscriptionModel("ggml-tiny.bin");
        _preloadService.Received(1).UnloadParakeetModel();
    }

    [Fact]
    public void SelectParakeetModel_ActivatesModel()
    {
        var vm = CreateViewModel();
        vm.ParakeetModels[0].IsDownloaded = true;

        vm.SelectParakeetModel(vm.ParakeetModels[0]);

        vm.ParakeetModels[0].IsActive.Should().BeTrue();
        vm.ParakeetModels[0].StatusText.Should().Be("Active");
        vm.ParakeetModelName.Should().Be("sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8");
    }

    [Fact]
    public void SelectParakeetModel_DeactivatesWhisperModels()
    {
        var vm = CreateViewModel();
        vm.WhisperModels[0].IsDownloaded = true;
        vm.WhisperModels[0].IsActive = true;
        vm.ParakeetModels[0].IsDownloaded = true;

        vm.SelectParakeetModel(vm.ParakeetModels[0]);

        vm.WhisperModels[0].IsActive.Should().BeFalse();
        vm.ParakeetModels[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public void SelectParakeetModel_CallsPreloadService()
    {
        var vm = CreateViewModel();
        vm.ParakeetModels[0].IsDownloaded = true;

        vm.SelectParakeetModel(vm.ParakeetModels[0]);

        _preloadService.Received(1).PreloadParakeetModel();
        _preloadService.Received(1).UnloadTranscriptionModel();
    }

    [Fact]
    public async Task DownloadWhisperModel_AutoActivatesFirstDownload()
    {
        _modelManager.DownloadModelAsync(Arg.Any<GgmlType>(), Arg.Any<IProgress<float>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var vm = CreateViewModel();
        await vm.DownloadWhisperModel(vm.WhisperModels[0]);

        vm.WhisperModels[0].IsDownloaded.Should().BeTrue();
        vm.WhisperModels[0].IsActive.Should().BeTrue();
        vm.LocalModelName.Should().Be("ggml-tiny.bin");
    }

    [Fact]
    public async Task DownloadParakeetModel_AutoActivatesFirstDownload()
    {
        _parakeetModelManager.DownloadModelAsync(Arg.Any<string>(), Arg.Any<IProgress<float>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var vm = CreateViewModel();
        await vm.DownloadParakeetModel(vm.ParakeetModels[0]);

        vm.ParakeetModels[0].IsDownloaded.Should().BeTrue();
        vm.ParakeetModels[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task DownloadWhisperModel_SetsErrorOnFailure()
    {
        _modelManager.DownloadModelAsync(Arg.Any<GgmlType>(), Arg.Any<IProgress<float>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new Exception("Network error")));

        var vm = CreateViewModel();
        await vm.DownloadWhisperModel(vm.WhisperModels[0]);

        vm.WhisperModels[0].StatusText.Should().Contain("Error");
        vm.WhisperModels[0].IsDownloaded.Should().BeFalse();
        vm.WhisperModels[0].IsDownloading.Should().BeFalse();
    }

    [Fact]
    public void FinishSetup_WritesLocalModelName()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.LocalModelName = "ggml-medium.bin";

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["Local"]!["ModelName"]!.GetValue<string>().Should().Be("ggml-medium.bin");
    }

    [Fact]
    public void FinishSetup_WritesParakeetModelName()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Parakeet;

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["Parakeet"]!["ModelName"]!.GetValue<string>().Should().Be("sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8");
    }

    [Fact]
    public void FileNameToGgmlType_MapsCorrectly()
    {
        SetupWizardViewModel.FileNameToGgmlType("ggml-tiny.bin").Should().Be(GgmlType.Tiny);
        SetupWizardViewModel.FileNameToGgmlType("ggml-base.bin").Should().Be(GgmlType.Base);
        SetupWizardViewModel.FileNameToGgmlType("ggml-small.bin").Should().Be(GgmlType.Small);
        SetupWizardViewModel.FileNameToGgmlType("ggml-medium.bin").Should().Be(GgmlType.Medium);
        SetupWizardViewModel.FileNameToGgmlType("ggml-large-v3.bin").Should().Be(GgmlType.LargeV3);
        SetupWizardViewModel.FileNameToGgmlType("ggml-large-v3-turbo.bin").Should().Be(GgmlType.LargeV3Turbo);
    }

    [Fact]
    public void FileNameToGgmlType_ThrowsForUnknown()
    {
        var act = () => SetupWizardViewModel.FileNameToGgmlType("unknown.bin");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_PrePopulatesLocalModelName()
    {
        var options = new WriteSpeechOptions
        {
            Local = new LocalWhisperOptions { ModelName = "ggml-medium.bin" }
        };

        var vm = CreateViewModel(options);

        vm.LocalModelName.Should().Be("ggml-medium.bin");
    }

    [Fact]
    public void Constructor_PrePopulatesParakeetModelName()
    {
        var options = new WriteSpeechOptions
        {
            Parakeet = new ParakeetOptions { ModelName = "custom-model" }
        };

        var vm = CreateViewModel(options);

        vm.ParakeetModelName.Should().Be("custom-model");
    }
}
