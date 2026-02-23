namespace WhisperShow.Core.Services.Audio;

public interface ISoundEffectService
{
    bool Enabled { get; set; }
    void PlayStartRecording();
    void PlayStopRecording();
    void PlayError();
}
