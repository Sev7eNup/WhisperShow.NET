using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using Voxwright.Core.Configuration;
using Voxwright.Core.Models;
using Voxwright.Core.Services.IDE;

namespace Voxwright.Core.Services.TextCorrection;

public class CustomTextCorrectionService : CloudTextCorrectionServiceBase
{
    private readonly OpenAiClientFactory _clientFactory;
    public override TextCorrectionProvider ProviderType => TextCorrectionProvider.Custom;

    public CustomTextCorrectionService(
        ILogger<CustomTextCorrectionService> logger,
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

        var chatClient = _clientFactory.GetChatClient(custom.Model, custom.ApiKey, custom.Endpoint);

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
