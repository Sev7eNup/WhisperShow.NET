using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using Voxwright.Core.Configuration;
using Voxwright.Core.Models;
using Voxwright.Core.Services.IDE;

namespace Voxwright.Core.Services.TextCorrection;

public class GroqTextCorrectionService : CloudTextCorrectionServiceBase
{
    private readonly OpenAiClientFactory _clientFactory;
    public override TextCorrectionProvider ProviderType => TextCorrectionProvider.Groq;

    public GroqTextCorrectionService(
        ILogger<GroqTextCorrectionService> logger,
        IOptionsMonitor<VoxwrightOptions> optionsMonitor,
        IDictionaryService dictionaryService,
        IIDEContextService ideContextService,
        OpenAiClientFactory clientFactory)
        : base(logger, optionsMonitor, dictionaryService, ideContextService)
    {
        _clientFactory = clientFactory;
    }

    protected override async Task<string?> SendCorrectionRequestAsync(
        string systemPrompt, string userMessage, CancellationToken ct)
    {
        var groq = OptionsMonitor.CurrentValue.TextCorrection.Groq;

        if (string.IsNullOrWhiteSpace(groq.ApiKey))
        {
            Logger.LogWarning("Groq API key not configured, skipping text correction");
            return null;
        }

        var chatClient = _clientFactory.GetChatClient(groq.Model, groq.ApiKey, groq.Endpoint);

        var result = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userMessage)
            ],
            new ChatCompletionOptions { Temperature = 0 },
            ct);

        return result.Value.Content.FirstOrDefault()?.Text;
    }
}
