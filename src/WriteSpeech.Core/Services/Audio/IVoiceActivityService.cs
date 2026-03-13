namespace WriteSpeech.Core.Services.Audio;

/// <summary>
/// Detects voice activity in audio streams using Silero VAD,
/// firing events when speech begins and when silence is detected after speech.
/// </summary>
public interface IVoiceActivityService : IDisposable
{
    /// <summary>Raised when speech is first detected in the audio stream.</summary>
    event EventHandler? SpeechStarted;

    /// <summary>Raised when silence is detected after a period of speech.</summary>
    event EventHandler? SilenceDetected;

    /// <summary>Feeds an audio chunk to the VAD model for analysis.</summary>
    void ProcessAudioChunk(float[] samples);

    /// <summary>Gets whether speech is currently being detected.</summary>
    bool IsSpeechActive { get; }

    /// <summary>Resets the VAD state for a new detection session.</summary>
    void Reset();

    /// <summary>Gets whether the Silero VAD model is loaded and ready.</summary>
    bool IsModelLoaded { get; }

    /// <summary>Loads the Silero VAD model if not already loaded.</summary>
    void EnsureModelLoaded();

    /// <summary>Unloads the VAD model to free resources.</summary>
    void UnloadModel();
}
