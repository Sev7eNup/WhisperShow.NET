using System.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Voxwright.Core.Configuration;
using Voxwright.Core.Services.Audio;

namespace Voxwright.App.Services;

public class SoundEffectService : ISoundEffectService
{
    private readonly ILogger<SoundEffectService> _logger;
    private readonly IOptionsMonitor<VoxwrightOptions> _optionsMonitor;

    private bool Enabled => _optionsMonitor.CurrentValue.App.SoundEffects;

    public SoundEffectService(ILogger<SoundEffectService> logger,
                              IOptionsMonitor<VoxwrightOptions> optionsMonitor)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
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
