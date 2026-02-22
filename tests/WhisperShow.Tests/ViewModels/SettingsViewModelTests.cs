using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WhisperShow.App.ViewModels;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services.Hotkey;
using WhisperShow.Core.Services.ModelManagement;
using WhisperShow.Core.Services.Snippets;
using WhisperShow.Core.Services.Statistics;
using WhisperShow.Core.Services.TextCorrection;
using WhisperShow.Tests.TestHelpers;

namespace WhisperShow.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly IModelPreloadService _preloadService;
    private readonly WhisperShowOptions _options;

    public SettingsViewModelTests()
    {
        _hotkeyService = Substitute.For<IGlobalHotkeyService>();
        _preloadService = Substitute.For<IModelPreloadService>();
        _options = new WhisperShowOptions
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

    private SettingsViewModel CreateViewModel(Action<WhisperShowOptions>? configure = null)
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
            _preloadService,
            NullLogger<SettingsViewModel>.Instance);
    }

    // --- Initialization ---

    [Fact]
    public void Constructor_LoadsOptionsCorrectly()
    {
        var vm = CreateViewModel();

        vm.ToggleModifiers.Should().Be("Control, Shift");
        vm.ToggleKey.Should().Be("Space");
        vm.PttModifiers.Should().Be("Control");
        vm.PttKey.Should().Be("Space");
        vm.SelectedLanguageCode.Should().Be("de");
        vm.Provider.Should().Be(TranscriptionProvider.OpenAI);
        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Cloud);
        vm.AudioCompressionEnabled.Should().BeTrue();
        vm.UseCombinedAudioModel.Should().BeFalse();
        vm.AutoDismissSeconds.Should().Be(10);
        vm.MaxRecordingSeconds.Should().Be(300);
        vm.GpuAcceleration.Should().BeTrue();
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
        vm.HotkeyDisplayText.Should().Contain("Toggle");
        vm.HotkeyDisplayText.Should().Contain("PTT");
        vm.HotkeyDisplayText.Should().Contain("Ctrl");
        vm.HotkeyDisplayText.Should().Contain("Space");
    }

    [Fact]
    public void Constructor_SetsToggleDisplayText()
    {
        var vm = CreateViewModel();
        vm.ToggleDisplayText.Should().Contain("start and stop");
        vm.ToggleDisplayText.Should().Contain("Ctrl");
    }

    [Fact]
    public void Constructor_SetsPttDisplayText()
    {
        var vm = CreateViewModel();
        vm.PttDisplayText.Should().Contain("Hold");
        vm.PttDisplayText.Should().Contain("Ctrl");
    }

    [Fact]
    public void Constructor_SetsLanguageDisplay()
    {
        var vm = CreateViewModel();
        vm.SelectedLanguageDisplay.Should().Be("German");
    }

    [Fact]
    public void Constructor_NullLanguage_DisplaysAutoDetect()
    {
        var vm = CreateViewModel(o => o.Language = null);
        vm.SelectedLanguageDisplay.Should().Be("Auto-detect");
        vm.IsAutoDetectLanguage.Should().BeTrue();
    }

    [Fact]
    public void Constructor_MasksApiKey()
    {
        var vm = CreateViewModel();
        vm.OpenAiApiKeyDisplay.Should().StartWith("sk-...");
        vm.OpenAiApiKeyDisplay.Should().EndWith("1234");
    }

    [Fact]
    public void Constructor_EmptyApiKey_DisplaysNotConfigured()
    {
        var vm = CreateViewModel(o => o.OpenAI.ApiKey = "");
        vm.OpenAiApiKeyDisplay.Should().Be("Not configured");
    }

    [Fact]
    public void Constructor_PopulatesToggleBadges()
    {
        var vm = CreateViewModel();
        vm.ToggleBadges.Should().Equal("Ctrl", "Shift", "Space");
    }

    [Fact]
    public void Constructor_PopulatesPttBadges()
    {
        var vm = CreateViewModel();
        vm.PttBadges.Should().Equal("Ctrl", "Space");
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

        vm.OpenHotkeyDialogCommand.Execute(null);

        vm.IsDialogOpen.Should().BeTrue();
        vm.ActiveDialog.Should().Be("Hotkey");
        vm.CapturingHotkey.Should().BeEmpty();
    }

    [Fact]
    public void OpenMicrophoneDialog_SetsDialogState()
    {
        var vm = CreateViewModel();

        vm.OpenMicrophoneDialogCommand.Execute(null);

        vm.IsDialogOpen.Should().BeTrue();
        vm.ActiveDialog.Should().Be("Microphone");
    }

    [Fact]
    public void OpenLanguageDialog_SetsDialogState()
    {
        var vm = CreateViewModel();

        vm.OpenLanguageDialogCommand.Execute(null);

        vm.IsDialogOpen.Should().BeTrue();
        vm.ActiveDialog.Should().Be("Language");
    }

    [Fact]
    public void OpenLanguageDialog_SetsPendingLanguageCode()
    {
        var vm = CreateViewModel(); // language = "de"

        vm.OpenLanguageDialogCommand.Execute(null);

        vm.PendingLanguageCode.Should().Be("de");
    }

    [Fact]
    public void CloseDialog_ResetsDialogState()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);
        vm.StartCapturingToggleHotkeyCommand.Execute(null);

        vm.CloseDialogCommand.Execute(null);

        vm.IsDialogOpen.Should().BeFalse();
        vm.ActiveDialog.Should().BeEmpty();
        vm.CapturingHotkey.Should().BeEmpty();
    }

    // --- Toggle Hotkey ---

    [Fact]
    public void StartCapturingToggleHotkey_SetsCapturingState()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);

        vm.StartCapturingToggleHotkeyCommand.Execute(null);

        vm.CapturingHotkey.Should().Be("Toggle");
    }

    [Fact]
    public void StartCapturingPttHotkey_SetsCapturingState()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);

        vm.StartCapturingPttHotkeyCommand.Execute(null);

        vm.CapturingHotkey.Should().Be("PushToTalk");
    }

    [Fact]
    public void ApplyNewHotkey_Toggle_UpdatesToggleProperties()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);
        vm.StartCapturingToggleHotkeyCommand.Execute(null);

        vm.ApplyNewHotkey("Alt", "F1");

        vm.ToggleModifiers.Should().Be("Alt");
        vm.ToggleKey.Should().Be("F1");
        vm.CapturingHotkey.Should().BeEmpty();
        vm.ToggleBadges.Should().Equal("Alt", "F1");
        vm.HotkeyDisplayText.Should().Contain("Alt");
        vm.HotkeyDisplayText.Should().Contain("F1");
        _hotkeyService.Received(1).UpdateToggleHotkey("Alt", "F1");
    }

    [Fact]
    public void ApplyNewHotkey_Toggle_DoesNotChangePttProperties()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);
        vm.StartCapturingToggleHotkeyCommand.Execute(null);

        vm.ApplyNewHotkey("Alt", "F1");

        vm.PttModifiers.Should().Be("Control");
        vm.PttKey.Should().Be("Space");
        vm.PttBadges.Should().Equal("Ctrl", "Space");
    }

    [Fact]
    public void ApplyNewHotkey_Ptt_UpdatesPttProperties()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);
        vm.StartCapturingPttHotkeyCommand.Execute(null);

        vm.ApplyNewHotkey("Shift", "F2");

        vm.PttModifiers.Should().Be("Shift");
        vm.PttKey.Should().Be("F2");
        vm.CapturingHotkey.Should().BeEmpty();
        vm.PttBadges.Should().Equal("Shift", "F2");
        _hotkeyService.Received(1).UpdatePushToTalkHotkey("Shift", "F2");
    }

    [Fact]
    public void ApplyNewHotkey_Ptt_DoesNotChangeToggleProperties()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);
        vm.StartCapturingPttHotkeyCommand.Execute(null);

        vm.ApplyNewHotkey("Shift", "F2");

        vm.ToggleModifiers.Should().Be("Control, Shift");
        vm.ToggleKey.Should().Be("Space");
        vm.ToggleBadges.Should().Equal("Ctrl", "Shift", "Space");
    }

    [Fact]
    public void ResetHotkeyToDefault_ResetsBothHotkeys()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);

        // Change both hotkeys
        vm.StartCapturingToggleHotkeyCommand.Execute(null);
        vm.ApplyNewHotkey("Alt", "F1");
        vm.StartCapturingPttHotkeyCommand.Execute(null);
        vm.ApplyNewHotkey("Shift", "F2");

        vm.ResetHotkeyToDefaultCommand.Execute(null);

        vm.ToggleModifiers.Should().Be("Control, Shift");
        vm.ToggleKey.Should().Be("Space");
        vm.ToggleBadges.Should().Equal("Ctrl", "Shift", "Space");
        vm.PttModifiers.Should().Be("Control");
        vm.PttKey.Should().Be("Space");
        vm.PttBadges.Should().Equal("Ctrl", "Space");
    }

    // --- Microphone ---

    [Fact]
    public void SelectMicrophone_UpdatesIndexAndClosesDialog()
    {
        var vm = CreateViewModel();
        vm.OpenMicrophoneDialogCommand.Execute(null);

        vm.SelectMicrophone(1);

        vm.SelectedMicrophoneIndex.Should().Be(1);
        vm.IsDialogOpen.Should().BeFalse();
    }

    [Fact]
    public void AvailableMicrophones_IsPopulated()
    {
        var vm = CreateViewModel();
        // NAudio should find at least one device or the fallback entry
        vm.AvailableMicrophones.Should().NotBeEmpty();
    }

    // --- Language ---

    [Fact]
    public void SelectLanguage_SetsPendingCode()
    {
        var vm = CreateViewModel();
        vm.OpenLanguageDialogCommand.Execute(null);

        vm.SelectLanguageCommand.Execute("en");

        vm.PendingLanguageCode.Should().Be("en");
        vm.IsAutoDetectLanguage.Should().BeFalse();
    }

    [Fact]
    public void ToggleAutoDetectLanguage_Toggles()
    {
        var vm = CreateViewModel(); // IsAutoDetectLanguage = false (language = "de")

        vm.ToggleAutoDetectLanguageCommand.Execute(null);

        vm.IsAutoDetectLanguage.Should().BeTrue();
        vm.PendingLanguageCode.Should().BeNull();
    }

    [Fact]
    public void SaveAndCloseLanguage_AppliesSelectedLanguage()
    {
        var vm = CreateViewModel();
        vm.OpenLanguageDialogCommand.Execute(null);
        vm.SelectLanguageCommand.Execute("en");

        vm.SaveAndCloseLanguageCommand.Execute(null);

        vm.SelectedLanguageCode.Should().Be("en");
        vm.SelectedLanguageDisplay.Should().Be("English");
        vm.IsDialogOpen.Should().BeFalse();
    }

    [Fact]
    public void SaveAndCloseLanguage_AutoDetect_SetsNull()
    {
        var vm = CreateViewModel();
        vm.OpenLanguageDialogCommand.Execute(null);
        vm.ToggleAutoDetectLanguageCommand.Execute(null);

        vm.SaveAndCloseLanguageCommand.Execute(null);

        vm.SelectedLanguageCode.Should().BeNull();
        vm.SelectedLanguageDisplay.Should().Be("Auto-detect");
        vm.IsDialogOpen.Should().BeFalse();
    }

    [Fact]
    public void AvailableLanguages_ContainsExpectedLanguages()
    {
        var vm = CreateViewModel();
        vm.AvailableLanguages.Should().HaveCount(20);
        vm.AvailableLanguages.Should().Contain(l => l.Code == "de" && l.DisplayName == "German");
        vm.AvailableLanguages.Should().Contain(l => l.Code == "en" && l.DisplayName == "English");
        vm.AvailableLanguages.Should().Contain(l => l.Code == "fr" && l.DisplayName == "French");
        vm.AvailableLanguages.Should().Contain(l => l.Code == "ja" && l.DisplayName == "Japanese");
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

        vm.LaunchAtLogin.Should().BeTrue();
        vm.OverlayAlwaysVisible.Should().BeFalse();
        vm.ShowInTaskbar.Should().BeTrue();
    }

    [Fact]
    public void Constructor_LoadsSoundSettings()
    {
        var vm = CreateViewModel(o =>
        {
            o.App.SoundEffects = false;
            o.Audio.MuteWhileDictating = false;
        });

        vm.SoundEffectsEnabled.Should().BeFalse();
        vm.MuteWhileDictating.Should().BeFalse();
    }

    [Fact]
    public void ToggleOverlayAlwaysVisible_BindingFlipsAndCommandRetains()
    {
        var vm = CreateViewModel(o => o.Overlay.AlwaysVisible = true);

        // Simulate ToggleButton: binding flips, then command fires
        vm.OverlayAlwaysVisible = false;
        vm.ToggleOverlayAlwaysVisibleCommand.Execute(null);
        vm.OverlayAlwaysVisible.Should().BeFalse();

        vm.OverlayAlwaysVisible = true;
        vm.ToggleOverlayAlwaysVisibleCommand.Execute(null);
        vm.OverlayAlwaysVisible.Should().BeTrue();
    }

    [Fact]
    public void ToggleShowInTaskbar_BindingFlipsAndCommandRetains()
    {
        var vm = CreateViewModel(o => o.Overlay.ShowInTaskbar = false);

        vm.ShowInTaskbar = true;
        vm.ToggleShowInTaskbarCommand.Execute(null);
        vm.ShowInTaskbar.Should().BeTrue();

        vm.ShowInTaskbar = false;
        vm.ToggleShowInTaskbarCommand.Execute(null);
        vm.ShowInTaskbar.Should().BeFalse();
    }

    [Fact]
    public void ToggleSoundEffects_BindingFlipsAndCommandRetains()
    {
        var vm = CreateViewModel(o => o.App.SoundEffects = true);

        vm.SoundEffectsEnabled = false;
        vm.ToggleSoundEffectsCommand.Execute(null);
        vm.SoundEffectsEnabled.Should().BeFalse();

        vm.SoundEffectsEnabled = true;
        vm.ToggleSoundEffectsCommand.Execute(null);
        vm.SoundEffectsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ToggleMuteWhileDictating_BindingFlipsAndCommandRetains()
    {
        var vm = CreateViewModel(o => o.Audio.MuteWhileDictating = true);

        vm.MuteWhileDictating = false;
        vm.ToggleMuteWhileDictatingCommand.Execute(null);
        vm.MuteWhileDictating.Should().BeFalse();

        vm.MuteWhileDictating = true;
        vm.ToggleMuteWhileDictatingCommand.Execute(null);
        vm.MuteWhileDictating.Should().BeTrue();
    }

    // --- System: Transcription Settings ---

    [Fact]
    public void SelectCorrectionProvider_ChangesProvider()
    {
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Off);

        vm.SelectCorrectionProviderCommand.Execute("Cloud");
        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Cloud);

        vm.SelectCorrectionProviderCommand.Execute("Off");
        vm.CorrectionProvider.Should().Be(TextCorrectionProvider.Off);
    }

    [Fact]
    public void ApplyAutoDismiss_UpdatesValue()
    {
        var vm = CreateViewModel();
        vm.StartEditingAutoDismissCommand.Execute(null);

        vm.ApplyAutoDismiss(20);

        vm.AutoDismissSeconds.Should().Be(20);
        vm.IsEditingAutoDismiss.Should().BeFalse();
    }

    [Fact]
    public void ApplyMaxRecording_UpdatesValue()
    {
        var vm = CreateViewModel();
        vm.StartEditingMaxRecordingCommand.Execute(null);

        vm.ApplyMaxRecording(600);

        vm.MaxRecordingSeconds.Should().Be(600);
        vm.IsEditingMaxRecording.Should().BeFalse();
    }

    // --- Transcription Settings ---

    [Fact]
    public void ApplyProvider_SwitchesToLocal()
    {
        var vm = CreateViewModel();

        vm.ApplyProvider(TranscriptionProvider.Local);

        vm.Provider.Should().Be(TranscriptionProvider.Local);
        vm.IsEditingProvider.Should().BeFalse();
    }

    [Fact]
    public void ApplyApiKey_UpdatesDisplayAndClearsFlag()
    {
        var vm = CreateViewModel();
        vm.StartEditingApiKeyCommand.Execute(null);

        vm.ApplyApiKey("sk-new-key-abcd");

        vm.OpenAiApiKey.Should().Be("sk-new-key-abcd");
        vm.OpenAiApiKeyDisplay.Should().EndWith("abcd");
        vm.IsEditingApiKey.Should().BeFalse();
    }

    [Fact]
    public void ApplyModel_UpdatesValue()
    {
        var vm = CreateViewModel();
        vm.StartEditingModelCommand.Execute(null);

        vm.ApplyModel("gpt-4o-mini-transcribe");

        vm.TranscriptionModel.Should().Be("gpt-4o-mini-transcribe");
        vm.IsEditingModel.Should().BeFalse();
    }

    [Fact]
    public void ToggleGpuAcceleration_FlipsValue()
    {
        var vm = CreateViewModel(o => o.Local.GpuAcceleration = true);
        vm.GpuAcceleration.Should().BeTrue();

        vm.ToggleGpuAccelerationCommand.Execute(null);
        vm.GpuAcceleration.Should().BeFalse();
    }

    // --- Audio Compression ---

    [Fact]
    public void Constructor_LoadsAudioCompressionEnabled()
    {
        var vm = CreateViewModel(o => o.Audio.CompressBeforeUpload = true);
        vm.AudioCompressionEnabled.Should().BeTrue();
    }

    [Fact]
    public void ToggleAudioCompression_BindingFlipsAndCommandRetains()
    {
        var vm = CreateViewModel(o => o.Audio.CompressBeforeUpload = true);

        vm.AudioCompressionEnabled = false;
        vm.ToggleAudioCompressionCommand.Execute(null);
        vm.AudioCompressionEnabled.Should().BeFalse();

        vm.AudioCompressionEnabled = true;
        vm.ToggleAudioCompressionCommand.Execute(null);
        vm.AudioCompressionEnabled.Should().BeTrue();
    }

    // --- Combined Audio Model ---

    [Fact]
    public void Constructor_LoadsUseCombinedAudioModel()
    {
        var vm = CreateViewModel(o => o.TextCorrection.UseCombinedAudioModel = true);
        vm.UseCombinedAudioModel.Should().BeTrue();
    }

    [Fact]
    public void ToggleCombinedAudioModel_BindingFlipsAndCommandRetains()
    {
        var vm = CreateViewModel(o => o.TextCorrection.UseCombinedAudioModel = false);

        vm.UseCombinedAudioModel = true;
        vm.ToggleCombinedAudioModelCommand.Execute(null);
        vm.UseCombinedAudioModel.Should().BeTrue();

        vm.UseCombinedAudioModel = false;
        vm.ToggleCombinedAudioModelCommand.Execute(null);
        vm.UseCombinedAudioModel.Should().BeFalse();
    }

    [Fact]
    public void Constructor_LoadsCombinedAudioModel()
    {
        var vm = CreateViewModel(o => o.TextCorrection.CombinedAudioModel = "gpt-4o-audio-preview");
        vm.CombinedAudioModel.Should().Be("gpt-4o-audio-preview");
    }

    [Fact]
    public void Constructor_LoadsDefaultCombinedAudioModel()
    {
        var vm = CreateViewModel();
        vm.CombinedAudioModel.Should().Be("gpt-4o-mini-audio-preview");
    }

    [Fact]
    public void ApplyCombinedAudioModel_UpdatesValue()
    {
        var vm = CreateViewModel();
        vm.StartEditingCombinedAudioModelCommand.Execute(null);
        vm.IsEditingCombinedAudioModel.Should().BeTrue();

        vm.ApplyCombinedAudioModel("gpt-4o-audio-preview");

        vm.CombinedAudioModel.Should().Be("gpt-4o-audio-preview");
        vm.IsEditingCombinedAudioModel.Should().BeFalse();
    }

    // --- Version ---

    [Fact]
    public void VersionText_ContainsWhisperShow()
    {
        var vm = CreateViewModel();
        vm.VersionText.Should().StartWith("WhisperShow v");
    }

    // --- Model Preloading ---

    [Fact]
    public void ApplyProvider_SwitchingToLocal_TriggersPreload()
    {
        var vm = CreateViewModel(o => o.Local.ModelName = "ggml-small.bin");

        vm.ApplyProvider(TranscriptionProvider.Local);

        _preloadService.Received(1).PreloadTranscriptionModel("ggml-small.bin");
    }

    [Fact]
    public void ApplyProvider_SwitchingToOpenAI_DoesNotTriggerPreload()
    {
        var vm = CreateViewModel(o => o.Provider = TranscriptionProvider.Local);

        vm.ApplyProvider(TranscriptionProvider.OpenAI);

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

        vm.SelectCorrectionProviderCommand.Execute("Local");

        _preloadService.Received(1).PreloadCorrectionModel("gemma-2b.gguf");
    }

    [Fact]
    public void SelectCorrectionProvider_SwitchingToCloud_DoesNotTriggerPreload()
    {
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Off);

        vm.SelectCorrectionProviderCommand.Execute("Cloud");

        _preloadService.DidNotReceive().PreloadCorrectionModel(Arg.Any<string?>());
    }

    [Fact]
    public void ActivateModel_AlwaysTriggersPreload()
    {
        var vm = CreateViewModel(o => o.Provider = TranscriptionProvider.OpenAI);
        var model = new WhisperModel { Name = "Base", FileName = "ggml-base.bin", SizeBytes = 0 };
        var item = new ModelItemViewModel(model, Whisper.net.Ggml.GgmlType.Base);
        item.IsDownloaded = true;

        vm.ActivateModelCommand.Execute(item);

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

        vm.ActivateCorrectionModelCommand.Execute(item);

        _preloadService.Received(1).PreloadCorrectionModel("gemma-2b.gguf");
    }
}
