namespace WriteSpeech.Core.Models;

/// <summary>
/// Contains the output of a speech-to-text transcription operation.
/// Returned by transcription services after converting recorded audio into text,
/// regardless of whether the transcription was performed locally or via a cloud API.
/// </summary>
public class TranscriptionResult
{
    /// <summary>The transcribed text produced from the audio input.</summary>
    public required string Text { get; init; }

    /// <summary>The detected or specified language of the transcription (e.g., "en", "de"), or <c>null</c> if the provider did not report a language.</summary>
    public string? Language { get; init; }

    /// <summary>The wall-clock duration of the audio that was transcribed, or <c>null</c> if not reported by the provider.</summary>
    public TimeSpan? Duration { get; init; }
}
