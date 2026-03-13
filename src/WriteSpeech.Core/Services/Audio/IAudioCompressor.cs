namespace WriteSpeech.Core.Services.Audio;

/// <summary>
/// Compresses audio data to reduce file size before uploading to cloud APIs.
/// </summary>
public interface IAudioCompressor
{
    /// <summary>
    /// Compresses WAV audio data to MP3 format to reduce upload size.
    /// </summary>
    byte[] CompressToMp3(byte[] wavData, int bitrate = 64);
}
