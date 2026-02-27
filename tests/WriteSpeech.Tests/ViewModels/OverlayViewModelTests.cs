using System.IO;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WriteSpeech.App.ViewModels;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.Audio;
using WriteSpeech.Core.Services.Configuration;
using WriteSpeech.Core.Services.History;
using WriteSpeech.Core.Services.Snippets;
using WriteSpeech.Core.Services.Statistics;
using WriteSpeech.Core.Services.IDE;
using WriteSpeech.Core.Services.Modes;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Core.Services.TextInsertion;
using WriteSpeech.Core.Services.Transcription;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.ViewModels;

public class OverlayViewModelTests : IDisposable
{
    private readonly IAudioRecordingService _audioService;
    private readonly IAudioMutingService _mutingService;
    private readonly ITextInsertionService _textInsertionService;
    private readonly ITextCorrectionService _textCorrectionService;
    private readonly ICombinedTranscriptionCorrectionService _combinedService;
    private readonly ISnippetService _snippetService;
    private readonly ITranscriptionService _transcriptionProvider;
    private readonly WriteSpeechOptions _optionsValue;

    public OverlayViewModelTests()
    {
        WpfTestHelper.EnsureApplication();

        _audioService = Substitute.For<IAudioRecordingService>();
        _mutingService = Substitute.For<IAudioMutingService>();
        _textInsertionService = Substitute.For<ITextInsertionService>();
        _textCorrectionService = Substitute.For<ITextCorrectionService>();
        _combinedService = Substitute.For<ICombinedTranscriptionCorrectionService>();
        _snippetService = Substitute.For<ISnippetService>();
        _snippetService.ApplySnippets(Arg.Any<string>()).Returns(x => x.Arg<string>());

        _transcriptionProvider = Substitute.For<ITranscriptionService>();
        _transcriptionProvider.ProviderName.Returns("Test Provider");
        _transcriptionProvider.IsAvailable.Returns(true);
        _transcriptionProvider.IsModelLoaded.Returns(true);
        _textCorrectionService.IsModelLoaded.Returns(true);
        _combinedService.IsModelLoaded.Returns(true);

        _optionsValue = new WriteSpeechOptions();
    }

    private OverlayViewModel CreateViewModel(Action<WriteSpeechOptions>? configure = null)
    {
        configure?.Invoke(_optionsValue);

        var providerFactory = new TestProviderFactory(_transcriptionProvider);
        var correctionFactory = new TestCorrectionProviderFactory(_textCorrectionService);

        var optionsMonitor = OptionsHelper.CreateMonitor(o =>
        {
            o.Provider = _optionsValue.Provider;
            o.Language = _optionsValue.Language;
            o.TextCorrection = _optionsValue.TextCorrection;
            o.OpenAI = _optionsValue.OpenAI;
            o.Hotkey = _optionsValue.Hotkey;
            o.Audio = _optionsValue.Audio;
            o.Overlay = _optionsValue.Overlay;
        });

        return new OverlayViewModel(
            _audioService,
            _mutingService,
            providerFactory,
            _textInsertionService,
            correctionFactory,
            _combinedService,
            _snippetService,
            Substitute.For<ISoundEffectService>(),
            Substitute.For<IUsageStatsService>(),
            Substitute.For<ITranscriptionHistoryService>(),
            Substitute.For<IWindowFocusService>(),
            Substitute.For<IIDEDetectionService>(),
            Substitute.For<IIDEContextService>(),
            Substitute.For<IModeService>(),
            Substitute.For<ISelectedTextService>(),
            new SynchronousDispatcherService(),
            Substitute.For<ISettingsPersistenceService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OverlayViewModel>.Instance,
            optionsMonitor);
    }

    // --- State Machine Tests ---

    [Fact]
    public void InitialState_IsIdle()
    {
        var vm = CreateViewModel();
        vm.State.Should().Be(RecordingState.Idle);
    }

    [Fact]
    public async Task ToggleRecording_FromIdle_StartsRecording()
    {
        var vm = CreateViewModel();

        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.State.Should().Be(RecordingState.Recording);
        await _audioService.Received(1).StartRecordingAsync();
    }

    [Fact]
    public async Task ToggleRecording_FromIdle_MutesOtherApps()
    {
        var vm = CreateViewModel();

        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        _mutingService.Received(1).MuteOtherApplications();
    }

