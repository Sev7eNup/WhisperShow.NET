namespace WriteSpeech.Core.Services.Audio;

/// <summary>
/// Plays audio feedback sound effects during the recording lifecycle.
/// </summary>
public interface ISoundEffectService
{
    /// <summary>Plays the sound effect indicating recording has started.</summary>
    void PlayStartRecording();

    /// <summary>Plays the sound effect indicating recording has stopped.</summary>
    void PlayStopRecording();

    /// <summary>Plays the sound effect indicating an error occurred.</summary>
    void PlayError();
}
