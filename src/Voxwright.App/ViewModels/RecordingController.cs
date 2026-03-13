using Microsoft.Extensions.Logging;
using Voxwright.Core.Services;
using Voxwright.Core.Services.Audio;

namespace Voxwright.App.ViewModels;

/// <summary>
/// Manages the audio recording lifecycle: start/stop recording, start/stop listening (VAD),
/// timer management, muting, and sound effects. Communicates state changes back to
/// <see cref="OverlayViewModel"/> via events.
/// </summary>
public class RecordingController : IDisposable
{
    private readonly IAudioRecordingService _audioService;
    private readonly IAudioMutingService _mutingService;
    private readonly ISoundEffectService _soundEffects;
    private readonly IDispatcherService _dispatcher;
    private readonly ILogger<RecordingController> _logger;

    private DateTime _recordingStartTime;
    private System.Timers.Timer? _recordingTimer;
    private CancellationTokenSource? _autoDismissCts;

    public RecordingController(
        IAudioRecordingService audioService,
        IAudioMutingService mutingService,
        ISoundEffectService soundEffects,
        IDispatcherService dispatcher,
        ILogger<RecordingController> logger)
    {
        _audioService = audioService;
        _mutingService = mutingService;
        _soundEffects = soundEffects;
        _dispatcher = dispatcher;
        _logger = logger;

        _audioService.AudioLevelChanged += OnAudioLevelChanged;
        _audioService.RecordingError += OnRecordingError;
        _audioService.MaxDurationReached += OnMaxDurationReached;
        _audioService.SpeechStarted += OnSpeechStarted;
        _audioService.SilenceDetected += OnSilenceDetected;
    }

    // --- Events (consumed by OverlayViewModel) ---

    /// <summary>Audio level changed (for waveform visualization).</summary>
    public event EventHandler<float>? AudioLevelChanged;

    /// <summary>Recording device error occurred.</summary>
    public event EventHandler<Exception>? RecordingError;

    /// <summary>Maximum recording duration reached, should auto-stop.</summary>
    public event EventHandler? MaxDurationReached;

    /// <summary>VAD detected speech onset (Listening → Recording transition).</summary>
    public event EventHandler? SpeechStarted;

    /// <summary>VAD detected silence after speech, should auto-stop.</summary>
    public event EventHandler? SilenceDetected;

    /// <summary>Recording timer tick with formatted time string.</summary>
    public event Action<string>? RecordingTimerTick;

    /// <summary>Auto-dismiss timer expired.</summary>
    public event EventHandler? AutoDismissExpired;

    // --- Audio control ---

    /// <summary>
    /// Starts audio recording. Optionally mutes other audio applications first
    /// and plays a start-recording sound effect.
    /// </summary>
    public async Task StartRecordingAsync(bool muteWhileDictating)
    {
        if (muteWhileDictating)
            _mutingService.MuteOtherApplications();

        _soundEffects.PlayStartRecording();
        await _audioService.StartRecordingAsync();
    }

    /// <summary>Stops the active recording and returns the captured audio as a WAV byte array.</summary>
    public async Task<byte[]> StopRecordingAsync()
    {
        return await _audioService.StopRecordingAsync();
    }

    /// <summary>
    /// Starts VAD listening mode. The microphone is opened but no audio is saved
    /// until Voice Activity Detection triggers the <see cref="SpeechStarted"/> event.
    /// </summary>
    public async Task StartListeningAsync(bool muteWhileDictating)
    {
        if (muteWhileDictating)
            _mutingService.MuteOtherApplications();

        await _audioService.StartListeningAsync();
    }

    /// <summary>Stops VAD listening mode and optionally unmutes other applications.</summary>
    public void StopListening(bool muteWhileDictating)
    {
        _audioService.StopListening();
        if (muteWhileDictating)
            _mutingService.UnmuteAll();
    }

    // --- Timer management ---

    /// <summary>Starts a 1-second interval timer that fires <see cref="RecordingTimerTick"/> with formatted elapsed time.</summary>
    public void StartRecordingTimer()
    {
        _recordingStartTime = DateTime.UtcNow;
        _recordingTimer = new System.Timers.Timer(1000);
        _recordingTimer.Elapsed += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - _recordingStartTime;
            RecordingTimerTick?.Invoke($"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}");
        };
        _recordingTimer.Start();
    }

    /// <summary>Stops and disposes the recording duration timer.</summary>
    public void StopRecordingTimer()
    {
        _recordingTimer?.Stop();
        _recordingTimer?.Dispose();
        _recordingTimer = null;
    }

    /// <summary>Starts an auto-dismiss countdown that fires <see cref="AutoDismissExpired"/> after the specified delay.</summary>
    public void StartAutoDismissTimer(int autoDismissSeconds)
        => _ = RunAutoDismissAsync(autoDismissSeconds);

    /// <summary>Cancels any pending auto-dismiss countdown.</summary>
    public void CancelAutoDismissTimer()
    {
        _autoDismissCts?.Cancel();
        _autoDismissCts?.Dispose();
        _autoDismissCts = null;
    }

    /// <summary>Get elapsed recording time in seconds since last StartRecordingTimer.</summary>
    public double GetElapsedSeconds()
        => (DateTime.UtcNow - _recordingStartTime).TotalSeconds;

    // --- Sound effects ---

    /// <summary>Plays the start-recording sound effect.</summary>
    public void PlayStartRecording() => _soundEffects.PlayStartRecording();

    /// <summary>Plays the stop-recording sound effect.</summary>
    public void PlayStopRecording() => _soundEffects.PlayStopRecording();

    /// <summary>Plays the error sound effect.</summary>
    public void PlayError() => _soundEffects.PlayError();

    // --- Muting ---

    /// <summary>Restores audio for all applications that were muted during dictation.</summary>
    public void UnmuteAll() => _mutingService.UnmuteAll();

    // --- Private event handlers ---

    private void OnAudioLevelChanged(object? sender, float level)
        => AudioLevelChanged?.Invoke(this, level);

    private void OnRecordingError(object? sender, Exception ex)
        => RecordingError?.Invoke(this, ex);

    private void OnMaxDurationReached(object? sender, EventArgs e)
        => MaxDurationReached?.Invoke(this, EventArgs.Empty);

    private void OnSpeechStarted(object? sender, EventArgs e)
        => SpeechStarted?.Invoke(this, EventArgs.Empty);

    private void OnSilenceDetected(object? sender, EventArgs e)
        => SilenceDetected?.Invoke(this, EventArgs.Empty);

    private async Task RunAutoDismissAsync(int seconds)
    {
        _autoDismissCts?.Cancel();
        _autoDismissCts?.Dispose();
        _autoDismissCts = new CancellationTokenSource();
        var token = _autoDismissCts.Token;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), token);
            AutoDismissExpired?.Invoke(this, EventArgs.Empty);
        }
        catch (TaskCanceledException) { }
    }

    /// <summary>Unsubscribes from audio service events, cancels timers, and releases resources.</summary>
    public void Dispose()
    {
        _audioService.AudioLevelChanged -= OnAudioLevelChanged;
        _audioService.RecordingError -= OnRecordingError;
        _audioService.MaxDurationReached -= OnMaxDurationReached;
        _audioService.SpeechStarted -= OnSpeechStarted;
        _audioService.SilenceDetected -= OnSilenceDetected;
        _autoDismissCts?.Cancel();
        _autoDismissCts?.Dispose();
        _autoDismissCts = null;
        _recordingTimer?.Stop();
        _recordingTimer?.Dispose();
        _recordingTimer = null;
    }
}
