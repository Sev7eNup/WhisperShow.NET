using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.IDE;

namespace WriteSpeech.Core.Services.TextCorrection;

public class OpenAiTextCorrectionService : ITextCorrectionService
{
    private readonly ILogger<OpenAiTextCorrectionService> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly IDictionaryService _dictionaryService;
    private readonly IIDEContextService _ideContextService;
    private readonly OpenAiClientFactory _clientFactory;

    public TextCorrectionProvider ProviderType => TextCorrectionProvider.Cloud;
    public bool IsModelLoaded => true;

    public OpenAiTextCorrectionService(
        ILogger<OpenAiTextCorrectionService> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor,
        IDictionaryService dictionaryService,
        IIDEContextService ideContextService,
        OpenAiClientFactory clientFactory)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _dictionaryService = dictionaryService;
        _ideContextService = ideContextService;
        _clientFactory = clientFactory;
    }

    public async Task<string> CorrectAsync(string rawText, string? language, string? systemPromptOverride = null, string? targetLanguage = null, CancellationToken ct = default)
    {
        try
        {
            var options = _optionsMonitor.CurrentValue;

            if (string.IsNullOrWhiteSpace(options.OpenAI.ApiKey))
            {
                _logger.LogWarning("OpenAI API key not configured, skipping text correction");
                return rawText;
            }

            var chatClient = _clientFactory.GetChatClient(options.TextCorrection.Model);

            var systemPrompt = systemPromptOverride ?? options.TextCorrection.SystemPrompt ?? TextCorrectionDefaults.CorrectionSystemPrompt;
            systemPrompt += _dictionaryService.BuildPromptFragment();
            systemPrompt += _ideContextService.BuildPromptFragment();

            if (options.TextCorrection.AutoAddToDictionary)
                systemPrompt += TextCorrectionDefaults.VocabExtractionInstruction;

            string userMessage;
            if (!string.IsNullOrEmpty(targetLanguage))
            {
                userMessage = $"[Translate to: {targetLanguage}]\n{rawText}";
            }
            else if (systemPromptOverride is not null)
            {
                userMessage = rawText;
            }
            else
            {
                var languageHint = string.IsNullOrEmpty(language)
                    ? "Keep the SAME language as the input — do NOT translate"
                    : $"Output language MUST be: {language}";
                userMessage = $"[{languageHint}]\n{rawText}";
            }

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

            if (string.IsNullOrWhiteSpace(correctedText))
                return rawText;

            if (options.TextCorrection.AutoAddToDictionary)
            {
                var (cleanText, vocab) = VocabResponseParser.Parse(correctedText);
                VocabResponseParser.AddExtractedVocabulary(vocab, _dictionaryService, _logger);
                return string.IsNullOrWhiteSpace(cleanText) ? rawText : cleanText;
            }

            return correctedText;
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
