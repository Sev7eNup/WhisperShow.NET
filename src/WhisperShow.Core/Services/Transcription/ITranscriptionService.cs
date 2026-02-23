using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.Transcription;

public interface ITranscriptionService
{
    Task<TranscriptionResult> TranscribeAsync(
        byte[] audioData,
        string? language = null,
        CancellationToken cancellationToken = default);

    TranscriptionProvider ProviderType { get; }
    string ProviderName { get; }
    bool IsAvailable { get; }
    bool IsModelLoaded { get; }
}
