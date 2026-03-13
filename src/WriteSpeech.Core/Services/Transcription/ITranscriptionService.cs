using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.Transcription;

/// <summary>
/// Transcribes audio data to text using a speech-to-text provider
/// (cloud or local).
/// </summary>
public interface ITranscriptionService
{
    /// <summary>Transcribes raw WAV audio bytes (16kHz, 16-bit, mono) to text.</summary>
    /// <param name="language">Language code for transcription, or null for auto-detect.</param>
    Task<TranscriptionResult> TranscribeAsync(
        byte[] audioData,
        string? language = null,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the provider type enum value for this service.</summary>
    TranscriptionProvider ProviderType { get; }

    /// <summary>Gets the display name of the transcription provider.</summary>
    string ProviderName { get; }

    /// <summary>Gets whether the provider is configured and ready to transcribe.</summary>
    bool IsAvailable { get; }

    /// <summary>Gets whether the local model is loaded (always true for cloud providers).</summary>
    bool IsModelLoaded { get; }
}
