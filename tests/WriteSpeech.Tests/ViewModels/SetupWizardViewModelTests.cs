using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using WriteSpeech.App.ViewModels;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Configuration;
using WriteSpeech.Tests.TestHelpers;
using Microsoft.Extensions.Logging;

namespace WriteSpeech.Tests.ViewModels;

public class SetupWizardViewModelTests
{
    private readonly ISettingsPersistenceService _persistenceService = Substitute.For<ISettingsPersistenceService>();
    private readonly ILogger<SetupWizardViewModel> _logger = Substitute.For<ILogger<SetupWizardViewModel>>();

    private SetupWizardViewModel CreateViewModel()
    {
        return new SetupWizardViewModel(
            _persistenceService,
            new SynchronousDispatcherService(),
            _logger);
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
        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Off);
        vm.OpenAiApiKey.Should().BeEmpty();
        vm.CorrectionApiKey.Should().BeEmpty();
        vm.SelectedMicrophoneIndex.Should().Be(0);
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
    public void CanGoNext_LocalProvider_NoApiKeyNeeded_IsTrue()
    {
        var vm = CreateViewModel();
        vm.NavigateNext();
        vm.SelectProvider("Local");

        vm.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void CanGoNext_ParakeetProvider_NoApiKeyNeeded_IsTrue()
    {
        var vm = CreateViewModel();
        vm.NavigateNext();
        vm.SelectProvider("Parakeet");

        vm.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void CanGoNext_CorrectionStep_AlwaysTrue()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.NavigateNext();
        vm.NavigateNext(); // At Correction
        vm.SelectCorrectionProvider("Anthropic");

        // Correction is optional, so even without API key we can proceed
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
    public void SetCorrectionApiKey_TrimsAndSets()
    {
        var vm = CreateViewModel();

        vm.SetCorrectionApiKey("  key-123  ");

        vm.CorrectionApiKey.Should().Be("key-123");
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

    [Fact]
    public void NeedsCorrectionApiKey_TrueForAnthropic()
    {
        var vm = CreateViewModel();
        vm.SelectCorrectionProvider("Anthropic");

        vm.NeedsCorrectionApiKey.Should().BeTrue();
    }

    [Fact]
    public void NeedsCorrectionApiKey_FalseForOff()
    {
        var vm = CreateViewModel();
        vm.SelectCorrectionProvider("Off");

        vm.NeedsCorrectionApiKey.Should().BeFalse();
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
    public void FinishSetup_WritesAnthropicApiKey()
    {
        var vm = CreateViewModel();
        vm.Provider = TranscriptionProvider.Local;
        vm.SelectCorrectionProvider("Anthropic");
        vm.SetCorrectionApiKey("ant-key-123");

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        vm.FinishSetup();

        capturedSection!["TextCorrection"]!["Anthropic"]!["ApiKey"]!.GetValue<string>().Should().Be("ant-key-123");
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
}
