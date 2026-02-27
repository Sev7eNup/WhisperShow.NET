using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.IDE;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.Services;

public class GoogleTextCorrectionServiceTests
{
    [Fact]
    public void ProviderType_IsGoogle()
    {
        var service = CreateService(apiKey: "test-key");
        service.ProviderType.Should().Be(TextCorrectionProvider.Google);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsRawText_WhenApiKeyMissing()
    {
        var service = CreateService(apiKey: null);
        const string rawText = "hello world";

        var result = await service.CorrectAsync(rawText, "en");

        result.Should().Be(rawText);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsRawText_WhenApiKeyEmpty()
    {
        var service = CreateService(apiKey: "");
        const string rawText = "hello world";

        var result = await service.CorrectAsync(rawText, "en");

        result.Should().Be(rawText);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsRawText_WhenApiFails()
    {
        var service = CreateService(apiKey: "invalid-key");
        const string rawText = "Dies ist ein Test.";

        var result = await service.CorrectAsync(rawText, "de");

        result.Should().Be(rawText);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var service = CreateService(apiKey: "test-key");
        var act = () => service.Dispose();
        act.Should().NotThrow();
    }

    private static GoogleTextCorrectionService CreateService(string? apiKey)
    {
        var options = OptionsHelper.CreateMonitor(o =>
        {
            o.TextCorrection.Provider = TextCorrectionProvider.Google;
            o.TextCorrection.Google.ApiKey = apiKey;
            o.TextCorrection.Google.Model = "gemini-2.5-flash";
            o.TextCorrection.Google.Endpoint = "https://generativelanguage.googleapis.com/v1beta/openai/";
        });

        return new GoogleTextCorrectionService(
            NullLogger<GoogleTextCorrectionService>.Instance, options,
            Substitute.For<IDictionaryService>(),
            Substitute.For<IIDEContextService>(),
            new OpenAiClientFactory(options));
    }
}
