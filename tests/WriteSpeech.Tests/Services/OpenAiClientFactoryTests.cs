using FluentAssertions;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Services;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.Services;

public class OpenAiClientFactoryTests
{
    private const string TestApiKey = "sk-test-key-12345678901234567890123456789012345678901234567890";

    [Fact]
    public void GetClient_ReturnsClient_WhenApiKeyConfigured()
    {
        var monitor = OptionsHelper.CreateMonitor(o => o.OpenAI.ApiKey = TestApiKey);
        var factory = new OpenAiClientFactory(monitor);

        var client = factory.GetClient();

        client.Should().NotBeNull();
    }

    [Fact]
    public void GetClient_ReturnsSameClient_WhenOptionsUnchanged()
    {
        var monitor = OptionsHelper.CreateMonitor(o => o.OpenAI.ApiKey = TestApiKey);
        var factory = new OpenAiClientFactory(monitor);

        var first = factory.GetClient();
        var second = factory.GetClient();

        ReferenceEquals(first, second).Should().BeTrue();
    }

    [Fact]
    public void GetClient_CreatesNewClient_WhenApiKeyChanges()
    {
        var options = new WriteSpeechOptions { OpenAI = { ApiKey = TestApiKey } };
        var monitor = new TestOptionsMonitor<WriteSpeechOptions>(options);
        var factory = new OpenAiClientFactory(monitor);

        var first = factory.GetClient();

        var updated = new WriteSpeechOptions { OpenAI = { ApiKey = TestApiKey + "-changed" } };
        monitor.Update(updated);

        var second = factory.GetClient();

        ReferenceEquals(first, second).Should().BeFalse();
    }

    [Fact]
    public void GetClient_CreatesNewClient_WhenEndpointChanges()
    {
        var options = new WriteSpeechOptions
        {
            OpenAI = { ApiKey = TestApiKey, Endpoint = "https://api.example.com/v1" }
        };
        var monitor = new TestOptionsMonitor<WriteSpeechOptions>(options);
        var factory = new OpenAiClientFactory(monitor);

        var first = factory.GetClient();

        var updated = new WriteSpeechOptions
        {
            OpenAI = { ApiKey = TestApiKey, Endpoint = "https://api.other.com/v1" }
        };
        monitor.Update(updated);

        var second = factory.GetClient();

        ReferenceEquals(first, second).Should().BeFalse();
    }

    [Fact]
    public void GetClient_ThrowsInvalidOperation_WhenApiKeyNull()
    {
        var monitor = OptionsHelper.CreateMonitor(o => o.OpenAI.ApiKey = null);
        var factory = new OpenAiClientFactory(monitor);

        var act = () => factory.GetClient();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*API key*not configured*");
    }

    [Fact]
    public void GetClient_ThrowsInvalidOperation_WhenApiKeyEmpty()
    {
        var monitor = OptionsHelper.CreateMonitor(o => o.OpenAI.ApiKey = "");
        var factory = new OpenAiClientFactory(monitor);

        var act = () => factory.GetClient();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetAudioClient_ReturnsNonNull()
    {
        var monitor = OptionsHelper.CreateMonitor(o => o.OpenAI.ApiKey = TestApiKey);
        var factory = new OpenAiClientFactory(monitor);

        var audioClient = factory.GetAudioClient("whisper-1");

        audioClient.Should().NotBeNull();
    }

    [Fact]
    public void GetChatClient_ReturnsNonNull()
    {
        var monitor = OptionsHelper.CreateMonitor(o => o.OpenAI.ApiKey = TestApiKey);
        var factory = new OpenAiClientFactory(monitor);

        var chatClient = factory.GetChatClient("gpt-4o-mini");

        chatClient.Should().NotBeNull();
    }

    // --- Explicit credentials overloads ---

    [Fact]
    public void GetClient_WithExplicitCredentials_ReturnsClient()
    {
        var monitor = OptionsHelper.CreateMonitor();
        var factory = new OpenAiClientFactory(monitor);

        var client = factory.GetClient(TestApiKey, null);

        client.Should().NotBeNull();
    }

    [Fact]
    public void GetClient_WithExplicitCredentials_CachesByKeyAndEndpoint()
    {
        var monitor = OptionsHelper.CreateMonitor();
        var factory = new OpenAiClientFactory(monitor);

        var first = factory.GetClient(TestApiKey, "https://api.groq.com/v1");
        var second = factory.GetClient(TestApiKey, "https://api.groq.com/v1");

        ReferenceEquals(first, second).Should().BeTrue();
    }

    [Fact]
    public void GetClient_WithDifferentEndpoints_ReturnsDifferentClients()
    {
        var monitor = OptionsHelper.CreateMonitor();
        var factory = new OpenAiClientFactory(monitor);

        var groqClient = factory.GetClient(TestApiKey, "https://api.groq.com/v1");
        var customClient = factory.GetClient(TestApiKey, "https://custom.api.com/v1");

        ReferenceEquals(groqClient, customClient).Should().BeFalse();
    }

    [Fact]
    public void GetClient_WithExplicitEmptyKey_Throws()
    {
        var monitor = OptionsHelper.CreateMonitor();
        var factory = new OpenAiClientFactory(monitor);

        var act = () => factory.GetClient("", null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetAudioClient_WithExplicitCredentials_ReturnsNonNull()
    {
        var monitor = OptionsHelper.CreateMonitor();
        var factory = new OpenAiClientFactory(monitor);

        var audioClient = factory.GetAudioClient("whisper-large-v3", TestApiKey, "https://api.groq.com/v1");

        audioClient.Should().NotBeNull();
    }

    // --- Security: Hashed cache key ---

    [Fact]
    public void CreateCacheKey_DoesNotContainRawApiKey()
    {
        var cacheKey = OpenAiClientFactory.CreateCacheKey(TestApiKey, null);

        cacheKey.Should().NotContain("sk-test");
        cacheKey.Should().NotContain(TestApiKey);
    }

    [Fact]
    public void CreateCacheKey_DifferentKeys_ProduceDifferentHashes()
    {
        var key1 = OpenAiClientFactory.CreateCacheKey("key-A", null);
        var key2 = OpenAiClientFactory.CreateCacheKey("key-B", null);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void CreateCacheKey_SameInputs_ProduceSameHash()
    {
        var key1 = OpenAiClientFactory.CreateCacheKey(TestApiKey, "https://api.example.com");
        var key2 = OpenAiClientFactory.CreateCacheKey(TestApiKey, "https://api.example.com");

        key1.Should().Be(key2);
    }

    [Fact]
    public void CreateCacheKey_DifferentEndpoints_ProduceDifferentHashes()
    {
        var key1 = OpenAiClientFactory.CreateCacheKey(TestApiKey, "https://api.a.com");
        var key2 = OpenAiClientFactory.CreateCacheKey(TestApiKey, "https://api.b.com");

        key1.Should().NotBe(key2);
    }
}
