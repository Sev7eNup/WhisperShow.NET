using System.Media;
using Microsoft.Extensions.Logging;
using WhisperShow.Core.Services.Audio;

namespace WhisperShow.App.Services;

public class SoundEffectService : ISoundEffectService
{
    private readonly ILogger<SoundEffectService> _logger;

    public bool Enabled { get; set; }

    public SoundEffectService(ILogger<SoundEffectService> logger, bool enabled)
    {
        _logger = logger;
        Enabled = enabled;
    }

    public void PlayStartRecording()
    {
        if (!Enabled) return;
        _logger.LogDebug("Playing start recording sound");
        SystemSounds.Exclamation.Play();
    }

    public void PlayStopRecording()
    {
        if (!Enabled) return;
        _logger.LogDebug("Playing stop recording sound");
        SystemSounds.Asterisk.Play();
    }

    public void PlayError()
    {
        if (!Enabled) return;
        _logger.LogDebug("Playing error sound");
        SystemSounds.Hand.Play();
    }
}
