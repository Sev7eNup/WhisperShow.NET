namespace Voxwright.Core.Services.Transcription;

/// <summary>
/// Optional interface for transcription services that can yield segments progressively.
/// When implemented, the overlay shows partial transcription results in real-time.
/// </summary>
public interface IStreamingTranscriptionService
{
    IAsyncEnumerable<string> TranscribeStreamingAsync(
        byte[] audioData,
        string? language = null,
        CancellationToken cancellationToken = default);
}
