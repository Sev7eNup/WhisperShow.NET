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

public class OverlayViewModelVadTests : IDisposable
{
    private readonly IAudioRecordingService _audioService;
    private readonly IAudioMutingService _mutingService;
    private readonly ITranscriptionService _transcriptionProvider;
    private readonly ITextCorrectionService _textCorrectionService;
    private readonly ICombinedTranscriptionCorrectionService _combinedService;
    private readonly ISnippetService _snippetService;
    private readonly ISoundEffectService _soundEffects;
    private readonly IWindowFocusService _windowFocusService;
    private readonly WriteSpeechOptions _optionsValue;

    public OverlayViewModelVadTests()
    {
        WpfTestHelper.EnsureApplication();

        _audioService = Substitute.For<IAudioRecordingService>();
        _mutingService = Substitute.For<IAudioMutingService>();
        _textCorrectionService = Substitute.For<ITextCorrectionService>();
        _combinedService = Substitute.For<ICombinedTranscriptionCorrectionService>();
        _snippetService = Substitute.For<ISnippetService>();
        _snippetService.ApplySnippets(Arg.Any<string>()).Returns(x => x.Arg<string>());
        _soundEffects = Substitute.For<ISoundEffectService>();
        _windowFocusService = Substitute.For<IWindowFocusService>();

        _transcriptionProvider = Substitute.For<ITranscriptionService>();
        _transcriptionProvider.ProviderName.Returns("Test Provider");
        _transcriptionProvider.IsAvailable.Returns(true);
        _transcriptionProvider.IsModelLoaded.Returns(true);
        _textCorrectionService.IsModelLoaded.Returns(true);
        _combinedService.IsModelLoaded.Returns(true);

        _optionsValue = new WriteSpeechOptions();
        // Set MinRecordingSeconds to 0 so silence detection fires immediately in tests
        _optionsValue.Audio.VoiceActivity.MinRecordingSeconds = 0;
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

        return OverlayViewModel.CreateForTests(
            _audioService,
            _mutingService,
            providerFactory,
            Substitute.For<ITextInsertionService>(),
            correctionFactory,
            _combinedService,
            _snippetService,
            _soundEffects,
            Substitute.For<IUsageStatsService>(),
            Substitute.For<ITranscriptionHistoryService>(),
            _windowFocusService,
            Substitute.For<IIDEDetectionService>(),
            Substitute.For<IIDEContextService>(),
            Substitute.For<IModeService>(),
            Substitute.For<ISelectedTextService>(),
            new SynchronousDispatcherService(),
            Substitute.For<ISettingsPersistenceService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OverlayViewModel>.Instance,
            optionsMonitor);
    }

    // --- Toggle with VAD enabled ---

    [Fact]
    public async Task Toggle_WithVadEnabled_FromIdle_StartsListening()
    {
        var vm = CreateViewModel(o => o.Audio.VoiceActivity.Enabled = true);

        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.State.Should().Be(RecordingState.Listening);
        await _audioService.Received(1).StartListeningAsync();
    }

    [Fact]
    public async Task Toggle_WithVadDisabled_FromIdle_StartsRecording()
    {
        var vm = CreateViewModel(o => o.Audio.VoiceActivity.Enabled = false);

        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.State.Should().Be(RecordingState.Recording);
        await _audioService.Received(1).StartRecordingAsync();
    }

    [Fact]
    public async Task Toggle_FromListening_StopsListeningAndGoesIdle()
    {
        var vm = CreateViewModel(o => o.Audio.VoiceActivity.Enabled = true);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        vm.State.Should().Be(RecordingState.Listening);

        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.State.Should().Be(RecordingState.Idle);
        _audioService.Received(1).StopListening();
    }

    [Fact]
    public async Task Toggle_WithVadEnabled_MutesApps()
    {
        var vm = CreateViewModel(o =>
        {
            o.Audio.VoiceActivity.Enabled = true;
            o.Audio.MuteWhileDictating = true;
        });

        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        _mutingService.Received(1).MuteOtherApplications();
    }

    [Fact]
    public async Task Toggle_StopListening_UnmutesApps()
    {
        var vm = CreateViewModel(o =>
        {
            o.Audio.VoiceActivity.Enabled = true;
            o.Audio.MuteWhileDictating = true;
        });
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        _mutingService.Received(1).UnmuteAll();
    }

    // --- SpeechStarted event ---

    [Fact]
    public async Task SpeechStarted_FromListening_TransitionsToRecording()
    {
        var vm = CreateViewModel(o => o.Audio.VoiceActivity.Enabled = true);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        vm.State.Should().Be(RecordingState.Listening);

        _audioService.SpeechStarted += Raise.Event();

        vm.State.Should().Be(RecordingState.Recording);
    }

    [Fact]
    public async Task SpeechStarted_FromListening_PlaysStartSound()
    {
        var vm = CreateViewModel(o => o.Audio.VoiceActivity.Enabled = true);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        _audioService.SpeechStarted += Raise.Event();

        _soundEffects.Received().PlayStartRecording();
    }

