using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Voxwright.Core.Models;
using Voxwright.Core.Services;
using Voxwright.Core.Services.IDE;
using Voxwright.Core.Services.TextCorrection;
using Voxwright.Tests.TestHelpers;

namespace Voxwright.Tests.Services;

public class GroqTextCorrectionServiceTests
{
    [Fact]
    public void ProviderType_IsGroq()
    {
        var service = CreateService(apiKey: "test-key");
        service.ProviderType.Should().Be(TextCorrectionProvider.Groq);
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

    private static GroqTextCorrectionService CreateService(string? apiKey)
    {
        var options = OptionsHelper.CreateMonitor(o =>
        {
            o.TextCorrection.Provider = TextCorrectionProvider.Groq;
            o.TextCorrection.Groq.ApiKey = apiKey;
            o.TextCorrection.Groq.Model = "llama-3.3-70b-versatile";
            o.TextCorrection.Groq.Endpoint = "https://api.groq.com/openai/v1";
        });

        return new GroqTextCorrectionService(
            NullLogger<GroqTextCorrectionService>.Instance, options,
            Substitute.For<IDictionaryService>(),
            Substitute.For<IIDEContextService>(),
            new OpenAiClientFactory(options));
    }
}
