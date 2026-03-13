using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Voxwright.Core.Configuration;
using Voxwright.Core.Services;
using Voxwright.Core.Services.Audio;
using Voxwright.Core.Services.Transcription;
using Voxwright.Tests.TestHelpers;

namespace Voxwright.Tests.Services;

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
    public void ProviderName_ReturnsCloudAPI()
    {
        var service = CreateService(apiKey: "sk-test");
        service.ProviderName.Should().Be("Cloud API");
    }

    // --- Cloud sub-provider resolution ---

    [Fact]
    public void ResolveCloudConfig_DefaultOpenAI_ReturnsOpenAiConfig()
    {
        var service = CreateService(apiKey: "sk-openai-key");
        var (apiKey, endpoint, model) = service.ResolveCloudConfig();

        apiKey.Should().Be("sk-openai-key");
        endpoint.Should().BeNull();
        model.Should().Be("whisper-1");
    }

    [Fact]
    public void ResolveCloudConfig_Groq_ReturnsGroqConfig()
    {
        var options = new VoxwrightOptions
        {
            CloudTranscriptionProvider = "Groq",
            GroqTranscription = { ApiKey = "gsk-groq-key", Model = "whisper-large-v3-turbo" }
        };
        var service = CreateServiceFromOptions(options);
        var (apiKey, endpoint, model) = service.ResolveCloudConfig();

        apiKey.Should().Be("gsk-groq-key");
        endpoint.Should().Be("https://api.groq.com/openai/v1");
        model.Should().Be("whisper-large-v3-turbo");
    }

    [Fact]
    public void ResolveCloudConfig_Custom_ReturnsCustomConfig()
    {
        var options = new VoxwrightOptions
        {
            CloudTranscriptionProvider = "Custom",
            CustomTranscription = { ApiKey = "custom-key", Endpoint = "https://my-api.com/v1", Model = "my-model" }
        };
        var service = CreateServiceFromOptions(options);
        var (apiKey, endpoint, model) = service.ResolveCloudConfig();

        apiKey.Should().Be("custom-key");
        endpoint.Should().Be("https://my-api.com/v1");
        model.Should().Be("my-model");
    }

    [Fact]
    public void IsAvailable_GroqWithApiKey_ReturnsTrue()
    {
        var options = new VoxwrightOptions
        {
            CloudTranscriptionProvider = "Groq",
            GroqTranscription = { ApiKey = "gsk-test" }
        };
        var service = CreateServiceFromOptions(options);
        service.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_GroqWithoutApiKey_ReturnsFalse()
    {
        var options = new VoxwrightOptions
        {
            CloudTranscriptionProvider = "Groq",
            GroqTranscription = { ApiKey = null }
        };
        var service = CreateServiceFromOptions(options);
        service.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void IsAvailable_CustomWithApiKey_ReturnsTrue()
    {
        var options = new VoxwrightOptions
        {
            CloudTranscriptionProvider = "Custom",
            CustomTranscription = { ApiKey = "custom-key", Endpoint = "https://api.test.com" }
        };
        var service = CreateServiceFromOptions(options);
        service.IsAvailable.Should().BeTrue();
    }

    private static OpenAiTranscriptionService CreateService(string? apiKey)
    {
        var options = OptionsHelper.CreateMonitor(o => o.OpenAI.ApiKey = apiKey);
        return new OpenAiTranscriptionService(
            NullLogger<OpenAiTranscriptionService>.Instance, options,
            Substitute.For<IAudioCompressor>(),
            new OpenAiClientFactory(options));
    }

    private static OpenAiTranscriptionService CreateServiceFromOptions(VoxwrightOptions opts)
    {
        var monitor = new TestOptionsMonitor<VoxwrightOptions>(opts);
        return new OpenAiTranscriptionService(
            NullLogger<OpenAiTranscriptionService>.Instance, monitor,
            Substitute.For<IAudioCompressor>(),
            new OpenAiClientFactory(monitor));
    }
}
