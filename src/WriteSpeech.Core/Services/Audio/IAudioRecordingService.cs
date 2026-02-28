namespace WriteSpeech.Core.Services.Audio;

public interface IAudioRecordingService : IDisposable
{
    event EventHandler<float>? AudioLevelChanged;
    event EventHandler<Exception>? RecordingError;
    event EventHandler? MaxDurationReached;
    event EventHandler? SpeechStarted;
    event EventHandler? SilenceDetected;
    Task StartRecordingAsync();
    Task<byte[]> StopRecordingAsync();
    bool IsRecording { get; }
    Task StartListeningAsync();
    void StopListening();
    bool IsListening { get; }
}
