using Microsoft.Extensions.Logging;
using NAudio.Lame;
using NAudio.Wave;

namespace WhisperShow.Core.Services.Audio;

public class AudioCompressor : IAudioCompressor
{
    private readonly ILogger<AudioCompressor> _logger;

    public AudioCompressor(ILogger<AudioCompressor> logger)
    {
        _logger = logger;
    }

    public byte[] CompressToMp3(byte[] wavData, int bitrate = 64)
    {
        _logger.LogDebug("Compressing WAV to MP3 ({InputSize} bytes, bitrate: {Bitrate} kbps)",
            wavData.Length, bitrate);

        try
        {
            using var wavStream = new MemoryStream(wavData);
            using var reader = new WaveFileReader(wavStream);
            using var mp3Stream = new MemoryStream();
            using (var writer = new LameMP3FileWriter(mp3Stream, reader.WaveFormat, bitrate))
            {
                reader.CopyTo(writer);
            }

            var result = mp3Stream.ToArray();
            _logger.LogInformation("MP3 compression: {InputSize} -> {OutputSize} bytes ({Ratio:P0} reduction)",
                wavData.Length, result.Length, 1.0 - (double)result.Length / wavData.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MP3 compression failed for {InputSize} byte WAV input", wavData.Length);
            throw;
        }
    }
}
