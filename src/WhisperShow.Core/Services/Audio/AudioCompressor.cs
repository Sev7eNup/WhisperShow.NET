using NAudio.Lame;
using NAudio.Wave;

namespace WhisperShow.Core.Services.Audio;

public class AudioCompressor : IAudioCompressor
{
    public byte[] CompressToMp3(byte[] wavData, int bitrate = 64)
    {
        using var wavStream = new MemoryStream(wavData);
        using var reader = new WaveFileReader(wavStream);
        using var mp3Stream = new MemoryStream();
        using (var writer = new LameMP3FileWriter(mp3Stream, reader.WaveFormat, bitrate))
        {
            reader.CopyTo(writer);
        }

        return mp3Stream.ToArray();
    }
}
