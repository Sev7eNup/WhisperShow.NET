namespace WriteSpeech.Core.Models;

public class UsageStats
{
    public int TotalTranscriptions { get; set; }
    public double TotalRecordingSeconds { get; set; }
    public long TotalAudioBytesProcessed { get; set; }
    public int ErrorCount { get; set; }
    public DateTime? FirstUsedUtc { get; set; }
    public DateTime? LastUsedUtc { get; set; }
    public Dictionary<string, int> TranscriptionsByProvider { get; set; } = new();
    public int TotalWordsTranscribed { get; set; }
    public double LongestRecordingSeconds { get; set; }
    public double? ShortestRecordingSeconds { get; set; }
    public Dictionary<string, int> CorrectionsByProvider { get; set; } = new();

    public double AverageRecordingSeconds => TotalTranscriptions > 0
        ? TotalRecordingSeconds / TotalTranscriptions : 0;

    public double SuccessRatePercent
    {
        get
        {
            var total = TotalTranscriptions + ErrorCount;
            return total > 0 ? (double)TotalTranscriptions / total * 100.0 : 0;
        }
    }

    public double EstimatedTimeSavedMinutes => TotalWordsTranscribed / 40.0;

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

    // Rough cost estimate: Whisper API pricing
    public const double CostPerMinuteUsd = 0.006;
    public double EstimatedApiCost => (TotalRecordingSeconds / 60.0) * CostPerMinuteUsd;
}
