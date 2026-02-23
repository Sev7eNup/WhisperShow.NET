namespace WhisperShow.Core.Services.TextCorrection;

/// <summary>
/// Combines transcription and text correction into a single API call
/// by sending audio directly to a GPT model with audio input capabilities.
/// </summary>
public interface ICombinedTranscriptionCorrectionService
{
    Task<string> TranscribeAndCorrectAsync(byte[] audioData, string? language, CancellationToken ct = default);
    bool IsAvailable { get; }
    bool IsModelLoaded { get; }
}
