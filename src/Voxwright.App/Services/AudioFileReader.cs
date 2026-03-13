using System.IO;
using Concentus;
using Concentus.Oggfile;
using Microsoft.Extensions.Logging;
using NAudio.Vorbis;
using NAudio.Wave;
using Voxwright.Core.Services.Audio;

namespace Voxwright.App.Services;

/// <summary>
/// Reads audio files in various formats (MP3, WAV, M4A, FLAC, OGG/Vorbis, OGG/Opus, MP4)
/// and converts them to 16 kHz, 16-bit, mono WAV — the format required by speech-to-text engines.
///
/// Format handling:
/// - Most formats (MP3, WAV, M4A, FLAC, MP4) are opened via <c>MediaFoundationReader</c>,
///   which delegates to the Windows Media Foundation codecs installed on the system.
/// - OGG files require special handling because Media Foundation does not natively support OGG:
///   first tries NAudio.Vorbis (<see cref="VorbisWaveReader"/>), and if that fails (not a
///   Vorbis stream), falls back to the Concentus library for OGG/Opus decoding.
///
/// A 500 MB file size guard prevents out-of-memory crashes on very large files.
/// Resampling uses <c>MediaFoundationResampler</c> at quality level 60 (highest).
/// </summary>
public class AudioFileReader : IAudioFileReader
{
    /// <summary>
    /// Maximum allowed audio file size in bytes (500 MB). Files exceeding this limit
    /// are rejected to prevent out-of-memory conditions during decoding and resampling.
    /// </summary>
    internal const long MaxAudioFileSize = 500 * 1024 * 1024; // 500 MB

    private readonly ILogger<AudioFileReader> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioFileReader"/> class.
    /// </summary>
    public AudioFileReader(ILogger<AudioFileReader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Reads an audio file from disk, decodes it, and converts it to 16 kHz / 16-bit / mono WAV format.
    /// The conversion runs on a background thread via <see cref="Task.Run"/>.
    /// Throws <see cref="InvalidOperationException"/> if the file exceeds <see cref="MaxAudioFileSize"/>.
    /// </summary>
    /// <param name="filePath">Absolute path to the audio file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A byte array containing the complete WAV file (including header).</returns>
    public async Task<byte[]> ReadAsWavAsync(string filePath, CancellationToken ct = default)
    {
        _logger.LogInformation("Reading audio file as WAV: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaxAudioFileSize)
            throw new InvalidOperationException(
                $"Audio file exceeds maximum size of {MaxAudioFileSize / (1024 * 1024)} MB ({fileInfo.Length / (1024 * 1024)} MB).");

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var ext = Path.GetExtension(filePath);
            using WaveStream reader = ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                ? OpenOggReader(filePath)
                : new MediaFoundationReader(filePath);

            var targetFormat = new WaveFormat(16000, 16, 1);
            using var resampler = new MediaFoundationResampler(reader, targetFormat);
            resampler.ResamplerQuality = 60; // highest quality

            using var ms = new MemoryStream();
            WaveFileWriter.WriteWavFileToStream(ms, resampler);
            var result = ms.ToArray();

            _logger.LogInformation("Audio file converted to WAV: {Size} bytes (16kHz/16bit/Mono)", result.Length);
            return result;
        }, ct);
    }

    /// <summary>
    /// Reads a file as raw bytes without any format conversion.
    /// Used when the caller needs the original file content (e.g., for cloud API upload).
    /// </summary>
    /// <param name="filePath">Absolute path to the audio file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw file bytes.</returns>
    public async Task<byte[]> ReadRawAsync(string filePath, CancellationToken ct = default)
    {
        _logger.LogInformation("Reading raw audio file: {FilePath}", filePath);
        var data = await File.ReadAllBytesAsync(filePath, ct);
        _logger.LogInformation("Raw audio file read: {Size} bytes", data.Length);
        return data;
    }

    private WaveStream OpenOggReader(string filePath)
    {
        // Try OGG/Vorbis first (NAudio.Vorbis)
        try
        {
            var reader = new VorbisWaveReader(filePath);
            _logger.LogInformation("Opened OGG/Vorbis file: {SampleRate}Hz, {Channels}ch",
                reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
            return reader;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Not an OGG/Vorbis file, trying Opus");
        }

        // Fall back to OGG/Opus (Concentus)
        return DecodeOpusOgg(filePath);
    }

    private RawSourceWaveStream DecodeOpusOgg(string filePath)
    {
        const int maxPackets = 50_000;

        using var fileStream = File.OpenRead(filePath);
        var opusDecoder = OpusCodecFactory.CreateDecoder(48000, 1);
        var oggIn = new OpusOggReadStream(opusDecoder, fileStream);

        var pcmStream = new MemoryStream();
        long totalBytes = 0;
        int packetCount = 0;
        while (oggIn.HasNextPacket && packetCount++ < maxPackets)
        {
            var samples = oggIn.DecodeNextPacket();
            if (samples is null) continue;
            foreach (var sample in samples)
            {
                var bytes = BitConverter.GetBytes(sample);
                pcmStream.Write(bytes, 0, bytes.Length);
                totalBytes += bytes.Length;
                if (totalBytes > MaxAudioFileSize)
                    throw new InvalidOperationException("Decoded audio exceeds maximum size limit.");
            }
        }

        pcmStream.Position = 0;
        var waveFormat = new WaveFormat(48000, 16, 1);

        _logger.LogInformation("Decoded OGG/Opus file: {Samples} samples at 48kHz",
            pcmStream.Length / 2);

        return new RawSourceWaveStream(pcmStream, waveFormat);
    }
}
