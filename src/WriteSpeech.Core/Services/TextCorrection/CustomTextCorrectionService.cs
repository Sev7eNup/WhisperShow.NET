using System.ClientModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.IDE;

namespace WriteSpeech.Core.Services.TextCorrection;

public class CustomTextCorrectionService : CloudTextCorrectionServiceBase
{
    public override TextCorrectionProvider ProviderType => TextCorrectionProvider.Custom;

    public CustomTextCorrectionService(
        ILogger<CustomTextCorrectionService> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor,
        IDictionaryService dictionaryService,
        IIDEContextService ideContextService)
        : base(logger, optionsMonitor, dictionaryService, ideContextService)
    {
    }

    protected override async Task<string?> SendCorrectionRequestAsync(
        string systemPrompt, string userMessage, CancellationToken ct)
    {
        var custom = OptionsMonitor.CurrentValue.TextCorrection.Custom;

        if (string.IsNullOrWhiteSpace(custom.ApiKey))
        {
            Logger.LogWarning("Custom API key not configured, skipping text correction");
            return null;
        }

        if (string.IsNullOrWhiteSpace(custom.Endpoint))
        {
            Logger.LogWarning("Custom endpoint not configured, skipping text correction");
            return null;
        }

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(custom.Endpoint)
        };

        var client = new OpenAIClient(
            credential: new ApiKeyCredential(custom.ApiKey),
            options: clientOptions);

        var chatClient = client.GetChatClient(custom.Model);

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
