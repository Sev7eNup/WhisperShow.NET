using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.IDE;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.Services;

public class CustomTextCorrectionServiceTests
{
    [Fact]
    public void ProviderType_IsCustom()
    {
        var service = CreateService(apiKey: "test-key", endpoint: "https://my-server.com/v1");
        service.ProviderType.Should().Be(TextCorrectionProvider.Custom);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsRawText_WhenApiKeyMissing()
    {
        var service = CreateService(apiKey: null, endpoint: "https://my-server.com/v1");
        const string rawText = "hello world";

        var result = await service.CorrectAsync(rawText, "en");

        result.Should().Be(rawText);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsRawText_WhenApiKeyEmpty()
    {
        var service = CreateService(apiKey: "", endpoint: "https://my-server.com/v1");
        const string rawText = "hello world";

        var result = await service.CorrectAsync(rawText, "en");

        result.Should().Be(rawText);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsRawText_WhenEndpointMissing()
    {
        var service = CreateService(apiKey: "test-key", endpoint: null);
        const string rawText = "hello world";

        var result = await service.CorrectAsync(rawText, "en");

        result.Should().Be(rawText);
    }

    [Fact]
    public async Task CorrectAsync_ReturnsRawText_WhenEndpointEmpty()
    {
        var service = CreateService(apiKey: "test-key", endpoint: "");
        const string rawText = "hello world";

        var result = await service.CorrectAsync(rawText, "en");

        result.Should().Be(rawText);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var service = CreateService(apiKey: "test-key", endpoint: "https://my-server.com/v1");
        var act = () => service.Dispose();
        act.Should().NotThrow();
    }

    private static CustomTextCorrectionService CreateService(string? apiKey, string? endpoint)
    {
        var options = OptionsHelper.CreateMonitor(o =>
        {
            o.TextCorrection.Provider = TextCorrectionProvider.Custom;
            o.TextCorrection.Custom.ApiKey = apiKey;
            o.TextCorrection.Custom.Model = "my-model";
            o.TextCorrection.Custom.Endpoint = endpoint;
        });

        return new CustomTextCorrectionService(
            NullLogger<CustomTextCorrectionService>.Instance, options,
            Substitute.For<IDictionaryService>(),
            Substitute.For<IIDEContextService>());
    }
}
