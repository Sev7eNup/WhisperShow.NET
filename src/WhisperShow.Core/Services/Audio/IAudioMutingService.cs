namespace WhisperShow.Core.Services.Audio;

public interface IAudioMutingService
{
    void MuteOtherApplications();
    void UnmuteAll();
}
