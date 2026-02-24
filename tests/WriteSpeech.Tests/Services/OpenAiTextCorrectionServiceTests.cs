using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.IDE;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.Services;

public class OpenAiTextCorrectionServiceTests
{
    [Fact]
    public async Task CorrectAsync_WhenApiFails_ReturnsRawText()
    {
        // Uses an invalid API key so the ChatClient call will throw.
        // The service should catch the exception and return the raw text.
        var service = CreateService(apiKey: "sk-invalid-key");
        const string rawText = "hallo das ist ein test";

        var result = await service.CorrectAsync(rawText, "de");

        result.Should().Be(rawText);
    }

    [Fact]
    public async Task CorrectAsync_PreservesRawTextOnException()
    {
        // Null API key → ChatClient constructor will throw.
        // Service should catch and return raw text unchanged.
        var service = CreateService(apiKey: null);
        const string rawText = "some raw transcription text";

        var result = await service.CorrectAsync(rawText, null);

        result.Should().Be(rawText);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsRawText_WhenApiKeyMissing()
    {
        var service = CreateService(apiKey: null);
        const string rawText = "Dies ist ein Test.";

        var result = await service.CorrectAsync(rawText, "de");

        result.Should().Be(rawText);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsRawText_WhenApiKeyEmpty()
    {
        var service = CreateService(apiKey: "");
        const string rawText = "Another test sentence.";

        var result = await service.CorrectAsync(rawText, "en");

        result.Should().Be(rawText);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var service = CreateService(apiKey: "sk-test");
        var act = () => service.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var service = CreateService(apiKey: "sk-test");
        service.Dispose();
        var act = () => service.Dispose();
        act.Should().NotThrow();
    }

    private static OpenAiTextCorrectionService CreateService(string? apiKey)
    {
        var options = OptionsHelper.CreateMonitor(o =>
        {
            o.OpenAI.ApiKey = apiKey;
            o.TextCorrection.Provider = TextCorrectionProvider.Cloud;
            o.TextCorrection.Model = "gpt-4o-mini";
        });
        return new OpenAiTextCorrectionService(
            NullLogger<OpenAiTextCorrectionService>.Instance, options,
            Substitute.For<IDictionaryService>(),
            Substitute.For<IIDEContextService>(),
            new OpenAiClientFactory(options));
    }
}
