using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.TextCorrection;

public class OpenAiTextCorrectionService : ITextCorrectionService
{
    private readonly ILogger<OpenAiTextCorrectionService> _logger;
    private readonly IOptionsMonitor<WhisperShowOptions> _optionsMonitor;
    private readonly IDictionaryService _dictionaryService;
    private readonly OpenAiClientFactory _clientFactory;

    public TextCorrectionProvider ProviderType => TextCorrectionProvider.Cloud;
    public bool IsModelLoaded => true;

    public OpenAiTextCorrectionService(
        ILogger<OpenAiTextCorrectionService> logger,
        IOptionsMonitor<WhisperShowOptions> optionsMonitor,
        IDictionaryService dictionaryService,
        OpenAiClientFactory clientFactory)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _dictionaryService = dictionaryService;
        _clientFactory = clientFactory;
    }

    public async Task<string> CorrectAsync(string rawText, string? language, CancellationToken ct = default)
    {
        try
        {
            var options = _optionsMonitor.CurrentValue;

            var chatClient = _clientFactory.GetChatClient(options.TextCorrection.Model);

            var systemPrompt = options.TextCorrection.SystemPrompt ?? TextCorrectionDefaults.CorrectionSystemPrompt;
            systemPrompt += _dictionaryService.BuildPromptFragment();

            var languageHint = string.IsNullOrEmpty(language)
                ? "Keep the SAME language as the input — do NOT translate"
                : $"Output language MUST be: {language}";
            var userMessage = $"[{languageHint}]\n{rawText}";

            _logger.LogInformation("Sending text correction request ({Length} chars, model: {Model})",
                rawText.Length, options.TextCorrection.Model);

            var result = await chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userMessage)
                ],
                new ChatCompletionOptions { Temperature = 0 },
                ct);

            var correctedText = result.Value.Content.FirstOrDefault()?.Text;

            _logger.LogInformation("Text correction completed: {OrigLength} → {CorrLength} chars",
                rawText.Length, correctedText?.Length ?? 0);

            return string.IsNullOrWhiteSpace(correctedText) ? rawText : correctedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Text correction failed, returning raw text");
            return rawText;
        }
    }

    public void Dispose()
    {
        // No unmanaged resources — cloud client lifecycle is managed by OpenAiClientFactory
    }
}
