using System.IO;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using WriteSpeech.Core.Services.Audio;

namespace WriteSpeech.App.Services;

public class AudioFileReader : IAudioFileReader
{
    private readonly ILogger<AudioFileReader> _logger;

    public AudioFileReader(ILogger<AudioFileReader> logger)
    {
        _logger = logger;
    }

    public async Task<byte[]> ReadAsWavAsync(string filePath, CancellationToken ct = default)
    {
        _logger.LogInformation("Reading audio file as WAV: {FilePath}", filePath);

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using var reader = new MediaFoundationReader(filePath);
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
}
