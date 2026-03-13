namespace WriteSpeech.Core.Services.Audio;

/// <summary>
/// Records audio from the microphone with support for both manual recording
/// and VAD-based listening mode for hands-free dictation.
/// </summary>
public interface IAudioRecordingService : IDisposable
{
    /// <summary>Raised when the current audio input level changes (0.0 to 1.0).</summary>
    event EventHandler<float>? AudioLevelChanged;

    /// <summary>Raised when an error occurs during recording.</summary>
    event EventHandler<Exception>? RecordingError;

    /// <summary>Raised when the maximum recording duration is reached.</summary>
    event EventHandler? MaxDurationReached;

    /// <summary>Raised when voice activity is detected during listening mode.</summary>
    event EventHandler? SpeechStarted;

    /// <summary>Raised when silence is detected after speech during listening mode.</summary>
    event EventHandler? SilenceDetected;

    /// <summary>Starts capturing audio from the microphone.</summary>
    Task StartRecordingAsync();

    /// <summary>Stops recording and returns the captured audio as WAV bytes (16kHz, 16-bit, mono).</summary>
    Task<byte[]> StopRecordingAsync();

    /// <summary>Gets whether audio is currently being recorded.</summary>
    bool IsRecording { get; }

    /// <summary>Starts VAD listening mode, monitoring the microphone for speech onset.</summary>
    Task StartListeningAsync();

    /// <summary>Stops VAD listening mode.</summary>
    void StopListening();

    /// <summary>Gets whether the service is in VAD listening mode.</summary>
    bool IsListening { get; }
}
