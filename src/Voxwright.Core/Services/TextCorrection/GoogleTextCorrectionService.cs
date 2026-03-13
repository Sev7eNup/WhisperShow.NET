using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using Voxwright.Core.Configuration;
using Voxwright.Core.Models;
using Voxwright.Core.Services.IDE;

namespace Voxwright.Core.Services.TextCorrection;

public class GoogleTextCorrectionService : CloudTextCorrectionServiceBase
{
    private readonly OpenAiClientFactory _clientFactory;
    public override TextCorrectionProvider ProviderType => TextCorrectionProvider.Google;

    public GoogleTextCorrectionService(
        ILogger<GoogleTextCorrectionService> logger,
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
        var google = OptionsMonitor.CurrentValue.TextCorrection.Google;

        if (string.IsNullOrWhiteSpace(google.ApiKey))
        {
            Logger.LogWarning("Google API key not configured, skipping text correction");
            return null;
        }

        var chatClient = _clientFactory.GetChatClient(google.Model, google.ApiKey, google.Endpoint);

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
