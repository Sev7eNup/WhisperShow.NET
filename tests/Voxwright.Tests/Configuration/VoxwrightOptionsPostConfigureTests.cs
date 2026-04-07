using FluentAssertions;
using Voxwright.Core.Configuration;
using Voxwright.Core.Services.Configuration;

namespace Voxwright.Tests.Configuration;

public class VoxwrightOptionsPostConfigureTests
{
    [Fact]
    public void PostConfigure_DecryptsAllApiKeys()
    {
        var postConfigure = new VoxwrightOptionsPostConfigure();
        var options = new VoxwrightOptions();

        // Encrypt all API keys
        options.OpenAI.ApiKey = ApiKeyProtector.Protect("openai-key");
        options.GroqTranscription.ApiKey = ApiKeyProtector.Protect("groq-tx-key");
        options.CustomTranscription.ApiKey = ApiKeyProtector.Protect("custom-tx-key");
        options.TextCorrection.Anthropic.ApiKey = ApiKeyProtector.Protect("anthropic-key");
        options.TextCorrection.Google.ApiKey = ApiKeyProtector.Protect("google-key");
        options.TextCorrection.Groq.ApiKey = ApiKeyProtector.Protect("groq-key");
        options.TextCorrection.Custom.ApiKey = ApiKeyProtector.Protect("custom-key");

        postConfigure.PostConfigure(null, options);

        options.OpenAI.ApiKey.Should().Be("openai-key");
        options.GroqTranscription.ApiKey.Should().Be("groq-tx-key");
        options.CustomTranscription.ApiKey.Should().Be("custom-tx-key");
        options.TextCorrection.Anthropic.ApiKey.Should().Be("anthropic-key");
        options.TextCorrection.Google.ApiKey.Should().Be("google-key");
        options.TextCorrection.Groq.ApiKey.Should().Be("groq-key");
        options.TextCorrection.Custom.ApiKey.Should().Be("custom-key");
    }

    [Fact]
    public void PostConfigure_PlaintextKeys_PassedThrough()
    {
        var postConfigure = new VoxwrightOptionsPostConfigure();
        var options = new VoxwrightOptions();
        options.OpenAI.ApiKey = "sk-plain-text-key";

        postConfigure.PostConfigure(null, options);

        options.OpenAI.ApiKey.Should().Be("sk-plain-text-key");
    }

    [Fact]
    public void PostConfigure_NullKeys_StayNull()
    {
        var postConfigure = new VoxwrightOptionsPostConfigure();
        var options = new VoxwrightOptions();

        postConfigure.PostConfigure(null, options);

        options.OpenAI.ApiKey.Should().BeNull();
        options.TextCorrection.Anthropic.ApiKey.Should().BeNull();
    }
}
