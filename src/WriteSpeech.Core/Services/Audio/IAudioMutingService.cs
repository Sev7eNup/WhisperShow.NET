namespace WriteSpeech.Core.Services.Audio;

/// <summary>
/// Mutes and unmutes other audio applications while dictating
/// to prevent interference with the recording.
/// </summary>
public interface IAudioMutingService
{
    /// <summary>Mutes all audio sessions except the current process and system sounds.</summary>
    void MuteOtherApplications();

    /// <summary>Restores the original volume of all previously muted audio sessions.</summary>
    void UnmuteAll();
}
