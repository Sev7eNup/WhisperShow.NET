namespace WriteSpeech.Core.Services.Audio;

/// <summary>
/// Reads audio files in various formats and converts them for transcription.
/// </summary>
public interface IAudioFileReader
{
    /// <summary>
    /// Supported file extensions for audio transcription.
    /// </summary>
    static readonly string[] SupportedExtensions = [".mp3", ".wav", ".m4a", ".flac", ".ogg", ".mp4"];

    /// <summary>
    /// Reads an audio file and converts it to WAV (16kHz, 16-bit, mono) for local transcription.
    /// </summary>
    Task<byte[]> ReadAsWavAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Reads the raw bytes of an audio file without conversion (for cloud providers that handle decoding).
    /// </summary>
    Task<byte[]> ReadRawAsync(string filePath, CancellationToken ct = default);
}
