using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.Statistics;

/// <summary>
/// Tracks usage statistics including transcription count, duration, bytes processed,
/// provider breakdown, error rate, and estimated API cost.
/// </summary>
public interface IUsageStatsService : IDisposable
{
    /// <summary>Gets the current accumulated usage statistics.</summary>
    UsageStats GetStats();

    /// <summary>Records a completed transcription with its metrics.</summary>
    void RecordTranscription(double durationSeconds, long audioBytesProcessed, string provider, int wordCount, string correctionProvider);

    /// <summary>Records a transcription error occurrence.</summary>
    void RecordError();

    /// <summary>Persists the current statistics to disk.</summary>
    Task SaveAsync();

    /// <summary>Loads previously persisted statistics from disk.</summary>
    Task LoadAsync();

    /// <summary>Resets all statistics to their default values.</summary>
    void Reset();
}
