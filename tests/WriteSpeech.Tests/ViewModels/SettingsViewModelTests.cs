using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.App.ViewModels;
using WriteSpeech.App.ViewModels.Settings;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.Configuration;
using WriteSpeech.Core.Services.Hotkey;
using WriteSpeech.Core.Services.ModelManagement;
using WriteSpeech.Core.Services.Snippets;
using WriteSpeech.Core.Services.Modes;
using WriteSpeech.Core.Services.Statistics;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly IModelPreloadService _preloadService;
    private readonly WriteSpeechOptions _options;

    public SettingsViewModelTests()
    {
        _hotkeyService = Substitute.For<IGlobalHotkeyService>();
        _preloadService = Substitute.For<IModelPreloadService>();
        _options = new WriteSpeechOptions
        {
            Provider = TranscriptionProvider.OpenAI,
            Language = "de",
            Hotkey = new HotkeyOptions
            {
                Toggle = new HotkeyBinding { Modifiers = "Control, Shift", Key = "Space" },
                PushToTalk = new HotkeyBinding { Modifiers = "Control", Key = "Space" }
            },
            Audio = new AudioOptions { DeviceIndex = 0, MaxRecordingSeconds = 300 },
            Overlay = new OverlayOptions { AutoDismissSeconds = 10 },
            TextCorrection = new TextCorrectionOptions { Provider = TextCorrectionProvider.Cloud },
            OpenAI = new OpenAiOptions { ApiKey = "sk-test-key-1234", Model = "whisper-1" },
            Local = new LocalWhisperOptions { GpuAcceleration = true }
        };
    }

    private SettingsViewModel CreateViewModel(Action<WriteSpeechOptions>? configure = null)
    {
        configure?.Invoke(_options);
        return new SettingsViewModel(
            OptionsHelper.Create(o =>
            {
                o.Provider = _options.Provider;
                o.Language = _options.Language;
                o.Hotkey = _options.Hotkey;
                o.Audio = _options.Audio;
                o.Overlay = _options.Overlay;
                o.TextCorrection = _options.TextCorrection;
                o.OpenAI = _options.OpenAI;
                o.Local = _options.Local;
                o.App = _options.App;
            }),
            _hotkeyService,
            Substitute.For<IDictionaryService>(),
            Substitute.For<ISnippetService>(),
            Substitute.For<IUsageStatsService>(),
            Substitute.For<IModelManager>(),
            Substitute.For<ICorrectionModelManager>(),
            Substitute.For<IParakeetModelManager>(),
            _preloadService,
            Substitute.For<IAutoStartService>(),
            new SynchronousDispatcherService(),
            Substitute.For<IModeService>(),
            Substitute.For<ISettingsPersistenceService>(),
            NullLogger<SettingsViewModel>.Instance);
    }

    // --- Initialization ---

    [Fact]
    public void Constructor_LoadsOptionsCorrectly()
    {
        var vm = CreateViewModel();

        vm.General.ToggleModifiers.Should().Be("Control, Shift");
        vm.General.ToggleKey.Should().Be("Space");
        vm.General.PttModifiers.Should().Be("Control");
        vm.General.PttKey.Should().Be("Space");
        vm.General.SelectedLanguageCode.Should().Be("de");
        vm.Transcription.Provider.Should().Be(TranscriptionProvider.OpenAI);
        vm.Transcription.CorrectionProvider.Should().Be(TextCorrectionProvider.Cloud);
        vm.System.AudioCompressionEnabled.Should().BeFalse();
        vm.Transcription.UseCombinedAudioModel.Should().BeFalse();
        vm.System.AutoDismissSeconds.Should().Be(10);
        vm.System.MaxRecordingSeconds.Should().Be(300);
        vm.Transcription.GpuAcceleration.Should().BeTrue();
    }

    [Fact]
    public void Constructor_DefaultPage_IsGeneral()
    {
        var vm = CreateViewModel();
        vm.SelectedPage.Should().Be(SettingsPage.General);
    }

    [Fact]
    public void Constructor_SetsHotkeyDisplayText()
    {
        var vm = CreateViewModel();
        vm.General.HotkeyDisplayText.Should().Contain("Toggle");
        vm.General.HotkeyDisplayText.Should().Contain("PTT");
        vm.General.HotkeyDisplayText.Should().Contain("Ctrl");
        vm.General.HotkeyDisplayText.Should().Contain("Space");
    }

    [Fact]
    public void Constructor_SetsToggleDisplayText()
    {
        var vm = CreateViewModel();
        vm.General.ToggleDisplayText.Should().Contain("start and stop");
        vm.General.ToggleDisplayText.Should().Contain("Ctrl");
    }

    [Fact]
    public void Constructor_SetsPttDisplayText()
    {
        var vm = CreateViewModel();
        vm.General.PttDisplayText.Should().Contain("Hold");
        vm.General.PttDisplayText.Should().Contain("Ctrl");
    }

    [Fact]
    public void Constructor_SetsLanguageDisplay()
    {
        var vm = CreateViewModel();
        vm.General.SelectedLanguageDisplay.Should().Be("German");
    }

    [Fact]
    public void Constructor_NullLanguage_DisplaysAutoDetect()
    {
        var vm = CreateViewModel(o => o.Language = null);
        vm.General.SelectedLanguageDisplay.Should().Be("Auto-detect");
        vm.General.IsAutoDetectLanguage.Should().BeTrue();
    }

    [Fact]
    public void Constructor_MasksApiKey()
    {
        var vm = CreateViewModel();
        vm.Transcription.OpenAiApiKeyDisplay.Should().StartWith("sk-...");
        vm.Transcription.OpenAiApiKeyDisplay.Should().EndWith("1234");
    }

    [Fact]
    public void Constructor_EmptyApiKey_DisplaysNotConfigured()
    {
        var vm = CreateViewModel(o => o.OpenAI.ApiKey = "");
        vm.Transcription.OpenAiApiKeyDisplay.Should().Be("Not configured");
    }

    [Fact]
    public void Constructor_PopulatesToggleBadges()
    {
        var vm = CreateViewModel();
        vm.General.ToggleBadges.Should().Equal("Ctrl", "Shift", "Space");
    }

    [Fact]
    public void Constructor_PopulatesPttBadges()
    {
        var vm = CreateViewModel();
        vm.General.PttBadges.Should().Equal("Ctrl", "Space");
    }

    // --- Navigation ---

    [Fact]
    public void NavigateCommand_ChangesSelectedPage()
    {
        var vm = CreateViewModel();

        vm.NavigateCommand.Execute(SettingsPage.System);
        vm.SelectedPage.Should().Be(SettingsPage.System);

        vm.NavigateCommand.Execute(SettingsPage.Models);
        vm.SelectedPage.Should().Be(SettingsPage.Models);

        vm.NavigateCommand.Execute(SettingsPage.General);
        vm.SelectedPage.Should().Be(SettingsPage.General);
    }

    // --- Dialog System ---

    [Fact]
    public void OpenHotkeyDialog_SetsDialogState()
    {
        var vm = CreateViewModel();

        vm.General.OpenHotkeyDialogCommand.Execute(null);

        vm.General.IsDialogOpen.Should().BeTrue();
        vm.General.ActiveDialog.Should().Be(SettingsDialogType.Hotkey);
        vm.General.CapturingHotkey.Should().Be(HotkeyCaptureTarget.None);
    }

    [Fact]
    public void OpenMicrophoneDialog_SetsDialogState()
    {
        var vm = CreateViewModel();

        vm.General.OpenMicrophoneDialogCommand.Execute(null);

        vm.General.IsDialogOpen.Should().BeTrue();
        vm.General.ActiveDialog.Should().Be(SettingsDialogType.Microphone);
    }

    [Fact]
    public void OpenLanguageDialog_SetsDialogState()
    {
        var vm = CreateViewModel();

        vm.General.OpenLanguageDialogCommand.Execute(null);

        vm.General.IsDialogOpen.Should().BeTrue();
        vm.General.ActiveDialog.Should().Be(SettingsDialogType.Language);
    }

    [Fact]
    public void OpenLanguageDialog_SetsPendingLanguageCode()
    {
        var vm = CreateViewModel(); // language = "de"

        vm.General.OpenLanguageDialogCommand.Execute(null);

        vm.General.PendingLanguageCode.Should().Be("de");
    }

    [Fact]
    public void CloseDialog_ResetsDialogState()
    {
        var vm = CreateViewModel();
        vm.General.OpenHotkeyDialogCommand.Execute(null);
        vm.General.StartCapturingToggleHotkeyCommand.Execute(null);

        vm.General.CloseDialogCommand.Execute(null);

        vm.General.IsDialogOpen.Should().BeFalse();
        vm.General.ActiveDialog.Should().Be(SettingsDialogType.None);
        vm.General.CapturingHotkey.Should().Be(HotkeyCaptureTarget.None);
    }

    // --- Toggle Hotkey ---

    [Fact]
    public void StartCapturingToggleHotkey_SetsCapturingState()
    {
        var vm = CreateViewModel();
        vm.General.OpenHotkeyDialogCommand.Execute(null);

        vm.General.StartCapturingToggleHotkeyCommand.Execute(null);

        vm.General.CapturingHotkey.Should().Be(HotkeyCaptureTarget.Toggle);
    }

    [Fact]
    public void StartCapturingPttHotkey_SetsCapturingState()
    {
        var vm = CreateViewModel();
        vm.General.OpenHotkeyDialogCommand.Execute(null);

        vm.General.StartCapturingPttHotkeyCommand.Execute(null);

        vm.General.CapturingHotkey.Should().Be(HotkeyCaptureTarget.PushToTalk);
    }

    [Fact]
    public void ApplyNewHotkey_Toggle_UpdatesToggleProperties()
    {
        var vm = CreateViewModel();
        vm.General.OpenHotkeyDialogCommand.Execute(null);
        vm.General.StartCapturingToggleHotkeyCommand.Execute(null);

        vm.General.ApplyNewHotkey("Alt", "F1");

        vm.General.ToggleModifiers.Should().Be("Alt");
        vm.General.ToggleKey.Should().Be("F1");
        vm.General.CapturingHotkey.Should().Be(HotkeyCaptureTarget.None);
        vm.General.ToggleBadges.Should().Equal("Alt", "F1");
        vm.General.HotkeyDisplayText.Should().Contain("Alt");
        vm.General.HotkeyDisplayText.Should().Contain("F1");
        _hotkeyService.Received(1).UpdateToggleHotkey("Alt", "F1", null);
    }

    [Fact]
    public void ApplyNewHotkey_Toggle_DoesNotChangePttProperties()
    {
        var vm = CreateViewModel();
        vm.General.OpenHotkeyDialogCommand.Execute(null);
        vm.General.StartCapturingToggleHotkeyCommand.Execute(null);

        vm.General.ApplyNewHotkey("Alt", "F1");

        vm.General.PttModifiers.Should().Be("Control");
        vm.General.PttKey.Should().Be("Space");
        vm.General.PttBadges.Should().Equal("Ctrl", "Space");
    }

    [Fact]
    public void ApplyNewHotkey_Ptt_UpdatesPttProperties()
    {
        var vm = CreateViewModel();
        vm.General.OpenHotkeyDialogCommand.Execute(null);
        vm.General.StartCapturingPttHotkeyCommand.Execute(null);

        vm.General.ApplyNewHotkey("Shift", "F2");

        vm.General.PttModifiers.Should().Be("Shift");
        vm.General.PttKey.Should().Be("F2");
        vm.General.CapturingHotkey.Should().Be(HotkeyCaptureTarget.None);
        vm.General.PttBadges.Should().Equal("Shift", "F2");
        _hotkeyService.Received(1).UpdatePushToTalkHotkey("Shift", "F2", null);
    }

    [Fact]
    public void ApplyNewHotkey_Ptt_DoesNotChangeToggleProperties()
    {
        var vm = CreateViewModel();
        vm.General.OpenHotkeyDialogCommand.Execute(null);
        vm.General.StartCapturingPttHotkeyCommand.Execute(null);

        vm.General.ApplyNewHotkey("Shift", "F2");

        vm.General.ToggleModifiers.Should().Be("Control, Shift");
        vm.General.ToggleKey.Should().Be("Space");
        vm.General.ToggleBadges.Should().Equal("Ctrl", "Shift", "Space");
    }

    [Fact]
    public void ResetHotkeyToDefault_ResetsBothHotkeys()
    {
        var vm = CreateViewModel();
        vm.General.OpenHotkeyDialogCommand.Execute(null);

        // Change both hotkeys
        vm.General.StartCapturingToggleHotkeyCommand.Execute(null);
        vm.General.ApplyNewHotkey("Alt", "F1");
        vm.General.StartCapturingPttHotkeyCommand.Execute(null);
        vm.General.ApplyNewHotkey("Shift", "F2");

        vm.General.ResetHotkeyToDefaultCommand.Execute(null);

        vm.General.ToggleModifiers.Should().Be("Control, Shift");
        vm.General.ToggleKey.Should().Be("Space");
        vm.General.ToggleBadges.Should().Equal("Ctrl", "Shift", "Space");
        vm.General.PttModifiers.Should().Be("Control");
        vm.General.PttKey.Should().Be("Space");
        vm.General.PttBadges.Should().Equal("Ctrl", "Space");
    }

    // --- Microphone ---

    [Fact]
    public void SelectMicrophone_UpdatesIndexAndKeepsDialogOpen()
    {
        var vm = CreateViewModel();
        vm.General.OpenMicrophoneDialogCommand.Execute(null);

        vm.General.SelectMicrophone(1);

        vm.General.SelectedMicrophoneIndex.Should().Be(1);
        vm.General.IsDialogOpen.Should().BeTrue();
    }

    [Fact]
    public void AvailableMicrophones_IsPopulated()
    {
        var vm = CreateViewModel();
        // NAudio should find at least one device or the fallback entry
        vm.General.AvailableMicrophones.Should().NotBeEmpty();
    }

    // --- Language ---

    [Fact]
    public void SelectLanguage_SetsPendingCode()
    {
        var vm = CreateViewModel();
        vm.General.OpenLanguageDialogCommand.Execute(null);

        vm.General.SelectLanguageCommand.Execute("en");

        vm.General.PendingLanguageCode.Should().Be("en");
        vm.General.IsAutoDetectLanguage.Should().BeFalse();
    }

    [Fact]
    public void ToggleAutoDetectLanguage_Toggles()
    {
        var vm = CreateViewModel(); // IsAutoDetectLanguage = false (language = "de")

        vm.General.ToggleAutoDetectLanguageCommand.Execute(null);

        vm.General.IsAutoDetectLanguage.Should().BeTrue();
        vm.General.PendingLanguageCode.Should().BeNull();
    }

    [Fact]
    public void SaveAndCloseLanguage_AppliesSelectedLanguage()
    {
        var vm = CreateViewModel();
        vm.General.OpenLanguageDialogCommand.Execute(null);
        vm.General.SelectLanguageCommand.Execute("en");

        vm.General.SaveAndCloseLanguageCommand.Execute(null);

        vm.General.SelectedLanguageCode.Should().Be("en");
        vm.General.SelectedLanguageDisplay.Should().Be("English");
        vm.General.IsDialogOpen.Should().BeFalse();
    }

    [Fact]
    public void SaveAndCloseLanguage_AutoDetect_SetsNull()
    {
        var vm = CreateViewModel();
        vm.General.OpenLanguageDialogCommand.Execute(null);
        vm.General.ToggleAutoDetectLanguageCommand.Execute(null);

        vm.General.SaveAndCloseLanguageCommand.Execute(null);

        vm.General.SelectedLanguageCode.Should().BeNull();
        vm.General.SelectedLanguageDisplay.Should().Be("Auto-detect");
        vm.General.IsDialogOpen.Should().BeFalse();
    }

    [Fact]
    public void AvailableLanguages_ContainsExpectedLanguages()
    {
        var vm = CreateViewModel();
        vm.General.AvailableLanguages.Should().HaveCount(20);
        vm.General.AvailableLanguages.Should().Contain(l => l.Code == "de" && l.DisplayName == "German");
        vm.General.AvailableLanguages.Should().Contain(l => l.Code == "en" && l.DisplayName == "English");
        vm.General.AvailableLanguages.Should().Contain(l => l.Code == "fr" && l.DisplayName == "French");
        vm.General.AvailableLanguages.Should().Contain(l => l.Code == "ja" && l.DisplayName == "Japanese");
    }

    // --- System: App Settings ---

    [Fact]
    public void Constructor_LoadsAppSettings()
    {
        var vm = CreateViewModel(o =>
        {
            o.App.LaunchAtLogin = true;
            o.Overlay.AlwaysVisible = false;
            o.Overlay.ShowInTaskbar = true;
        });

        vm.System.LaunchAtLogin.Should().BeTrue();
        vm.System.OverlayAlwaysVisible.Should().BeFalse();
        vm.System.ShowInTaskbar.Should().BeTrue();
    }

    [Fact]
    public void Constructor_LoadsSoundSettings()
    {
        var vm = CreateViewModel(o =>
        {
            o.App.SoundEffects = false;
            o.Audio.MuteWhileDictating = false;
        });

        vm.System.SoundEffectsEnabled.Should().BeFalse();
        vm.System.MuteWhileDictating.Should().BeFalse();
    }

    [Fact]
    public void ToggleOverlayAlwaysVisible_BindingFlipsAndCommandRetains()
    {
        var vm = CreateViewModel(o => o.Overlay.AlwaysVisible = true);

        vm.System.OverlayAlwaysVisible = false;
        vm.System.ToggleOverlayAlwaysVisibleCommand.Execute(null);
        vm.System.OverlayAlwaysVisible.Should().BeFalse();

        vm.System.OverlayAlwaysVisible = true;
        vm.System.ToggleOverlayAlwaysVisibleCommand.Execute(null);
        vm.System.OverlayAlwaysVisible.Should().BeTrue();
    }

    [Fact]
    public void ToggleShowInTaskbar_BindingFlipsAndCommandRetains()
    {
        var vm = CreateViewModel(o => o.Overlay.ShowInTaskbar = false);

        vm.System.ShowInTaskbar = true;
        vm.System.ToggleShowInTaskbarCommand.Execute(null);
        vm.System.ShowInTaskbar.Should().BeTrue();

        vm.System.ShowInTaskbar = false;
        vm.System.ToggleShowInTaskbarCommand.Execute(null);
        vm.System.ShowInTaskbar.Should().BeFalse();
    }

    [Fact]
    public void ToggleSoundEffects_BindingFlipsAndCommandRetains()
    {
        var vm = CreateViewModel(o => o.App.SoundEffects = true);

        vm.System.SoundEffectsEnabled = false;
        vm.System.ToggleSoundEffectsCommand.Execute(null);
        vm.System.SoundEffectsEnabled.Should().BeFalse();

        vm.System.SoundEffectsEnabled = true;
        vm.System.ToggleSoundEffectsCommand.Execute(null);
        vm.System.SoundEffectsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ToggleMuteWhileDictating_BindingFlipsAndCommandRetains()
    {
        var vm = CreateViewModel(o => o.Audio.MuteWhileDictating = true);

        vm.System.MuteWhileDictating = false;
        vm.System.ToggleMuteWhileDictatingCommand.Execute(null);
        vm.System.MuteWhileDictating.Should().BeFalse();

        vm.System.MuteWhileDictating = true;
        vm.System.ToggleMuteWhileDictatingCommand.Execute(null);
        vm.System.MuteWhileDictating.Should().BeTrue();
    }

    // --- System: Transcription Settings ---

    [Fact]
    public void SelectCorrectionProvider_ChangesProvider()
    {
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Off);

        vm.Transcription.SelectCorrectionProviderCommand.Execute("Cloud");
        vm.Transcription.CorrectionProvider.Should().Be(TextCorrectionProvider.Cloud);

        vm.Transcription.SelectCorrectionProviderCommand.Execute("Off");
        vm.Transcription.CorrectionProvider.Should().Be(TextCorrectionProvider.Off);
    }

    [Fact]
    public void ApplyAutoDismiss_UpdatesValue()
    {
        var vm = CreateViewModel();
        vm.System.StartEditingAutoDismissCommand.Execute(null);

        vm.System.ApplyAutoDismiss(20);

        vm.System.AutoDismissSeconds.Should().Be(20);
        vm.System.IsEditingAutoDismiss.Should().BeFalse();
    }

    [Fact]
    public void ApplyMaxRecording_UpdatesValue()
    {
        var vm = CreateViewModel();
        vm.System.StartEditingMaxRecordingCommand.Execute(null);

        vm.System.ApplyMaxRecording(600);

        vm.System.MaxRecordingSeconds.Should().Be(600);
        vm.System.IsEditingMaxRecording.Should().BeFalse();
    }

    // --- Transcription Settings ---

    [Fact]
    public void ApplyProvider_SwitchesToLocal()
    {
        var vm = CreateViewModel();

        vm.Transcription.ApplyProvider(TranscriptionProvider.Local);

        vm.Transcription.Provider.Should().Be(TranscriptionProvider.Local);
        vm.Transcription.IsEditingProvider.Should().BeFalse();
    }

    [Fact]
    public void ApplyApiKey_UpdatesDisplayAndClearsFlag()
    {
        var vm = CreateViewModel();
        vm.Transcription.StartEditingApiKeyCommand.Execute(null);

        vm.Transcription.ApplyApiKey("sk-new-key-abcd");

        vm.Transcription.OpenAiApiKey.Should().Be("sk-new-key-abcd");
        vm.Transcription.OpenAiApiKeyDisplay.Should().EndWith("abcd");
        vm.Transcription.IsEditingApiKey.Should().BeFalse();
    }

    [Fact]
    public void ApplyModel_UpdatesValue()
    {
        var vm = CreateViewModel();
        vm.Transcription.StartEditingModelCommand.Execute(null);

        vm.Transcription.ApplyModel("gpt-4o-mini-transcribe");

        vm.Transcription.TranscriptionModel.Should().Be("gpt-4o-mini-transcribe");
        vm.Transcription.IsEditingModel.Should().BeFalse();
    }

    [Fact]
    public void ToggleGpuAcceleration_FlipsValue()
    {
        var vm = CreateViewModel(o => o.Local.GpuAcceleration = true);
        vm.Transcription.GpuAcceleration.Should().BeTrue();

        vm.Transcription.ToggleGpuAccelerationCommand.Execute(null);
        vm.Transcription.GpuAcceleration.Should().BeFalse();
    }

    // --- Audio Compression ---

    [Fact]
    public void Constructor_LoadsAudioCompressionEnabled()
    {
        var vm = CreateViewModel(o => o.Audio.CompressBeforeUpload = true);
        vm.System.AudioCompressionEnabled.Should().BeTrue();
    }

    [Fact]
    public void ToggleAudioCompression_BindingFlipsAndCommandRetains()
    {
        var vm = CreateViewModel(o => o.Audio.CompressBeforeUpload = true);

        vm.System.AudioCompressionEnabled = false;
        vm.System.ToggleAudioCompressionCommand.Execute(null);
        vm.System.AudioCompressionEnabled.Should().BeFalse();

        vm.System.AudioCompressionEnabled = true;
        vm.System.ToggleAudioCompressionCommand.Execute(null);
        vm.System.AudioCompressionEnabled.Should().BeTrue();
    }

    // --- Combined Audio Model ---

    [Fact]
    public void Constructor_LoadsUseCombinedAudioModel()
    {
        var vm = CreateViewModel(o => o.TextCorrection.UseCombinedAudioModel = true);
        vm.Transcription.UseCombinedAudioModel.Should().BeTrue();
    }

    [Fact]
    public void ToggleCombinedAudioModel_BindingFlipsAndCommandRetains()
    {
        var vm = CreateViewModel(o => o.TextCorrection.UseCombinedAudioModel = false);

        vm.Transcription.UseCombinedAudioModel = true;
        vm.Transcription.ToggleCombinedAudioModelCommand.Execute(null);
        vm.Transcription.UseCombinedAudioModel.Should().BeTrue();

        vm.Transcription.UseCombinedAudioModel = false;
        vm.Transcription.ToggleCombinedAudioModelCommand.Execute(null);
        vm.Transcription.UseCombinedAudioModel.Should().BeFalse();
    }

    [Fact]
    public void Constructor_LoadsCombinedAudioModel()
    {
        var vm = CreateViewModel(o => o.TextCorrection.CombinedAudioModel = "gpt-4o-audio-preview");
        vm.Transcription.CombinedAudioModel.Should().Be("gpt-4o-audio-preview");
    }

    [Fact]
    public void Constructor_LoadsDefaultCombinedAudioModel()
    {
        var vm = CreateViewModel();
        vm.Transcription.CombinedAudioModel.Should().Be("gpt-4o-mini-audio-preview");
    }

    [Fact]
    public void ApplyCombinedAudioModel_UpdatesValue()
    {
        var vm = CreateViewModel();
        vm.Transcription.StartEditingCombinedAudioModelCommand.Execute(null);
        vm.Transcription.IsEditingCombinedAudioModel.Should().BeTrue();

        vm.Transcription.ApplyCombinedAudioModel("gpt-4o-audio-preview");

        vm.Transcription.CombinedAudioModel.Should().Be("gpt-4o-audio-preview");
        vm.Transcription.IsEditingCombinedAudioModel.Should().BeFalse();
    }

    // --- Version ---

    [Fact]
    public void VersionText_ContainsWriteSpeech()
    {
        var vm = CreateViewModel();
        vm.VersionText.Should().StartWith("WriteSpeech v");
    }

    // --- Model Preloading ---

    [Fact]
    public void ApplyProvider_SwitchingToLocal_TriggersPreload()
    {
        var vm = CreateViewModel(o => o.Local.ModelName = "ggml-small.bin");

        vm.Transcription.ApplyProvider(TranscriptionProvider.Local);

        _preloadService.Received(1).PreloadTranscriptionModel("ggml-small.bin");
    }

    [Fact]
    public void ApplyProvider_SwitchingToOpenAI_DoesNotTriggerPreload()
    {
        var vm = CreateViewModel(o => o.Provider = TranscriptionProvider.Local);

        vm.Transcription.ApplyProvider(TranscriptionProvider.OpenAI);

        _preloadService.DidNotReceive().PreloadTranscriptionModel(Arg.Any<string?>());
    }

    [Fact]
    public void SelectCorrectionProvider_SwitchingToLocal_TriggersPreload()
    {
        var vm = CreateViewModel(o =>
        {
            o.TextCorrection.Provider = TextCorrectionProvider.Off;
            o.TextCorrection.LocalModelName = "gemma-2b.gguf";
        });

        vm.Transcription.SelectCorrectionProviderCommand.Execute("Local");

        _preloadService.Received(1).PreloadCorrectionModel("gemma-2b.gguf");
    }

    [Fact]
    public void SelectCorrectionProvider_SwitchingToCloud_DoesNotTriggerPreload()
    {
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Off);

        vm.Transcription.SelectCorrectionProviderCommand.Execute("Cloud");

        _preloadService.DidNotReceive().PreloadCorrectionModel(Arg.Any<string?>());
    }

    [Fact]
    public void ActivateModel_AlwaysTriggersPreload()
    {
        var vm = CreateViewModel(o => o.Provider = TranscriptionProvider.OpenAI);
        var model = new WhisperModel { Name = "Base", FileName = "ggml-base.bin", SizeBytes = 0 };
        var item = new ModelItemViewModel(model, Whisper.net.Ggml.GgmlType.Base);
        item.IsDownloaded = true;

        vm.Transcription.Models.ActivateModelCommand.Execute(item);

        _preloadService.Received(1).PreloadTranscriptionModel("ggml-base.bin");
    }

    [Fact]
    public void ActivateCorrectionModel_AlwaysTriggersPreload()
    {
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Off);
        var model = new CorrectionModelInfo
        {
            Name = "Gemma 2B", FileName = "gemma-2b.gguf", SizeBytes = 0, DownloadUrl = "https://example.com"
        };
        var item = new CorrectionModelItemViewModel(model);
        item.IsDownloaded = true;

        vm.Transcription.Models.ActivateCorrectionModelCommand.Execute(item);

        _preloadService.Received(1).PreloadCorrectionModel("gemma-2b.gguf");
    }

    // --- EnsureObject helper ---

    [Fact]
    public void EnsureObject_CreatesNewObjectWhenMissing()
    {
        var parent = JsonNode.Parse("{}")!;

        var result = SettingsViewModel.EnsureObject(parent, "Child");

        result.Should().NotBeNull();
        parent["Child"].Should().NotBeNull();
    }

    [Fact]
    public void EnsureObject_ReturnsExistingObject()
    {
        var parent = JsonNode.Parse("""{ "Child": { "Key": "value" } }""")!;

        var result = SettingsViewModel.EnsureObject(parent, "Child");

        result["Key"]!.GetValue<string>().Should().Be("value");
    }

    [Fact]
    public void EnsureObject_ReplacesNonObjectNode()
    {
        var parent = JsonNode.Parse("""{ "Child": "not an object" }""")!;

        var result = SettingsViewModel.EnsureObject(parent, "Child");

        result.Should().NotBeNull();
        result.Count.Should().Be(0);
    }

    // --- Mic Test ---

    [Fact]
    public void StopMicTest_WhenNotRunning_DoesNotThrow()
    {
        var vm = CreateViewModel();

        var act = () => vm.General.StopMicTest();

        act.Should().NotThrow();
        vm.General.IsMicTesting.Should().BeFalse();
        vm.General.MicTestLevel.Should().Be(0);
    }

    [Fact]
    public void StopMicTest_ResetsState()
    {
        var vm = CreateViewModel();

        // Directly set state to simulate running test (avoids needing real audio device)
        vm.General.IsMicTesting = true;
        vm.General.MicTestLevel = 0.5f;

        vm.General.StopMicTest();

        vm.General.IsMicTesting.Should().BeFalse();
        vm.General.MicTestLevel.Should().Be(0);
    }
}
