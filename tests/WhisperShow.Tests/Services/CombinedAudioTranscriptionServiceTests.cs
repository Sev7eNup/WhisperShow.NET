using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WhisperShow.Core.Services;
using WhisperShow.Core.Services.Audio;
using WhisperShow.Core.Services.TextCorrection;
using WhisperShow.Tests.TestHelpers;

namespace WhisperShow.Tests.Services;

public class CombinedAudioTranscriptionServiceTests
{
    private readonly IAudioCompressor _audioCompressor = Substitute.For<IAudioCompressor>();

    private CombinedAudioTranscriptionService CreateService(
        string? apiKey = "sk-test",
        bool useCombinedModel = true)
    {
        var options = OptionsHelper.CreateMonitor(o =>
        {
            o.OpenAI.ApiKey = apiKey;
            o.TextCorrection.UseCombinedAudioModel = useCombinedModel;
            o.TextCorrection.CombinedAudioModel = "gpt-4o-mini-audio-preview";
        });

        return new CombinedAudioTranscriptionService(
            NullLogger<CombinedAudioTranscriptionService>.Instance,
            options,
            _audioCompressor,
            Substitute.For<IDictionaryService>(),
            new OpenAiClientFactory(options));
    }

    [Fact]
    public void IsAvailable_WithApiKeyAndEnabled_ReturnsTrue()
    {
        var service = CreateService(apiKey: "sk-test", useCombinedModel: true);

        service.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_WithoutApiKey_ReturnsFalse()
    {
        var service = CreateService(apiKey: null, useCombinedModel: true);

        service.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void IsAvailable_WithEmptyApiKey_ReturnsFalse()
    {
        var service = CreateService(apiKey: "", useCombinedModel: true);

        service.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void IsAvailable_WhenDisabled_ReturnsFalse()
    {
        var service = CreateService(apiKey: "sk-test", useCombinedModel: false);

        service.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task TranscribeAndCorrectAsync_WithInvalidKey_ThrowsException()
    {
        _audioCompressor.CompressToMp3(Arg.Any<byte[]>(), Arg.Any<int>())
            .Returns(new byte[] { 1, 2, 3 });

        var service = CreateService(apiKey: "sk-invalid");

        var act = () => service.TranscribeAndCorrectAsync(new byte[2000], "de");

        await act.Should().ThrowAsync<Exception>();
    }
}
