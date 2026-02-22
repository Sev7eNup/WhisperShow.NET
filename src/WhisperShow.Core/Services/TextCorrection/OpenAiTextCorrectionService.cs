using System.ClientModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using WhisperShow.Core.Configuration;

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
    private ChatClient? _chatClient;
    private string? _lastApiKey;
    private string? _lastModel;
    private string? _lastEndpoint;

    public OpenAiTextCorrectionService(
        ILogger<OpenAiTextCorrectionService> logger,
        IOptionsMonitor<WhisperShowOptions> optionsMonitor,
        IDictionaryService dictionaryService)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _dictionaryService = dictionaryService;
    }

    public async Task<string> CorrectAsync(string rawText, string? language, CancellationToken ct = default)
    {
        try
        {
            var options = _optionsMonitor.CurrentValue;

            // Recreate client if API key, model, or endpoint changed
            if (_chatClient is null || _lastApiKey != options.OpenAI.ApiKey
                || _lastModel != options.TextCorrection.Model || _lastEndpoint != options.OpenAI.Endpoint)
            {
                var clientOptions = new OpenAIClientOptions();
                if (!string.IsNullOrEmpty(options.OpenAI.Endpoint))
                    clientOptions.Endpoint = new Uri(options.OpenAI.Endpoint);

                _chatClient = new ChatClient(
                    model: options.TextCorrection.Model,
                    credential: new ApiKeyCredential(options.OpenAI.ApiKey!),
                    options: clientOptions);
                _lastApiKey = options.OpenAI.ApiKey;
                _lastModel = options.TextCorrection.Model;
                _lastEndpoint = options.OpenAI.Endpoint;
            }

            var systemPrompt = options.TextCorrection.SystemPrompt ?? DefaultSystemPrompt;
            systemPrompt += _dictionaryService.BuildPromptFragment();

            var languageHint = string.IsNullOrEmpty(language) ? "auto-detected" : language;
            var userMessage = $"[Language: {languageHint}]\n{rawText}";

            _logger.LogInformation("Sending text correction request ({Length} chars, model: {Model})",
                rawText.Length, options.TextCorrection.Model);

            var result = await _chatClient.CompleteChatAsync(
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
