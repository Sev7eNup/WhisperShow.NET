using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using WhisperShow.Core.Configuration;

namespace WhisperShow.Core.Services.TextCorrection;

public class OpenAiTextCorrectionService : ITextCorrectionService
{
    private const string DefaultSystemPrompt =
        """
        You are a speech-to-text post-processor. Fix punctuation, capitalization,
        and grammar in the transcribed text. Do not change wording, meaning, or
        structure. Do not add or remove content. Return only the corrected text
        with no explanation.
        """;

    private readonly ILogger<OpenAiTextCorrectionService> _logger;
    private readonly WhisperShowOptions _options;
    private ChatClient? _chatClient;

    public OpenAiTextCorrectionService(
        ILogger<OpenAiTextCorrectionService> logger,
        IOptions<WhisperShowOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<string> CorrectAsync(string rawText, string? language, CancellationToken ct = default)
    {
        try
        {
            _chatClient ??= new ChatClient(
                model: _options.TextCorrection.Model,
                apiKey: _options.OpenAI.ApiKey!);

            var systemPrompt = _options.TextCorrection.SystemPrompt ?? DefaultSystemPrompt;

            var languageHint = string.IsNullOrEmpty(language) ? "auto-detected" : language;
            var userMessage = $"[Language: {languageHint}]\n{rawText}";

            _logger.LogInformation("Sending text correction request ({Length} chars, model: {Model})",
                rawText.Length, _options.TextCorrection.Model);

            var result = await _chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userMessage)
                ],
                cancellationToken: ct);

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
