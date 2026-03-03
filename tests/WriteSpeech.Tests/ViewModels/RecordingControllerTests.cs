using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.App.ViewModels;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Audio;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.ViewModels;

public class RecordingControllerTests : IDisposable
{
    private readonly IAudioRecordingService _audioService;
    private readonly IAudioMutingService _mutingService;
    private readonly ISoundEffectService _soundEffects;
    private readonly IDispatcherService _dispatcher;
    private readonly RecordingController _controller;

    public RecordingControllerTests()
    {
        _audioService = Substitute.For<IAudioRecordingService>();
        _mutingService = Substitute.For<IAudioMutingService>();
        _soundEffects = Substitute.For<ISoundEffectService>();
        _dispatcher = new SynchronousDispatcherService();

        _controller = new RecordingController(
            _audioService,
            _mutingService,
            _soundEffects,
            _dispatcher,
            NullLogger<RecordingController>.Instance);
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public async Task StartRecordingAsync_MuteEnabled_MutesOtherApps()
    {
        await _controller.StartRecordingAsync(muteWhileDictating: true);

        _mutingService.Received().MuteOtherApplications();
        _soundEffects.Received().PlayStartRecording();
        await _audioService.Received().StartRecordingAsync();
    }

    [Fact]
    public async Task StartRecordingAsync_MuteDisabled_DoesNotMute()
    {
        await _controller.StartRecordingAsync(muteWhileDictating: false);

        _mutingService.DidNotReceive().MuteOtherApplications();
        _soundEffects.Received().PlayStartRecording();
    }

    [Fact]
    public async Task StopRecordingAsync_ReturnsAudioData()
    {
        var expected = new byte[] { 1, 2, 3 };
        _audioService.StopRecordingAsync().Returns(expected);

        var result = await _controller.StopRecordingAsync();

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public void StopListening_MuteEnabled_UnmutesAll()
    {
        _controller.StopListening(muteWhileDictating: true);

        _audioService.Received().StopListening();
        _mutingService.Received().UnmuteAll();
    }

    [Fact]
    public void StopListening_MuteDisabled_DoesNotUnmute()
    {
        _controller.StopListening(muteWhileDictating: false);

        _audioService.Received().StopListening();
        _mutingService.DidNotReceive().UnmuteAll();
    }

    [Fact]
    public void AudioLevelChanged_ReraisesEvent()
    {
        float? received = null;
        _controller.AudioLevelChanged += (_, level) => received = level;

        _audioService.AudioLevelChanged += Raise.Event<EventHandler<float>>(_audioService, 0.75f);

        received.Should().Be(0.75f);
    }

    [Fact]
    public void RecordingError_ReraisesEvent()
    {
        Exception? received = null;
        _controller.RecordingError += (_, ex) => received = ex;

        var error = new InvalidOperationException("test error");
        _audioService.RecordingError += Raise.Event<EventHandler<Exception>>(_audioService, error);

        received.Should().BeSameAs(error);
    }

    [Fact]
    public void SpeechStarted_ReraisesEvent()
    {
        var fired = false;
        _controller.SpeechStarted += (_, _) => fired = true;

        _audioService.SpeechStarted += Raise.EventWith(EventArgs.Empty);

        fired.Should().BeTrue();
    }

    [Fact]
    public void SilenceDetected_ReraisesEvent()
    {
        var fired = false;
        _controller.SilenceDetected += (_, _) => fired = true;

        _audioService.SilenceDetected += Raise.EventWith(EventArgs.Empty);

        fired.Should().BeTrue();
    }

    [Fact]
    public void MaxDurationReached_ReraisesEvent()
    {
        var fired = false;
        _controller.MaxDurationReached += (_, _) => fired = true;

        _audioService.MaxDurationReached += Raise.EventWith(EventArgs.Empty);

        fired.Should().BeTrue();
    }

    [Fact]
    public async Task AutoDismissTimer_FiresExpiredEvent()
    {
        var tcs = new TaskCompletionSource();
        _controller.AutoDismissExpired += (_, _) => tcs.SetResult();

        _controller.StartAutoDismissTimer(1);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task, "auto-dismiss should fire within timeout");
    }

    [Fact]
    public async Task CancelAutoDismissTimer_PreventsExpiredEvent()
    {
        var fired = false;
        _controller.AutoDismissExpired += (_, _) => fired = true;

        _controller.StartAutoDismissTimer(1);
        _controller.CancelAutoDismissTimer();

        await Task.Delay(1500);
        fired.Should().BeFalse();
    }

    [Fact]
    public void GetElapsedSeconds_ReturnsElapsedTime()
    {
        _controller.StartRecordingTimer();
        // Timer just started, should be very close to 0
        _controller.GetElapsedSeconds().Should().BeLessThan(1);
        _controller.StopRecordingTimer();
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var controller = new RecordingController(
            _audioService, _mutingService, _soundEffects, _dispatcher,
            NullLogger<RecordingController>.Instance);

        var act = () => controller.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var controller = new RecordingController(
            _audioService, _mutingService, _soundEffects, _dispatcher,
            NullLogger<RecordingController>.Instance);

        controller.Dispose();
        var act = () => controller.Dispose();

        act.Should().NotThrow();
    }
}
