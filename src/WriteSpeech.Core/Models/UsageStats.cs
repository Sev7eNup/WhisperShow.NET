namespace WriteSpeech.Core.Models;

/// <summary>
/// Tracks lifetime usage statistics for the application, persisted to disk as JSON.
/// Includes transcription counts, recording durations, provider breakdown, error rates,
/// and derived metrics such as success rate and estimated API cost. Displayed on the
/// Statistics settings page.
/// </summary>
public class UsageStats
{
    /// <summary>Total number of successful transcriptions completed.</summary>
    public int TotalTranscriptions { get; set; }

    /// <summary>Cumulative duration of all recordings in seconds.</summary>
    public double TotalRecordingSeconds { get; set; }

    /// <summary>Total number of raw audio bytes processed across all transcriptions.</summary>
    public long TotalAudioBytesProcessed { get; set; }

    /// <summary>Total number of transcription attempts that failed with an error.</summary>
    public int ErrorCount { get; set; }

    /// <summary>UTC timestamp of the very first transcription, or <c>null</c> if the app has never been used.</summary>
    public DateTime? FirstUsedUtc { get; set; }

    /// <summary>UTC timestamp of the most recent transcription.</summary>
    public DateTime? LastUsedUtc { get; set; }

    /// <summary>Number of successful transcriptions broken down by provider name (e.g., "OpenAI", "Local", "Parakeet").</summary>
    public Dictionary<string, int> TranscriptionsByProvider { get; set; } = new();

    /// <summary>Total number of words across all transcriptions.</summary>
    public int TotalWordsTranscribed { get; set; }

    /// <summary>Duration in seconds of the longest single recording.</summary>
    public double LongestRecordingSeconds { get; set; }

    /// <summary>Duration in seconds of the shortest single recording, or <c>null</c> if no recordings have been made.</summary>
    public double? ShortestRecordingSeconds { get; set; }

    /// <summary>Number of text corrections broken down by correction provider name (e.g., "OpenAI", "Anthropic", "Local").</summary>
    public Dictionary<string, int> CorrectionsByProvider { get; set; } = new();

    /// <summary>Average recording duration in seconds, computed as total recording time divided by transcription count.</summary>
    public double AverageRecordingSeconds => TotalTranscriptions > 0
        ? TotalRecordingSeconds / TotalTranscriptions : 0;

    /// <summary>Percentage of transcription attempts that succeeded (0-100), computed from successful transcriptions vs. total attempts including errors.</summary>
    public double SuccessRatePercent
    {
        get
        {
            var total = TotalTranscriptions + ErrorCount;
            return total > 0 ? (double)TotalTranscriptions / total * 100.0 : 0;
        }
    }

    /// <summary>Estimated time saved in minutes by using voice input instead of typing, based on an average typing speed of 40 words per minute.</summary>
    public double EstimatedTimeSavedMinutes => TotalWordsTranscribed / 40.0;

    /// <summary>Formatted display string for total recording time (e.g., "1:23:45" or "5:30").</summary>
    public string TotalRecordingDisplay
    {
        get
        {
            var ts = TimeSpan.FromSeconds(TotalRecordingSeconds);
            return ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"m\:ss");
        }
    }

    /// <summary>Approximate cost per minute of audio for the OpenAI Whisper API, in USD. Used for <see cref="EstimatedApiCost"/>.</summary>
    public const double CostPerMinuteUsd = 0.006;

    /// <summary>Estimated cumulative API cost in USD based on total recording duration and <see cref="CostPerMinuteUsd"/>. Only meaningful for cloud transcription usage.</summary>
    public double EstimatedApiCost => (TotalRecordingSeconds / 60.0) * CostPerMinuteUsd;
}
