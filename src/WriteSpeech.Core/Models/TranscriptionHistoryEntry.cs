namespace WriteSpeech.Core.Models;

/// <summary>
/// Represents a single entry in the transcription history log. Each completed
/// transcription (from microphone or file) is stored as a history entry,
/// allowing the user to review, copy, and re-use past transcriptions.
/// Persisted to <c>%APPDATA%/WriteSpeech/history.json</c>.
/// </summary>
public class TranscriptionHistoryEntry
{
    /// <summary>The full transcribed (and optionally corrected) text.</summary>
    public string Text { get; set; } = "";

    /// <summary>UTC timestamp of when this transcription was completed.</summary>
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Name of the transcription provider that produced this result (e.g., "OpenAI", "Local", "File (Parakeet)").</summary>
    public string Provider { get; set; } = "";

    /// <summary>Duration of the source audio in seconds.</summary>
    public double DurationSeconds { get; set; }

    /// <summary>Absolute path to the source audio file, or <c>null</c> if the transcription was from a live microphone recording.</summary>
    public string? SourceFilePath { get; set; }

    /// <summary>Truncated preview of <see cref="Text"/> (up to 80 characters) for display in history lists.</summary>
    public string Preview => Text.Length > 80 ? Text[..80] + "..." : Text;

    /// <summary>Human-readable relative timestamp (e.g., "just now", "5m ago", "2h ago", "3d ago").</summary>
    public string TimeAgo
    {
        get
        {
            var diff = DateTime.UtcNow - TimestampUtc;
            return diff.TotalMinutes < 1 ? "just now"
                : diff.TotalMinutes < 60 ? $"{(int)diff.TotalMinutes}m ago"
                : diff.TotalHours < 24 ? $"{(int)diff.TotalHours}h ago"
                : $"{(int)diff.TotalDays}d ago";
        }
    }
}
