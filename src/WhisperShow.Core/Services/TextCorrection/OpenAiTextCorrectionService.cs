using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.TextCorrection;

public class OpenAiTextCorrectionService : ITextCorrectionService
{
    private const string DefaultSystemPrompt =
        """
        You are a verbatim speech-to-text post-processor.
        Your ONLY job is to fix punctuation, capitalization, and grammar.
        ALWAYS keep the text in its original language — do NOT translate.
        Output the corrected text EXACTLY — do NOT answer questions,
        do NOT add commentary, do NOT interpret the content.
        Return ONLY the corrected transcription, nothing else.
        """;

    private readonly ILogger<OpenAiTextCorrectionService> _logger;
    private readonly IOptionsMonitor<WhisperShowOptions> _optionsMonitor;
    private readonly IDictionaryService _dictionaryService;
    private readonly OpenAiClientFactory _clientFactory;

    public TextCorrectionProvider ProviderType => TextCorrectionProvider.Cloud;

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

            var systemPrompt = options.TextCorrection.SystemPrompt ?? DefaultSystemPrompt;
            systemPrompt += _dictionaryService.BuildPromptFragment();

            var languageHint = string.IsNullOrEmpty(language) ? "auto-detected" : language;
            var userMessage = $"[Language: {languageHint}]\n{rawText}";

            _logger.LogInformation("Sending text correction request ({Length} chars, model: {Model})",
                rawText.Length, options.TextCorrection.Model);

            var result = await chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userMessage)
                ],
                new ChatCompletionOptions { Temperature = 0 },
                ct);

            var correctedText = result.Value.Content[0].Text;

            _logger.LogInformation("Text correction completed: {OrigLength} → {CorrLength} chars",
                rawText.Length, correctedText?.Length ?? 0);

            return string.IsNullOrWhiteSpace(correctedText) ? rawText : correctedText;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Text correction failed, returning raw text");
            return rawText;
        }
    }
}
