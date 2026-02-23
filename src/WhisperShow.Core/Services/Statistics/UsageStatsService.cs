using System.Text.Json;
using Microsoft.Extensions.Logging;
using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.Statistics;

public class UsageStatsService : IUsageStatsService
{
    private readonly ILogger<UsageStatsService> _logger;
    private readonly string _filePath;
    private UsageStats _stats = new();
    private readonly Lock _lock = new();
    private readonly DebouncedSaveHelper _saveHelper;
    private bool _loaded;

    public UsageStatsService(ILogger<UsageStatsService> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperShow", "usage-stats.json");
        _saveHelper = new DebouncedSaveHelper(SaveAsync, logger);
    }

    public UsageStats GetStats()
    {
        EnsureLoaded();
        lock (_lock) return Clone(_stats);
    }

    public void RecordTranscription(double durationSeconds, long audioBytesProcessed, string provider)
    {
        EnsureLoaded();
        lock (_lock)
        {
            _stats.TotalTranscriptions++;
            _stats.TotalRecordingSeconds += durationSeconds;
            _stats.TotalAudioBytesProcessed += audioBytesProcessed;
            _stats.LastUsedUtc = DateTime.UtcNow;
            _stats.FirstUsedUtc ??= DateTime.UtcNow;

            if (!_stats.TranscriptionsByProvider.TryAdd(provider, 1))
                _stats.TranscriptionsByProvider[provider]++;
        }

        ScheduleSave();
    }

    public void RecordError()
    {
        EnsureLoaded();
        lock (_lock) _stats.ErrorCount++;
        ScheduleSave();
    }

    public void Reset()
    {
        lock (_lock) _stats = new UsageStats();
        ScheduleSave();
    }

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _loaded = true;
                return;
            }

            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            var stats = JsonSerializer.Deserialize<UsageStats>(json);
            if (stats is not null)
            {
                lock (_lock) _stats = stats;
            }

            _loaded = true;
            _logger.LogInformation("Loaded usage stats: {Count} transcriptions", _stats.TotalTranscriptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load usage stats");
            _loaded = true;
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);

            UsageStats snapshot;
            lock (_lock) snapshot = Clone(_stats);

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save usage stats");
        }
    }

    private void ScheduleSave() => _saveHelper.Schedule();

    private void EnsureLoaded()
    {
        if (_loaded) return;
        throw new InvalidOperationException(
            $"{nameof(UsageStatsService)} not initialized. Call LoadAsync() at startup.");
    }

    private static UsageStats Clone(UsageStats s) => new()
    {
        TotalTranscriptions = s.TotalTranscriptions,
        TotalRecordingSeconds = s.TotalRecordingSeconds,
        TotalAudioBytesProcessed = s.TotalAudioBytesProcessed,
        ErrorCount = s.ErrorCount,
        FirstUsedUtc = s.FirstUsedUtc,
        LastUsedUtc = s.LastUsedUtc,
        TranscriptionsByProvider = new Dictionary<string, int>(s.TranscriptionsByProvider)
    };
}
