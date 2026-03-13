using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Voxwright.Core.Models;
using Voxwright.Core.Services.IDE;
using Voxwright.Core.Services.TextCorrection;
using Voxwright.Tests.TestHelpers;

namespace Voxwright.Tests.Services;

public class AnthropicTextCorrectionServiceTests
{
    [Fact]
    public void ProviderType_IsAnthropic()
    {
        var service = CreateService(apiKey: "sk-test");
        service.ProviderType.Should().Be(TextCorrectionProvider.Anthropic);
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
        // Invalid API key will cause the HTTP call to fail
        var service = CreateService(apiKey: "sk-invalid-key");
        const string rawText = "Dies ist ein Test.";

        var result = await service.CorrectAsync(rawText, "de");

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

    private static AnthropicTextCorrectionService CreateService(string? apiKey)
    {
        var options = OptionsHelper.CreateMonitor(o =>
        {
            o.TextCorrection.Provider = TextCorrectionProvider.Anthropic;
            o.TextCorrection.Anthropic.ApiKey = apiKey;
            o.TextCorrection.Anthropic.Model = "claude-sonnet-4-6";
        });

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        return new AnthropicTextCorrectionService(
            NullLogger<AnthropicTextCorrectionService>.Instance, options,
            Substitute.For<IDictionaryService>(),
            Substitute.For<IIDEContextService>(),
            httpClientFactory);
    }
}
