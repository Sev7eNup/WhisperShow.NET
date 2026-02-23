using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services;
using WhisperShow.Core.Services.TextCorrection;
using WhisperShow.Tests.TestHelpers;

namespace WhisperShow.Tests.Services;

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
            new OpenAiClientFactory(options));
    }
}
