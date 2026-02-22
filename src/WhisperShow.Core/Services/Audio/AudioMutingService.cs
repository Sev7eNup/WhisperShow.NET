using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace WhisperShow.Core.Services.Audio;

public class AudioMutingService : IAudioMutingService
{
    private readonly ILogger<AudioMutingService> _logger;
    private readonly List<AudioSessionControl> _mutedSessions = new();
    private readonly int _ownProcessId = Environment.ProcessId;

    public AudioMutingService(ILogger<AudioMutingService> logger)
    {
        _logger = logger;
    }

    public void MuteOtherApplications()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    var session = sessions[i];
                    var processId = (int)session.GetProcessID;

                    if (processId != _ownProcessId
                        && processId != 0
                        && !session.SimpleAudioVolume.Mute)
                    {
                        session.SimpleAudioVolume.Mute = true;
                        _mutedSessions.Add(session);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not mute audio session {Index}", i);
                }
            }

            _logger.LogInformation("Muted {Count} audio sessions", _mutedSessions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mute other applications");
        }
    }

    public void UnmuteAll()
    {
        foreach (var session in _mutedSessions)
        {
            try
            {
                session.SimpleAudioVolume.Mute = false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not unmute audio session");
            }
        }

        _logger.LogInformation("Unmuted {Count} audio sessions", _mutedSessions.Count);
        _mutedSessions.Clear();
    }
}
