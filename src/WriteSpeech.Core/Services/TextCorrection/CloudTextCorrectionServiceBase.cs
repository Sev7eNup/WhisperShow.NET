using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.IDE;

namespace WriteSpeech.Core.Services.TextCorrection;

/// <summary>
/// Shared base class for all cloud-based text correction providers (OpenAI, Anthropic, Google, Groq).
/// Handles system prompt assembly (dictionary, IDE context, vocab extraction) and response processing.
/// </summary>
public abstract class CloudTextCorrectionServiceBase : ITextCorrectionService
{
    protected readonly ILogger Logger;
    protected readonly IOptionsMonitor<WriteSpeechOptions> OptionsMonitor;
    protected readonly IDictionaryService DictionaryService;
    protected readonly IIDEContextService IdeContextService;

    public abstract TextCorrectionProvider ProviderType { get; }
    public virtual bool IsModelLoaded => true;

    protected CloudTextCorrectionServiceBase(
        ILogger logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor,
        IDictionaryService dictionaryService,
        IIDEContextService ideContextService)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(dictionaryService);
        ArgumentNullException.ThrowIfNull(ideContextService);
        Logger = logger;
        OptionsMonitor = optionsMonitor;
        DictionaryService = dictionaryService;
        IdeContextService = ideContextService;
    }

    /// <summary>
    /// Maximum character length for text sent to cloud correction APIs.
    /// Prevents excessive token usage from very long transcriptions or selected text.
    /// </summary>
    internal const int MaxInputLength = 50_000;

    public async Task<string> CorrectAsync(
        string rawText,
        string? language,
        string? systemPromptOverride = null,
        string? targetLanguage = null,
        CancellationToken ct = default)
    {
        try
        {
            if (rawText.Length > MaxInputLength)
            {
                Logger.LogWarning("Input text exceeds maximum length ({Length} > {Max}), truncating",
                    rawText.Length, MaxInputLength);
                rawText = rawText[..MaxInputLength];
            }

            var options = OptionsMonitor.CurrentValue;
            var (systemPrompt, userMessage) = BuildPrompt(options, rawText, language, systemPromptOverride, targetLanguage);

            Logger.LogInformation("Sending text correction request ({Length} chars, provider: {Provider})",
                rawText.Length, ProviderType);

            var correctedText = await SendCorrectionRequestAsync(systemPrompt, userMessage, ct);

            Logger.LogInformation("Text correction completed: {OrigLength} → {CorrLength} chars",
                rawText.Length, correctedText?.Length ?? 0);

            return ProcessResponse(correctedText, rawText, options.TextCorrection.AutoAddToDictionary);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Text correction failed ({Provider}), returning raw text", ProviderType);
            return rawText;
        }
    }

    protected abstract Task<string?> SendCorrectionRequestAsync(
        string systemPrompt, string userMessage, CancellationToken ct);

    protected (string systemPrompt, string userMessage) BuildPrompt(
        WriteSpeechOptions options,
        string rawText,
        string? language,
        string? systemPromptOverride,
        string? targetLanguage)
    {
        return TextCorrectionDefaults.BuildCorrectionPrompt(
            systemPromptOverride,
            options.TextCorrection.SystemPrompt,
            DictionaryService.BuildPromptFragment(),
            IdeContextService.BuildPromptFragment(),
            options.TextCorrection.AutoAddToDictionary,
            rawText,
            language,
            targetLanguage);
    }

    protected string ProcessResponse(string? correctedText, string rawText, bool autoAddToDictionary)
    {
        if (string.IsNullOrWhiteSpace(correctedText))
            return rawText;

        if (autoAddToDictionary)
        {
            var (cleanText, vocab) = VocabResponseParser.Parse(correctedText);
            VocabResponseParser.AddExtractedVocabulary(vocab, DictionaryService, Logger);
            return string.IsNullOrWhiteSpace(cleanText) ? rawText : cleanText;
        }

        return correctedText;
    }

    public virtual void Dispose()
    {
    }
}
