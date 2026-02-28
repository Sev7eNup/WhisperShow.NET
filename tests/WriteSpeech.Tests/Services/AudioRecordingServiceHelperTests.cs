using FluentAssertions;
using WriteSpeech.Core.Services.Audio;

namespace WriteSpeech.Tests.Services;

public class AudioRecordingServiceHelperTests
{
    [Fact]
    public void ConvertBytesToFloats_AllZeros_ReturnsAllZeros()
    {
        var buffer = new byte[8]; // 4 samples of 16-bit silence
        var result = AudioRecordingService.ConvertBytesToFloats(buffer, 8);

        result.Should().HaveCount(4);
        result.Should().AllBeEquivalentTo(0f);
    }

    [Fact]
    public void ConvertBytesToFloats_MaxPositive_ReturnsNearOne()
    {
        // 16-bit max positive = 32767 = 0xFF7F (little-endian: 0xFF, 0x7F)
        var buffer = new byte[] { 0xFF, 0x7F };
        var result = AudioRecordingService.ConvertBytesToFloats(buffer, 2);

        result.Should().HaveCount(1);
        result[0].Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void ConvertBytesToFloats_MaxNegative_ReturnsMinusOne()
    {
        // 16-bit -32768 = 0x0080 (little-endian: 0x00, 0x80)
        var buffer = new byte[] { 0x00, 0x80 };
        var result = AudioRecordingService.ConvertBytesToFloats(buffer, 2);

        result.Should().HaveCount(1);
        result[0].Should().BeApproximately(-1.0f, 0.001f);
    }

    [Fact]
    public void ConvertBytesToFloats_MultipleSamples_CorrectCount()
    {
        var buffer = new byte[20]; // 10 samples
        var result = AudioRecordingService.ConvertBytesToFloats(buffer, 20);

        result.Should().HaveCount(10);
    }

    [Fact]
    public void ConvertBytesToFloats_PartialBuffer_OnlyConvertsRecordedBytes()
    {
        var buffer = new byte[100]; // Large buffer
        buffer[0] = 0xFF;
        buffer[1] = 0x7F;

        // Only 4 bytes recorded (2 samples)
        var result = AudioRecordingService.ConvertBytesToFloats(buffer, 4);

        result.Should().HaveCount(2);
        result[0].Should().BeApproximately(1.0f, 0.001f);
        result[1].Should().Be(0f);
    }

    [Fact]
    public void ConvertBytesToFloats_KnownValue_IsNormalized()
    {
        // Sample value 16384 (half max) = 0x00, 0x40 (little-endian)
        var buffer = new byte[] { 0x00, 0x40 };
        var result = AudioRecordingService.ConvertBytesToFloats(buffer, 2);

        result[0].Should().BeApproximately(0.5f, 0.001f);
    }
}
