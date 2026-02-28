namespace WriteSpeech.Core.Services.Audio;

public interface IVoiceActivityService : IDisposable
{
    event EventHandler? SpeechStarted;
    event EventHandler? SilenceDetected;
    void ProcessAudioChunk(float[] samples);
    bool IsSpeechActive { get; }
    void Reset();
    bool IsModelLoaded { get; }
    void EnsureModelLoaded();
    void UnloadModel();
}
