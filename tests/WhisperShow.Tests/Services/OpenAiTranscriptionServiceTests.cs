using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WhisperShow.Core.Services;
using WhisperShow.Core.Services.Audio;
using WhisperShow.Core.Services.Transcription;
using WhisperShow.Tests.TestHelpers;

namespace WhisperShow.Tests.Services;

public class OpenAiTranscriptionServiceTests
{
    [Fact]
    public void IsAvailable_WithApiKey_ReturnsTrue()
    {
        var service = CreateService(apiKey: "sk-test-key");
        service.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_WithNullApiKey_ReturnsFalse()
    {
        var service = CreateService(apiKey: null);
        service.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void IsAvailable_WithEmptyApiKey_ReturnsFalse()
    {
        var service = CreateService(apiKey: "");
        service.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void IsAvailable_WithWhitespaceApiKey_ReturnsFalse()
    {
        var service = CreateService(apiKey: "   ");
        service.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task TranscribeAsync_WhenNotAvailable_ThrowsInvalidOperation()
    {
        var service = CreateService(apiKey: null);

        var act = () => service.TranscribeAsync([1, 2, 3]);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*API key*");
    }

    [Fact]
    public void ProviderName_ReturnsOpenAIAPI()
    {
        var service = CreateService(apiKey: "sk-test");
        service.ProviderName.Should().Be("OpenAI API");
    }

    private static OpenAiTranscriptionService CreateService(string? apiKey)
    {
        var options = OptionsHelper.CreateMonitor(o => o.OpenAI.ApiKey = apiKey);
        return new OpenAiTranscriptionService(
            NullLogger<OpenAiTranscriptionService>.Instance, options,
            Substitute.For<IAudioCompressor>(),
            new OpenAiClientFactory(options));
    }
}
