namespace WriteSpeech.Core.Services.TextCorrection;

/// <summary>
/// Combines transcription and text correction into a single API call
/// by sending audio directly to a GPT model with audio input capabilities.
/// </summary>
public interface ICombinedTranscriptionCorrectionService
{
    Task<string> TranscribeAndCorrectAsync(byte[] audioData, string? language, string? systemPromptOverride = null, string? targetLanguage = null, CancellationToken ct = default);
    /// <summary>Gets whether the combined audio model is configured and usable.</summary>
    bool IsAvailable { get; }
    /// <summary>Gets whether the combined audio model is currently loaded and ready for inference.</summary>
    bool IsModelLoaded { get; }
}