    [Fact]
    public async Task ToggleRecording_FromRecording_StopsAndTranscribes()
    {
        var audioData = new byte[2000];
        _audioService.StopRecordingAsync().Returns(audioData);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello world" });

        var vm = CreateViewModel();
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // Idle → Recording

        await vm.ToggleRecordingCommand.ExecuteAsync(null); // Recording → Transcribing → auto-insert

        await _audioService.Received(1).StopRecordingAsync();
    }

    [Fact]
    public async Task ToggleRecording_FromRecording_UnmutesApps()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello" });

        var vm = CreateViewModel();
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Recording

        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Stop

        _mutingService.Received(1).UnmuteAll();
    }

    [Fact]
    public async Task ToggleRecording_FromError_DismissesResult()
    {
        _audioService.StartRecordingAsync().ThrowsAsync(new Exception("mic error"));

        var vm = CreateViewModel();
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Error
        vm.State.Should().Be(RecordingState.Error);

        await vm.ToggleRecordingCommand.ExecuteAsync(null); // Error → Idle
        vm.State.Should().Be(RecordingState.Idle);
    }

    // --- Transcription Validation ---

    [Fact]
    public async Task StopRecording_AudioTooShort_SetsErrorState()
    {
        _audioService.StopRecordingAsync().Returns(new byte[500]); // < 1000

        var vm = CreateViewModel();
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Recording
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Stop

        vm.State.Should().Be(RecordingState.Error);
        vm.ErrorMessage.Should().Contain("too short");
    }

    [Fact]
    public async Task StopRecording_EmptyTranscription_SetsErrorState()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "" });

        var vm = CreateViewModel();
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.State.Should().Be(RecordingState.Error);
        vm.ErrorMessage.Should().Contain("No speech detected");
    }

    [Fact]
    public async Task StopRecording_TranscriptionException_SetsErrorState()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("API error"));

        var vm = CreateViewModel();
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.State.Should().Be(RecordingState.Error);
        vm.ErrorMessage.Should().Contain("API error");
    }

    // --- Text Correction ---

    [Fact]
    public async Task StopRecording_CorrectionEnabled_CallsCorrectAsync()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "raw text" });
        _textCorrectionService.CorrectAsync("raw text", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("Corrected text.");

        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Cloud);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        await _textCorrectionService.Received(1).CorrectAsync("raw text", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopRecording_CorrectionDisabled_SkipsCorrectAsync()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "raw text" });

        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Off);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        await _textCorrectionService.DidNotReceive().CorrectAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // --- Waveform Buffer ---

    [Fact]
    public void GetWaveformLevels_ReturnsBuffer()
    {
        var vm = CreateViewModel();
        var levels = vm.GetWaveformLevels();
        levels.Should().HaveCount(20);
        levels.Should().AllSatisfy(l => l.Should().Be(0f));
    }

    [Fact]
    public void ClearWaveform_ResetsAllToZero()
    {
        var vm = CreateViewModel();
        // Simulate some audio levels via the property
        vm.AudioLevel = 0.5f;
        vm.AudioLevel = 0.8f;

        vm.ClearWaveform();

        vm.GetWaveformLevels().Should().AllSatisfy(l => l.Should().Be(0f));
    }

    [Fact]
    public void OnAudioLevelChanged_FiresWaveformUpdated()
    {
        var vm = CreateViewModel();
        bool fired = false;
        vm.WaveformUpdated += (_, _) => fired = true;

        vm.AudioLevel = 0.5f;

        fired.Should().BeTrue();
    }

    [Fact]
    public void OnAudioLevelChanged_ShiftsBuffer()
    {
        var vm = CreateViewModel();

        vm.AudioLevel = 0.1f;
        vm.AudioLevel = 0.2f;
        vm.AudioLevel = 0.3f;

        var levels = vm.GetWaveformLevels();
        // Last 3 values should be our inputs
        levels[19].Should().Be(0.3f);
        levels[18].Should().Be(0.2f);
        levels[17].Should().Be(0.1f);
        // Earlier values should still be 0
        levels[0].Should().Be(0f);
    }

    [Fact]
    public void GetWaveformLevels_ReturnsCopy_NotReference()
    {
        var vm = CreateViewModel();
        vm.AudioLevel = 0.5f;

        var first = vm.GetWaveformLevels();
        var second = vm.GetWaveformLevels();

        first.Should().NotBeSameAs(second);
        first.Should().BeEquivalentTo(second);
    }

    [Fact]
    public void GetWaveformLevels_ModifyingCopy_DoesNotAffectOriginal()
    {
        var vm = CreateViewModel();
        vm.AudioLevel = 0.5f;

        var copy = vm.GetWaveformLevels();
        copy[19] = 999f;

        vm.GetWaveformLevels()[19].Should().Be(0.5f);
    }

    // --- CancelAutoDismissTimer CTS Dispose ---

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var vm = CreateViewModel();

        var act = () =>
        {
            vm.Dispose();
            vm.Dispose();
        };

        act.Should().NotThrow();
    }

    // --- ShowResultOverlay ---

    [Fact]
    public async Task StopRecording_ShowResultOverlayDisabled_GoesDirectlyToIdle()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello world" });

        var vm = CreateViewModel(o => o.Overlay.ShowResultOverlay = false);
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Recording
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Transcribing → Idle (skip Result)

        vm.State.Should().Be(RecordingState.Idle);
        await _textInsertionService.Received(1).InsertTextAsync("hello world");
    }

    [Fact]
    public async Task StopRecording_ShowResultOverlayEnabled_ShowsResult()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello world" });

        var vm = CreateViewModel(o => o.Overlay.ShowResultOverlay = true);
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Recording
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Transcribing → Result

        vm.State.Should().Be(RecordingState.Result);
        vm.TranscribedText.Should().Be("hello world");
    }

    // --- Dismiss ---

    [Fact]
    public async Task DismissResult_ResetsState()
    {
        _audioService.StartRecordingAsync().ThrowsAsync(new Exception("error"));

        var vm = CreateViewModel();
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Error
        vm.State.Should().Be(RecordingState.Error);

        vm.DismissResultCommand.Execute(null);

        vm.State.Should().Be(RecordingState.Idle);
        vm.TranscribedText.Should().BeNull();
        vm.ErrorMessage.Should().BeNull();
    }

    // --- Auto-Dismiss ---

    [Fact]
    public async Task Error_AutoDismissesAfterTimeout()
    {
        _audioService.StopRecordingAsync().Returns(new byte[500]); // too short → Error

        var vm = CreateViewModel(o => o.Overlay = new OverlayOptions { AutoDismissSeconds = 1 });
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Recording
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Error

        vm.State.Should().Be(RecordingState.Error);

        await Task.Delay(1500);

        vm.State.Should().Be(RecordingState.Idle);
    }

    // --- Push-to-Talk ---

    [Fact]
    public async Task HotkeyStart_FromIdle_StartsRecording()
    {
        var vm = CreateViewModel();

        await vm.HotkeyStartRecordingAsync();

        vm.State.Should().Be(RecordingState.Recording);
        await _audioService.Received(1).StartRecordingAsync();
    }

    [Fact]
    public async Task HotkeyStop_FromRecording_StopsAndTranscribes()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello" });

        var vm = CreateViewModel();
        await vm.HotkeyStartRecordingAsync(); // → Recording

        await vm.HotkeyStopRecordingAsync(); // → Transcribing → auto-insert

        await _audioService.Received(1).StopRecordingAsync();
    }

    [Fact]
    public async Task HotkeyStart_NotIdle_DoesNothing()
    {
        var vm = CreateViewModel();
        await vm.HotkeyStartRecordingAsync(); // → Recording

        await vm.HotkeyStartRecordingAsync(); // should be ignored

        await _audioService.Received(1).StartRecordingAsync(); // still only once
    }

    [Fact]
    public async Task HotkeyStop_NotRecording_DoesNothing()
    {
        var vm = CreateViewModel();

        await vm.HotkeyStopRecordingAsync(); // Idle → should be ignored

        vm.State.Should().Be(RecordingState.Idle);
        await _audioService.DidNotReceive().StopRecordingAsync();
    }

    // --- Combined Audio Model ---

    [Fact]
    public async Task StopRecording_CombinedModelEnabled_UsesCombinedService()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _combinedService.IsAvailable.Returns(true);
        _combinedService.TranscribeAndCorrectAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("corrected text");

        var vm = CreateViewModel(o =>
        {
            o.TextCorrection.Provider = TextCorrectionProvider.Cloud;
            o.TextCorrection.UseCombinedAudioModel = true;
        });
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Recording
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Stop

        await _combinedService.Received(1).TranscribeAndCorrectAsync(
            Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        // Standard pipeline should NOT be used
        await _transcriptionProvider.DidNotReceive().TranscribeAsync(
            Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopRecording_CombinedModelDisabled_UsesStandardPipeline()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "standard text" });

        var vm = CreateViewModel(o =>
        {
            o.TextCorrection.Provider = TextCorrectionProvider.Cloud;
            o.TextCorrection.UseCombinedAudioModel = false;
        });
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        await _transcriptionProvider.Received(1).TranscribeAsync(
            Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _combinedService.DidNotReceive().TranscribeAndCorrectAsync(
            Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopRecording_CombinedModelFails_FallsBackToStandardPipeline()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _combinedService.IsAvailable.Returns(true);
        _combinedService.TranscribeAndCorrectAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("combined model error"));
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "fallback text" });

        var vm = CreateViewModel(o =>
        {
            o.TextCorrection.Provider = TextCorrectionProvider.Cloud;
            o.TextCorrection.UseCombinedAudioModel = true;
        });
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        // Combined was tried first
        await _combinedService.Received(1).TranscribeAndCorrectAsync(
            Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        // Then fell back to standard pipeline
        await _transcriptionProvider.Received(1).TranscribeAsync(
            Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // --- Conditional Muting ---

    [Fact]
    public async Task StartRecording_MuteDisabled_DoesNotMute()
    {
        var vm = CreateViewModel(o => o.Audio.MuteWhileDictating = false);

        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        _mutingService.DidNotReceive().MuteOtherApplications();
    }

    [Fact]
    public async Task StopRecording_MuteDisabled_DoesNotUnmute()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello" });

        var vm = CreateViewModel(o => o.Audio.MuteWhileDictating = false);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        _mutingService.DidNotReceive().UnmuteAll();
    }

    // --- AlwaysVisible ---

    [Fact]
    public void Constructor_SetsAlwaysVisibleFromOptions()
    {
        var vm = CreateViewModel(o => o.Overlay.AlwaysVisible = false);
        vm.IsOverlayAlwaysVisible.Should().BeFalse();
    }

    [Fact]
    public void Constructor_DefaultAlwaysVisible_IsTrue()
    {
        var vm = CreateViewModel();
        vm.IsOverlayAlwaysVisible.Should().BeTrue();
    }

    // --- Hotkey Display ---

    [Fact]
    public void PushToTalkHotkeyText_FormatsCorrectly()
    {
        var vm = CreateViewModel();
        vm.PushToTalkHotkeyText.Should().Be("Click or hold \"Ctrl + Space\" to start dictating");
    }

    [Fact]
    public void PushToTalkHotkeyText_CustomHotkey_FormatsCorrectly()
    {
        var vm = CreateViewModel(o =>
        {
            o.Hotkey.PushToTalk.Modifiers = "Control, Alt";
            o.Hotkey.PushToTalk.Key = "F1";
        });
        vm.PushToTalkHotkeyText.Should().Be("Click or hold \"Ctrl + Alt + F1\" to start dictating");
    }

    [Fact]
    public void PushToTalkHotkeyText_MouseButton_FormatsCorrectly()
    {
        var vm = CreateViewModel(o =>
        {
            o.Hotkey.PushToTalk.Modifiers = "Control";
            o.Hotkey.PushToTalk.Key = "";
            o.Hotkey.PushToTalk.MouseButton = "XButton1";
        });
        vm.PushToTalkHotkeyText.Should().Be("Click or hold \"Ctrl + Mouse 4\" to start dictating");
    }

    [Fact]
    public void PushToTalkHotkeyText_MiddleClick_NoModifiers_FormatsCorrectly()
    {
        var vm = CreateViewModel(o =>
        {
            o.Hotkey.PushToTalk.Modifiers = "";
            o.Hotkey.PushToTalk.Key = "";
            o.Hotkey.PushToTalk.MouseButton = "Middle";
        });
        vm.PushToTalkHotkeyText.Should().Be("Click or hold \"Middle Click\" to start dictating");
    }

    // --- Provider Name ---

    [Fact]
    public void UpdateProviderName_SetsCurrentProviderName()
    {
        var vm = CreateViewModel();
        vm.CurrentProviderName.Should().Be("Test Provider");
    }

    // --- Status Text ---

    [Fact]
    public void InitialStatusText_IsEmpty()
    {
        var vm = CreateViewModel();
        vm.StatusText.Should().BeEmpty();
    }

    [Fact]
    public async Task StandardPipeline_ModelLoaded_ShowsTranscribing()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello" });

        var statusTexts = new List<string>();
        var vm = CreateViewModel();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverlayViewModel.StatusText))
                statusTexts.Add(vm.StatusText);
        };

        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Recording
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Transcribing → Result

        statusTexts.Should().Contain("Transcribing...");
    }

    [Fact]
    public async Task StandardPipeline_ModelNotLoaded_ShowsLoadingStatus()
    {
        _transcriptionProvider.IsModelLoaded.Returns(false);
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello" });

        var statusTexts = new List<string>();
        var vm = CreateViewModel();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverlayViewModel.StatusText))
                statusTexts.Add(vm.StatusText);
        };

        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        statusTexts.Should().Contain("Loading transcription model...");
    }

    [Fact]
    public async Task StandardPipeline_WithCorrection_ShowsCorrecting()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "raw text" });
        _textCorrectionService.CorrectAsync("raw text", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("Corrected text.");

        var statusTexts = new List<string>();
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Cloud);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverlayViewModel.StatusText))
                statusTexts.Add(vm.StatusText);
        };

        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        statusTexts.Should().Contain("Transcribing...");
        statusTexts.Should().Contain("Correcting text...");
    }

    [Fact]
    public async Task CombinedModel_ShowsTranscribingAndCorrecting()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _combinedService.IsAvailable.Returns(true);
        _combinedService.TranscribeAndCorrectAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("corrected text");

        var statusTexts = new List<string>();
        var vm = CreateViewModel(o =>
        {
            o.TextCorrection.Provider = TextCorrectionProvider.Cloud;
            o.TextCorrection.UseCombinedAudioModel = true;
        });
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverlayViewModel.StatusText))
                statusTexts.Add(vm.StatusText);
        };

        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        statusTexts.Should().Contain("Transcribing & correcting...");
    }

    [Fact]
    public async Task StatusText_ClearedAfterTranscription()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello" });

        var vm = CreateViewModel();
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.State.Should().Be(RecordingState.Result);
        vm.StatusText.Should().BeEmpty();
    }

    // --- Dispose ---

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var vm = CreateViewModel();
        var act = () => vm.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_CancelsAutoDismissTimer()
    {
        _audioService.StopRecordingAsync().Returns(new byte[500]); // too short → Error

        var vm = CreateViewModel(o => o.Overlay = new OverlayOptions { AutoDismissSeconds = 60 });
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Recording
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Error (starts auto-dismiss)

        vm.Dispose();

        // After dispose, the auto-dismiss should be cancelled — state stays Error
        await Task.Delay(200);
        vm.State.Should().Be(RecordingState.Error);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var vm = CreateViewModel();
        vm.Dispose();
        var act = () => vm.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_UnsubscribesAudioLevelChanged()
    {
        var vm = CreateViewModel();
        vm.Dispose();

        // Fire the event after disposal — AudioLevel should NOT update
        _audioService.AudioLevelChanged += Raise.Event<EventHandler<float>>(_audioService, 0.5f);
        vm.AudioLevel.Should().Be(0f);
    }

    // --- Mode Resolution Tests ---

    [Fact]
    public async Task StopRecording_WithActiveMode_PassesModePromptToCorrector()
    {
        var modeService = Substitute.For<IModeService>();
        modeService.ResolveSystemPrompt(Arg.Any<string?>()).Returns("Use formal email tone.");
        modeService.ResolveTargetLanguage(Arg.Any<string?>()).Returns((string?)null);

        _textCorrectionService.CorrectAsync(Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(x => x.ArgAt<string>(0));

        var vm = CreateViewModelWithModeService(modeService, o =>
            o.TextCorrection.Provider = TextCorrectionProvider.OpenAI);

        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello" });

        await StartAndStopRecording(vm);

        await _textCorrectionService.Received().CorrectAsync(
            Arg.Any<string>(), Arg.Any<string?>(),
            "Use formal email tone.", Arg.Is<string?>(x => x == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopRecording_WithTranslateMode_PassesTargetLanguage()
    {
        var modeService = Substitute.For<IModeService>();
        modeService.ResolveSystemPrompt(Arg.Any<string?>()).Returns("Translate prompt");
        modeService.ResolveTargetLanguage(Arg.Any<string?>()).Returns("English");

        _textCorrectionService.CorrectAsync(Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(x => x.ArgAt<string>(0));

        var vm = CreateViewModelWithModeService(modeService, o =>
            o.TextCorrection.Provider = TextCorrectionProvider.OpenAI);

        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hallo welt" });

        await StartAndStopRecording(vm);

        await _textCorrectionService.Received().CorrectAsync(
            Arg.Any<string>(), Arg.Any<string?>(),
            "Translate prompt", "English", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopRecording_WithDefaultMode_PassesNullSystemPrompt()
    {
        var modeService = Substitute.For<IModeService>();
        modeService.ResolveSystemPrompt(Arg.Any<string?>()).Returns((string?)null);
        modeService.ResolveTargetLanguage(Arg.Any<string?>()).Returns((string?)null);

        _textCorrectionService.CorrectAsync(Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(x => x.ArgAt<string>(0));

        var vm = CreateViewModelWithModeService(modeService, o =>
            o.TextCorrection.Provider = TextCorrectionProvider.OpenAI);

        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello" });

        await StartAndStopRecording(vm);

        await _textCorrectionService.Received().CorrectAsync(
            Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Is<string?>(x => x == null), Arg.Is<string?>(x => x == null), Arg.Any<CancellationToken>());
    }

    // --- IDE Context Tests ---

    [Fact]
    public async Task StopRecording_IDEDetected_PreparesContext()
    {
        var ideDetection = Substitute.For<IIDEDetectionService>();
        ideDetection.DetectIDE(Arg.Any<IntPtr>()).Returns(new IDEInfo("Code", "/workspace", null));
        var ideContext = Substitute.For<IIDEContextService>();

        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello" });

        var vm = CreateViewModelWithIDEServices(ideDetection, ideContext, o =>
        {
            o.Integration.VariableRecognition = true;
            o.Integration.FileTagging = true;
        });

        await StartAndStopRecording(vm);

        await ideContext.Received().PrepareContextAsync("/workspace", true, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopRecording_NoIDEDetected_ClearsContext()
    {
        var ideDetection = Substitute.For<IIDEDetectionService>();
        ideDetection.DetectIDE(Arg.Any<IntPtr>()).Returns((IDEInfo?)null);
        var ideContext = Substitute.For<IIDEContextService>();

        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello" });

        var vm = CreateViewModelWithIDEServices(ideDetection, ideContext, o =>
        {
            o.Integration.VariableRecognition = true;
        });

        await StartAndStopRecording(vm);

        ideContext.Received().Clear();
    }

    [Fact]
    public async Task StopRecording_IntegrationDisabled_SkipsContext()
    {
        var ideDetection = Substitute.For<IIDEDetectionService>();
        var ideContext = Substitute.For<IIDEContextService>();

        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "hello" });

        var vm = CreateViewModelWithIDEServices(ideDetection, ideContext, o =>
        {
            o.Integration.VariableRecognition = false;
            o.Integration.FileTagging = false;
        });

        await StartAndStopRecording(vm);

        ideDetection.DidNotReceive().DetectIDE(Arg.Any<IntPtr>());
    }

    // --- Event Handler Tests ---

    [Fact]
    public async Task OnRecordingError_DuringRecording_SetsErrorState()
    {
        var vm = CreateViewModel();
        await vm.HotkeyStartRecordingAsync();
        vm.State.Should().Be(RecordingState.Recording);

        _audioService.RecordingError += Raise.Event<EventHandler<Exception>>(
            _audioService, new IOException("Device lost"));

        vm.State.Should().Be(RecordingState.Error);
        vm.ErrorMessage.Should().Contain("Device lost");
    }

    [Fact]
    public async Task OnRecordingError_DuringRecording_UnmutesApps()
    {
        var vm = CreateViewModel(o => o.Audio.MuteWhileDictating = true);
        await vm.HotkeyStartRecordingAsync();

        _audioService.RecordingError += Raise.Event<EventHandler<Exception>>(
            _audioService, new IOException("Device lost"));

        _mutingService.Received().UnmuteAll();
    }

    [Fact]
    public void OnRecordingError_WhenIdle_IsIgnored()
    {
        var vm = CreateViewModel();
        vm.State.Should().Be(RecordingState.Idle);

        _audioService.RecordingError += Raise.Event<EventHandler<Exception>>(
            _audioService, new IOException("Device lost"));

        vm.State.Should().Be(RecordingState.Idle);
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task OnMaxDurationReached_DuringRecording_AutoStops()
    {
        var vm = CreateViewModel();
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "max duration test" });

        await vm.HotkeyStartRecordingAsync();
        vm.State.Should().Be(RecordingState.Recording);

        _audioService.MaxDurationReached += Raise.Event<EventHandler>(_audioService, EventArgs.Empty);
        // Allow async handler to complete
        await Task.Delay(100);

        vm.State.Should().NotBe(RecordingState.Recording);
    }

    [Fact]
    public void OnMaxDurationReached_WhenNotRecording_IsIgnored()
    {
        var vm = CreateViewModel();
        vm.State.Should().Be(RecordingState.Idle);

        _audioService.MaxDurationReached += Raise.Event<EventHandler>(_audioService, EventArgs.Empty);

        vm.State.Should().Be(RecordingState.Idle);
    }

    [Fact]
    public void UpdatePosition_PersistsViaPersistenceService()
    {
        var persistence = Substitute.For<ISettingsPersistenceService>();
        var vm = CreateViewModelWithPersistence(persistence);

        vm.UpdatePosition(100.5, 200.3);

        persistence.Received().ScheduleUpdate(Arg.Any<Action<System.Text.Json.Nodes.JsonNode>>());
        vm.PositionX.Should().Be(100.5);
        vm.PositionY.Should().Be(200.3);
    }

    [Fact]
    public async Task StopRecording_AppliesSnippetsAfterCorrection()
    {
        _snippetService.ApplySnippets("Hello world").Returns("Hello universe");
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "Hello world" });
        _audioService.StopRecordingAsync().Returns(new byte[2000]);

        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Off);
        await vm.HotkeyStartRecordingAsync();
        await vm.HotkeyStopRecordingAsync();

        vm.TranscribedText.Should().Be("Hello universe");
    }

    // --- Error Recovery Tests ---

    [Fact]
    public async Task OnRecordingError_ThenStartRecording_CanRecordAgain()
    {
        var vm = CreateViewModel();
        await vm.HotkeyStartRecordingAsync();
        vm.State.Should().Be(RecordingState.Recording);

        // Simulate device error during recording
        _audioService.RecordingError += Raise.Event<EventHandler<Exception>>(
            _audioService, new IOException("Device lost"));

        vm.State.Should().Be(RecordingState.Error);

        // Dismiss error → Idle
        vm.ToggleRecordingCommand.Execute(null);
        vm.State.Should().Be(RecordingState.Idle);

        // Start recording again — must not throw
        await vm.HotkeyStartRecordingAsync();
        vm.State.Should().Be(RecordingState.Recording);
    }

    [Fact]
    public async Task StopRecording_AudioTooShort_ThenStartRecording_CanRecordAgain()
    {
        _audioService.StopRecordingAsync().Returns(new byte[100]); // < 1000 bytes
        var vm = CreateViewModel();

        await vm.HotkeyStartRecordingAsync();
        vm.State.Should().Be(RecordingState.Recording);

        await vm.HotkeyStopRecordingAsync();
        vm.State.Should().Be(RecordingState.Error);
        vm.ErrorMessage.Should().Contain("too short");

        // Dismiss error → Idle
        vm.ToggleRecordingCommand.Execute(null);
        vm.State.Should().Be(RecordingState.Idle);

        // Start recording again — must not throw
        await vm.HotkeyStartRecordingAsync();
        vm.State.Should().Be(RecordingState.Recording);
    }

    // --- Helper methods for custom DI ---

    private OverlayViewModel CreateViewModelWithModeService(
        IModeService modeService, Action<WriteSpeechOptions>? configure = null)
    {
        configure?.Invoke(_optionsValue);
        _audioService.StopRecordingAsync().Returns(new byte[2000]);

        var optionsMonitor = OptionsHelper.CreateMonitor(o =>
        {
            o.Provider = _optionsValue.Provider;
            o.Language = _optionsValue.Language;
            o.TextCorrection = _optionsValue.TextCorrection;
            o.OpenAI = _optionsValue.OpenAI;
            o.Hotkey = _optionsValue.Hotkey;
            o.Audio = _optionsValue.Audio;
            o.Overlay = _optionsValue.Overlay;
        });

        return new OverlayViewModel(
            _audioService, _mutingService,
            new TestProviderFactory(_transcriptionProvider),
            _textInsertionService,
            new TestCorrectionProviderFactory(_textCorrectionService),
            _combinedService, _snippetService,
            Substitute.For<ISoundEffectService>(),
            Substitute.For<IUsageStatsService>(),
            Substitute.For<ITranscriptionHistoryService>(),
            Substitute.For<IWindowFocusService>(),
            Substitute.For<IIDEDetectionService>(),
            Substitute.For<IIDEContextService>(),
            modeService,
            Substitute.For<ISelectedTextService>(),
            new SynchronousDispatcherService(),
            Substitute.For<ISettingsPersistenceService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OverlayViewModel>.Instance,
            optionsMonitor);
    }

    private OverlayViewModel CreateViewModelWithIDEServices(
        IIDEDetectionService ideDetection, IIDEContextService ideContext,
        Action<WriteSpeechOptions>? configure = null)
    {
        configure?.Invoke(_optionsValue);
        _audioService.StopRecordingAsync().Returns(new byte[2000]);

        var optionsMonitor = OptionsHelper.CreateMonitor(o =>
        {
            o.Provider = _optionsValue.Provider;
            o.Language = _optionsValue.Language;
            o.TextCorrection = _optionsValue.TextCorrection;
            o.OpenAI = _optionsValue.OpenAI;
            o.Hotkey = _optionsValue.Hotkey;
            o.Audio = _optionsValue.Audio;
            o.Overlay = _optionsValue.Overlay;
            o.Integration = _optionsValue.Integration;
        });

        return new OverlayViewModel(
            _audioService, _mutingService,
            new TestProviderFactory(_transcriptionProvider),
            _textInsertionService,
            new TestCorrectionProviderFactory(_textCorrectionService),
            _combinedService, _snippetService,
            Substitute.For<ISoundEffectService>(),
            Substitute.For<IUsageStatsService>(),
            Substitute.For<ITranscriptionHistoryService>(),
            Substitute.For<IWindowFocusService>(),
            ideDetection, ideContext,
            Substitute.For<IModeService>(),
            Substitute.For<ISelectedTextService>(),
            new SynchronousDispatcherService(),
            Substitute.For<ISettingsPersistenceService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OverlayViewModel>.Instance,
            optionsMonitor);
    }

    private OverlayViewModel CreateViewModelWithPersistence(
        ISettingsPersistenceService persistence)
    {
        var optionsMonitor = OptionsHelper.CreateMonitor(o =>
        {
            o.Provider = _optionsValue.Provider;
            o.Hotkey = _optionsValue.Hotkey;
            o.Audio = _optionsValue.Audio;
            o.Overlay = _optionsValue.Overlay;
        });

        return new OverlayViewModel(
            _audioService, _mutingService,
            new TestProviderFactory(_transcriptionProvider),
            _textInsertionService,
            new TestCorrectionProviderFactory(_textCorrectionService),
            _combinedService, _snippetService,
            Substitute.For<ISoundEffectService>(),
            Substitute.For<IUsageStatsService>(),
            Substitute.For<ITranscriptionHistoryService>(),
            Substitute.For<IWindowFocusService>(),
            Substitute.For<IIDEDetectionService>(),
            Substitute.For<IIDEContextService>(),
            Substitute.For<IModeService>(),
            Substitute.For<ISelectedTextService>(),
            new SynchronousDispatcherService(),
            persistence,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OverlayViewModel>.Instance,
            optionsMonitor);
    }

    private async Task StartAndStopRecording(OverlayViewModel vm)
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        await vm.HotkeyStartRecordingAsync();
        await vm.HotkeyStopRecordingAsync();
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    /// <summary>
    /// Custom factory that wraps a mock provider, bypassing the OfType constraint.
    /// </summary>
    private class TestProviderFactory : TranscriptionProviderFactory
    {
        private readonly ITranscriptionService _provider;

        public TestProviderFactory(ITranscriptionService provider)
            : base([provider])
        {
            _provider = provider;
        }

        public override ITranscriptionService GetProvider(TranscriptionProvider provider) => _provider;
    }

    /// <summary>
    /// Custom correction factory that wraps a mock provider, bypassing the OfType constraint.
    /// </summary>
    private class TestCorrectionProviderFactory : TextCorrectionProviderFactory
    {
        private readonly ITextCorrectionService _provider;

        public TestCorrectionProviderFactory(ITextCorrectionService provider)
            : base([provider])
        {
            _provider = provider;
        }

        public override ITextCorrectionService? GetProvider(TextCorrectionProvider provider) =>
            provider == TextCorrectionProvider.Off ? null : _provider;
    }
}
