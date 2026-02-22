using System.Media;

namespace WhisperShow.App.Services;

public class SoundEffectService
{
    public bool Enabled { get; set; }

    public SoundEffectService(bool enabled)
    {
        Enabled = enabled;
    }

    public void PlayStartRecording()
    {
        if (Enabled) SystemSounds.Exclamation.Play();
    }

    public void PlayStopRecording()
    {
        if (Enabled) SystemSounds.Asterisk.Play();
    }

    public void PlayError()
    {
        if (Enabled) SystemSounds.Hand.Play();
    }
}
