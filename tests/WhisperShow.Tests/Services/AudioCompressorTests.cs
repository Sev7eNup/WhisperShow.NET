using FluentAssertions;
using NAudio.Wave;
using WhisperShow.Core.Services.Audio;

namespace WhisperShow.Tests.Services;

public class AudioCompressorTests
{
    private static byte[] CreateTestWavData(double durationSeconds = 1.0)
    {
        var sampleRate = 16000;
        var bitsPerSample = 16;
        var channels = 1;
        var sampleCount = (int)(sampleRate * durationSeconds);

        using var stream = new System.IO.MemoryStream();
        using (var writer = new WaveFileWriter(stream, new WaveFormat(sampleRate, bitsPerSample, channels)))
        {
            // Write silence (zeros) as test audio
            var buffer = new byte[sampleCount * (bitsPerSample / 8)];
            writer.Write(buffer, 0, buffer.Length);
        }

        return stream.ToArray();
    }

    [Fact]
    public void CompressToMp3_WithValidWav_ProducesSmallerOutput()
    {
        var compressor = new AudioCompressor(Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioCompressor>.Instance);
        var wavData = CreateTestWavData(2.0);

        var mp3Data = compressor.CompressToMp3(wavData);

        mp3Data.Should().NotBeEmpty();
        mp3Data.Length.Should().BeLessThan(wavData.Length);
    }

    [Fact]
    public void CompressToMp3_WithValidWav_ReturnsNonEmptyData()
    {
        var compressor = new AudioCompressor(Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioCompressor>.Instance);
        var wavData = CreateTestWavData();

        var mp3Data = compressor.CompressToMp3(wavData);

        mp3Data.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CompressToMp3_WithInvalidData_ThrowsException()
    {
        var compressor = new AudioCompressor(Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioCompressor>.Instance);
        var invalidData = new byte[] { 0, 1, 2, 3, 4 };

        var act = () => compressor.CompressToMp3(invalidData);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void CompressToMp3_WithCustomBitrate_ProducesOutput()
    {
        var compressor = new AudioCompressor(Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioCompressor>.Instance);
        var wavData = CreateTestWavData(1.0);

        var mp3Data32 = compressor.CompressToMp3(wavData, bitrate: 32);
        var mp3Data128 = compressor.CompressToMp3(wavData, bitrate: 128);

        mp3Data32.Should().NotBeEmpty();
        mp3Data128.Should().NotBeEmpty();
        // Higher bitrate should produce larger output
        mp3Data128.Length.Should().BeGreaterThan(mp3Data32.Length);
    }
}
