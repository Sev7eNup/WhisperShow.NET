namespace WhisperShow.Core.Models;

public class TranscriptionResult
{
    public required string Text { get; init; }
    public string? Language { get; init; }
    public TimeSpan? Duration { get; init; }
}
