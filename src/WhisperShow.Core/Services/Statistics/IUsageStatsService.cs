using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.Statistics;

public interface IUsageStatsService : IDisposable
{
    UsageStats GetStats();
    void RecordTranscription(double durationSeconds, long audioBytesProcessed, string provider);
    void RecordError();
    Task SaveAsync();
    Task LoadAsync();
    void Reset();
}
