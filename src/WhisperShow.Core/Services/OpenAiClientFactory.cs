using System.ClientModel;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using WhisperShow.Core.Configuration;

namespace WhisperShow.Core.Services;

public class OpenAiClientFactory
{
    private readonly IOptionsMonitor<WhisperShowOptions> _optionsMonitor;
    private OpenAIClient? _client;
    private string? _lastApiKey;
    private string? _lastEndpoint;

    public OpenAiClientFactory(IOptionsMonitor<WhisperShowOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public OpenAIClient GetClient()
    {
        var opts = _optionsMonitor.CurrentValue.OpenAI;

        if (_client is null || _lastApiKey != opts.ApiKey || _lastEndpoint != opts.Endpoint)
        {
            var clientOptions = new OpenAIClientOptions();
            if (!string.IsNullOrEmpty(opts.Endpoint))
                clientOptions.Endpoint = new Uri(opts.Endpoint);

            _client = new OpenAIClient(
                credential: new ApiKeyCredential(opts.ApiKey!),
                options: clientOptions);
            _lastApiKey = opts.ApiKey;
            _lastEndpoint = opts.Endpoint;
        }

        return _client;
    }

    public AudioClient GetAudioClient(string model) => GetClient().GetAudioClient(model);

    public ChatClient GetChatClient(string model) => GetClient().GetChatClient(model);
}