    [Fact]
    public void SpeechStarted_WhenNotListening_IsIgnored()
    {
        var vm = CreateViewModel();
        vm.State.Should().Be(RecordingState.Idle);

        _audioService.SpeechStarted += Raise.Event();

        vm.State.Should().Be(RecordingState.Idle);
    }

    // --- SilenceDetected event ---

    [Fact]
    public async Task SilenceDetected_WhenRecording_StopsAndTranscribes()
    {
        var vm = CreateViewModel(o => o.Audio.VoiceActivity.Enabled = true);

        // Setup transcription to return a valid result
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "Hello world" });

        // Enter listening, then recording
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        _audioService.SpeechStarted += Raise.Event();
        vm.State.Should().Be(RecordingState.Recording);

        // Simulate silence
        _audioService.SilenceDetected += Raise.Event();

        // Should have called StopRecordingAsync (transcription pipeline)
        await _audioService.Received(1).StopRecordingAsync();
    }

    [Fact]
    public void SilenceDetected_WhenNotRecording_IsIgnored()
    {
        var vm = CreateViewModel();
        vm.State.Should().Be(RecordingState.Idle);

        _audioService.SilenceDetected += Raise.Event();

        vm.State.Should().Be(RecordingState.Idle);
    }

    // --- VAD listening loop ---

    [Fact]
    public async Task VadLoop_AfterSuccessfulTranscription_RestartsListening()
    {
        var vm = CreateViewModel(o => o.Audio.VoiceActivity.Enabled = true);

        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "Hello world" });

        // Start listening
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        vm.State.Should().Be(RecordingState.Listening);

        // VAD detects speech → recording
        _audioService.SpeechStarted += Raise.Event();
        vm.State.Should().Be(RecordingState.Recording);

        // VAD detects silence → transcribe → back to listening
        _audioService.SilenceDetected += Raise.Event();

        // Should restart listening (the loop)
        await _audioService.Received(2).StartListeningAsync();
        vm.State.Should().Be(RecordingState.Listening);
    }

    [Fact]
    public async Task VadLoop_EmptyTranscription_RestartsListening()
    {
        var vm = CreateViewModel(o => o.Audio.VoiceActivity.Enabled = true);

        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "" });

        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        _audioService.SpeechStarted += Raise.Event();
        _audioService.SilenceDetected += Raise.Event();

        // Empty result should still restart listening (noise was detected)
        await _audioService.Received(2).StartListeningAsync();
        vm.State.Should().Be(RecordingState.Listening);
    }

    [Fact]
    public async Task VadLoop_TooShortRecording_RestartsListening()
    {
        var vm = CreateViewModel(o => o.Audio.VoiceActivity.Enabled = true);

        // Very small audio data → treated as too short
        _audioService.StopRecordingAsync().Returns(new byte[100]);

        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        _audioService.SpeechStarted += Raise.Event();
        _audioService.SilenceDetected += Raise.Event();

        // Too short should restart listening (not show error)
        await _audioService.Received(2).StartListeningAsync();
        vm.State.Should().Be(RecordingState.Listening);
    }

    // --- StartListening error ---

    [Fact]
    public async Task StartListening_WhenFails_GoesToError()
    {
        _audioService.StartListeningAsync().ThrowsAsync(new InvalidOperationException("Mic not found"));

        var vm = CreateViewModel(o => o.Audio.VoiceActivity.Enabled = true);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.State.Should().Be(RecordingState.Error);
        vm.ErrorMessage.Should().Contain("Listening failed");
    }

    // --- Push-to-talk unaffected by VAD ---

    [Fact]
    public async Task PushToTalk_WithVadEnabled_StartsNormalRecording()
    {
        var vm = CreateViewModel(o => o.Audio.VoiceActivity.Enabled = true);

        await vm.HotkeyStartRecordingAsync();

        vm.State.Should().Be(RecordingState.Recording);
        await _audioService.Received(1).StartRecordingAsync();
        await _audioService.DidNotReceive().StartListeningAsync();
    }

    // --- DismissResult stops VAD loop ---

    [Fact]
    public async Task DismissResult_DuringVadLoop_StopsListening()
    {
        var vm = CreateViewModel(o =>
        {
            o.Audio.VoiceActivity.Enabled = true;
            o.Overlay.ShowResultOverlay = true;
        });

        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "Hello" });

        // Start VAD loop
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        _audioService.SpeechStarted += Raise.Event();

        // After transcription, if VAD loop restarts listening
        _audioService.SilenceDetected += Raise.Event();
        vm.State.Should().Be(RecordingState.Listening);

        // Now if we toggle (dismiss from listening), should go to idle
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        vm.State.Should().Be(RecordingState.Idle);
    }

    // --- Captures foreground window ---

    [Fact]
    public async Task StartListening_CapturesForegroundWindow()
    {
        _windowFocusService.GetForegroundWindow().Returns(new IntPtr(12345));

        var vm = CreateViewModel(o => o.Audio.VoiceActivity.Enabled = true);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        _windowFocusService.Received(1).GetForegroundWindow();
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

}
