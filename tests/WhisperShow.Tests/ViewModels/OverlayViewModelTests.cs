using System.Windows;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WhisperShow.App.Services;
using WhisperShow.App.ViewModels;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services.Audio;
using WhisperShow.Core.Services.Hotkey;
using WhisperShow.Core.Services.TextCorrection;
using WhisperShow.Core.Services.TextInsertion;
using WhisperShow.Core.Services.Transcription;
using WhisperShow.Tests.TestHelpers;

namespace WhisperShow.Tests.ViewModels;

public class OverlayViewModelTests : IDisposable
{
    private readonly IAudioRecordingService _audioService;
    private readonly IAudioMutingService _mutingService;
    private readonly ITextInsertionService _textInsertionService;
    private readonly ITextCorrectionService _textCorrectionService;
    private readonly ICombinedTranscriptionCorrectionService _combinedService;
    private readonly ITranscriptionService _transcriptionProvider;
    private readonly WhisperShowOptions _optionsValue;

    public OverlayViewModelTests()
    {
        WpfTestHelper.EnsureApplication();

        _audioService = Substitute.For<IAudioRecordingService>();
        _mutingService = Substitute.For<IAudioMutingService>();
        _textInsertionService = Substitute.For<ITextInsertionService>();
        _textCorrectionService = Substitute.For<ITextCorrectionService>();
        _combinedService = Substitute.For<ICombinedTranscriptionCorrectionService>();

        _transcriptionProvider = Substitute.For<ITranscriptionService>();
        _transcriptionProvider.ProviderName.Returns("Test Provider");
        _transcriptionProvider.IsAvailable.Returns(true);

        _optionsValue = new WhisperShowOptions();
    }

    private OverlayViewModel CreateViewModel(Action<WhisperShowOptions>? configure = null)
    {
        configure?.Invoke(_optionsValue);

        // Build a factory that returns our mock provider for both provider types.
        // Since we can't use OfType<> matching with mocks, we wrap it.
        var providerFactory = new TestProviderFactory(_transcriptionProvider);

        var opts = OptionsHelper.Create(o =>
        {
            o.Provider = _optionsValue.Provider;
            o.Language = _optionsValue.Language;
            o.TextCorrection = _optionsValue.TextCorrection;
            o.OpenAI = _optionsValue.OpenAI;
            o.Hotkey = _optionsValue.Hotkey;
            o.Audio = _optionsValue.Audio;
            o.Overlay = _optionsValue.Overlay;
        });

        var settingsVm = new SettingsViewModel(
            opts,
            Substitute.For<IGlobalHotkeyService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsViewModel>.Instance);

        return new OverlayViewModel(
            _audioService,
            _mutingService,
            providerFactory,
            _textInsertionService,
            _textCorrectionService,
            _combinedService,
            new SoundEffectService(Microsoft.Extensions.Logging.Abstractions.NullLogger<SoundEffectService>.Instance, false),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OverlayViewModel>.Instance,
            opts,
            settingsVm);
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
        _textCorrectionService.CorrectAsync("raw text", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("Corrected text.");

        var vm = CreateViewModel(o => o.TextCorrection.Enabled = true);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        await _textCorrectionService.Received(1).CorrectAsync("raw text", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopRecording_CorrectionDisabled_SkipsCorrectAsync()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "raw text" });

        var vm = CreateViewModel(o => o.TextCorrection.Enabled = false);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        await _textCorrectionService.DidNotReceive().CorrectAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
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
        _combinedService.TranscribeAndCorrectAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("corrected text");

        var vm = CreateViewModel(o =>
        {
            o.TextCorrection.Enabled = true;
            o.TextCorrection.UseCombinedAudioModel = true;
        });
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Recording
        await vm.ToggleRecordingCommand.ExecuteAsync(null); // → Stop

        await _combinedService.Received(1).TranscribeAndCorrectAsync(
            Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
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
            o.TextCorrection.Enabled = true;
            o.TextCorrection.UseCombinedAudioModel = false;
        });
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        await _transcriptionProvider.Received(1).TranscribeAsync(
            Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _combinedService.DidNotReceive().TranscribeAndCorrectAsync(
            Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopRecording_CombinedModelFails_FallsBackToStandardPipeline()
    {
        _audioService.StopRecordingAsync().Returns(new byte[2000]);
        _combinedService.IsAvailable.Returns(true);
        _combinedService.TranscribeAndCorrectAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("combined model error"));
        _transcriptionProvider.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TranscriptionResult { Text = "fallback text" });

        var vm = CreateViewModel(o =>
        {
            o.TextCorrection.Enabled = true;
            o.TextCorrection.UseCombinedAudioModel = true;
        });
        await vm.ToggleRecordingCommand.ExecuteAsync(null);
        await vm.ToggleRecordingCommand.ExecuteAsync(null);

        // Combined was tried first
        await _combinedService.Received(1).TranscribeAndCorrectAsync(
            Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
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

    // --- Provider Name ---

    [Fact]
    public void UpdateProviderName_SetsCurrentProviderName()
    {
        var vm = CreateViewModel();
        vm.CurrentProviderName.Should().Be("Test Provider");
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
}
