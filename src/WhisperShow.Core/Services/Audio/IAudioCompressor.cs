namespace WhisperShow.Core.Services.Audio;

public interface IAudioCompressor
{
    /// <summary>
    /// Compresses WAV audio data to MP3 format to reduce upload size.
    /// </summary>
    byte[] CompressToMp3(byte[] wavData, int bitrate = 64);
}
