using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.TextCorrection;

/// <summary>
/// Corrects and improves raw transcription text using an AI language model,
/// applying grammar fixes, filler-word removal, and mode-specific formatting.
/// </summary>
public interface ITextCorrectionService : IDisposable
{
    /// <summary>Corrects the raw transcription text, optionally using a mode-specific system prompt and target language for translation.</summary>
    /// <param name="rawText">The unprocessed transcription text to correct.</param>
    /// <param name="language">The source language code, or null for auto-detect.</param>
    /// <param name="systemPromptOverride">Custom system prompt from the active correction mode, or null for the default prompt.</param>
    /// <param name="targetLanguage">Target language for translation modes, or null for same-language correction.</param>
    Task<string> CorrectAsync(string rawText, string? language, string? systemPromptOverride = null, string? targetLanguage = null, CancellationToken ct = default);

    /// <summary>Gets the provider type enum value for this service.</summary>
    TextCorrectionProvider ProviderType { get; }

    /// <summary>Gets whether the local model is loaded (always true for cloud providers).</summary>
    bool IsModelLoaded { get; }
}
