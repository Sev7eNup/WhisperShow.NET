using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.Transcription;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.Services;

public class ParakeetTranscriptionServiceTests
{
    private ParakeetTranscriptionService CreateService(Action<WriteSpeechOptions>? configure = null)
    {
        var options = OptionsHelper.CreateMonitor(o =>
        {
            o.Provider = TranscriptionProvider.Parakeet;
            configure?.Invoke(o);
        });
        return new ParakeetTranscriptionService(
            NullLogger<ParakeetTranscriptionService>.Instance, options);
    }

    [Fact]
    public void ProviderType_IsParakeet()
    {
        var service = CreateService();
        service.ProviderType.Should().Be(TranscriptionProvider.Parakeet);
    }

    [Fact]
    public void ProviderName_ContainsParakeet()
    {
        var service = CreateService();
        service.ProviderName.Should().Contain("Parakeet");
    }

    [Fact]
    public void IsAvailable_ReturnsFalse_WhenModelDirectoryDoesNotExist()
    {
        var service = CreateService(o =>
            o.Parakeet.ModelDirectory = @"C:\nonexistent\path\12345");
        service.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void IsModelLoaded_IsFalse_Initially()
    {
        var service = CreateService();
        service.IsModelLoaded.Should().BeFalse();
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var service = CreateService();
        var act = () => service.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenCalledTwice()
    {
        var service = CreateService();
        service.Dispose();
        var act = () => service.Dispose();
        act.Should().NotThrow();
    }

    // --- WAV conversion ---

    [Fact]
    public void ConvertWavToFloatSamples_EmptyData_ReturnsEmpty()
    {
        var result = ParakeetTranscriptionService.ConvertWavToFloatSamples([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ConvertWavToFloatSamples_HeaderOnly_ReturnsEmpty()
    {
        var header = new byte[44];
        var result = ParakeetTranscriptionService.ConvertWavToFloatSamples(header);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ConvertWavToFloatSamples_SilentSamples_ReturnsZeros()
    {
        // 44 byte header + 4 bytes of silence (2 samples of 16-bit zeros)
        var data = new byte[48]; // all zeros
        var result = ParakeetTranscriptionService.ConvertWavToFloatSamples(data);

        result.Should().HaveCount(2);
        result[0].Should().Be(0f);
        result[1].Should().Be(0f);
    }

    [Fact]
    public void ConvertWavToFloatSamples_MaxPositive_NormalizesCorrectly()
    {
        var data = new byte[46]; // header + 1 sample
        // short.MaxValue = 32767 → little-endian: 0xFF, 0x7F
        data[44] = 0xFF;
        data[45] = 0x7F;

        var result = ParakeetTranscriptionService.ConvertWavToFloatSamples(data);

        result.Should().HaveCount(1);
        result[0].Should().BeApproximately(32767f / 32768f, 0.0001f);
    }

    [Fact]
    public void ConvertWavToFloatSamples_MaxNegative_NormalizesCorrectly()
    {
        var data = new byte[46]; // header + 1 sample
        // short.MinValue = -32768 → little-endian: 0x00, 0x80
        data[44] = 0x00;
        data[45] = 0x80;

        var result = ParakeetTranscriptionService.ConvertWavToFloatSamples(data);

        result.Should().HaveCount(1);
        result[0].Should().Be(-1.0f);
    }

    [Fact]
    public void ConvertWavToFloatSamples_MultipleSamples_ReturnsCorrectCount()
    {
        // 44 header + 20 bytes of data = 10 samples
        var data = new byte[64];
        var result = ParakeetTranscriptionService.ConvertWavToFloatSamples(data);
        result.Should().HaveCount(10);
    }
}
