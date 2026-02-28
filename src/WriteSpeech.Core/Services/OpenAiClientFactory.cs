using System.ClientModel;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using WriteSpeech.Core.Configuration;

namespace WriteSpeech.Core.Services;

public class OpenAiClientFactory
{
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, OpenAIClient> _clients = new();

    public OpenAiClientFactory(IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        _optionsMonitor = optionsMonitor;
    }

    public OpenAIClient GetClient()
    {
        var opts = _optionsMonitor.CurrentValue.OpenAI;

        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new InvalidOperationException("OpenAI API key is not configured.");

        return GetClient(opts.ApiKey, opts.Endpoint);
    }

    public OpenAIClient GetClient(string apiKey, string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API key is not configured.");

        var cacheKey = CreateCacheKey(apiKey, endpoint);

        lock (_lock)
        {
            if (_clients.TryGetValue(cacheKey, out var existing))
                return existing;

            var clientOptions = new OpenAIClientOptions();
            if (!string.IsNullOrEmpty(endpoint))
                clientOptions.Endpoint = new Uri(endpoint);

            var client = new OpenAIClient(
                credential: new ApiKeyCredential(apiKey),
                options: clientOptions);
            _clients[cacheKey] = client;
            return client;
        }
    }

    public AudioClient GetAudioClient(string model) => GetClient().GetAudioClient(model);

    public AudioClient GetAudioClient(string model, string apiKey, string? endpoint) =>
        GetClient(apiKey, endpoint).GetAudioClient(model);

    public ChatClient GetChatClient(string model) => GetClient().GetChatClient(model);

    public ChatClient GetChatClient(string model, string apiKey, string? endpoint) =>
        GetClient(apiKey, endpoint).GetChatClient(model);

    internal static string CreateCacheKey(string apiKey, string? endpoint)
    {
        var input = $"{apiKey}|{endpoint ?? ""}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }
}
