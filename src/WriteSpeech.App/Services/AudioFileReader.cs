using System.IO;
using Concentus;
using Concentus.Oggfile;
using Microsoft.Extensions.Logging;
using NAudio.Vorbis;
using NAudio.Wave;
using WriteSpeech.Core.Services.Audio;

namespace WriteSpeech.App.Services;

public class AudioFileReader : IAudioFileReader
{
    internal const long MaxAudioFileSize = 500 * 1024 * 1024; // 500 MB

    private readonly ILogger<AudioFileReader> _logger;

    public AudioFileReader(ILogger<AudioFileReader> logger)
    {
        _logger = logger;
    }

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
        using var fileStream = File.OpenRead(filePath);
        var opusDecoder = OpusCodecFactory.CreateDecoder(48000, 1);
        var oggIn = new OpusOggReadStream(opusDecoder, fileStream);

        var pcmStream = new MemoryStream();
        long totalBytes = 0;
        while (oggIn.HasNextPacket)
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
