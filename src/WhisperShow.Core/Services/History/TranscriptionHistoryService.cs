using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.History;

public class TranscriptionHistoryService : ITranscriptionHistoryService
{
    private readonly ILogger<TranscriptionHistoryService> _logger;
    private readonly int _maxEntries;
    private readonly string _filePath;
    private readonly Lock _lock = new();
    private readonly DebouncedSaveHelper _saveHelper;
    private List<TranscriptionHistoryEntry>? _entries;

    public TranscriptionHistoryService(
        ILogger<TranscriptionHistoryService> logger,
        IOptions<WhisperShowOptions> options)
    {
        _logger = logger;
        _maxEntries = options.Value.App.MaxHistoryEntries;
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperShow", "transcription-history.json");
        _saveHelper = new DebouncedSaveHelper(SaveAsync, logger);
    }

    public IReadOnlyList<TranscriptionHistoryEntry> GetEntries()
    {
        EnsureLoaded();
        lock (_lock) return _entries!.AsReadOnly();
    }

    public void AddEntry(string text, string provider, double durationSeconds)
    {
        EnsureLoaded();
        lock (_lock)
        {
            _entries!.Insert(0, new TranscriptionHistoryEntry
            {
                Text = text,
                TimestampUtc = DateTime.UtcNow,
                Provider = provider,
                DurationSeconds = durationSeconds
            });

            while (_entries.Count > _maxEntries)
                _entries.RemoveAt(_entries.Count - 1);
        }

        ScheduleSave();
    }

    public void RemoveEntry(TranscriptionHistoryEntry entry)
    {
        EnsureLoaded();
        lock (_lock) _entries!.Remove(entry);
        ScheduleSave();
    }

    public void Clear()
    {
        EnsureLoaded();
        lock (_lock) _entries!.Clear();
        ScheduleSave();
    }

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _entries = [];
                return;
            }

            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            _entries = JsonSerializer.Deserialize<List<TranscriptionHistoryEntry>>(json) ?? [];
            _logger.LogInformation("Loaded {Count} history entries", _entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load transcription history");
            _entries = [];
        }
    }

    public async Task SaveAsync()
    {
        List<TranscriptionHistoryEntry> snapshot;
        lock (_lock) snapshot = [.. _entries ?? []];

        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save transcription history");
        }
    }

    private void EnsureLoaded()
    {
        if (_entries is not null) return;
        throw new InvalidOperationException(
            $"{nameof(TranscriptionHistoryService)} not initialized. Call LoadAsync() at startup.");
    }

    private void ScheduleSave() => _saveHelper.Schedule();
}
