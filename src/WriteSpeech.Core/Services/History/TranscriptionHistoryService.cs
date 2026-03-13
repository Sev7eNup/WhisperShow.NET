using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;

namespace WriteSpeech.Core.Services.History;

public class TranscriptionHistoryService : ITranscriptionHistoryService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private readonly ILogger<TranscriptionHistoryService> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly string _filePath;
    private readonly Lock _lock = new();
    private readonly DebouncedSaveHelper _saveHelper;
    private List<TranscriptionHistoryEntry>? _entries;

    public TranscriptionHistoryService(
        ILogger<TranscriptionHistoryService> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            WriteSpeechOptions.AppDataFolderName, "transcription-history.json");
        _saveHelper = new DebouncedSaveHelper(SaveAsync, logger);
    }

    public IReadOnlyList<TranscriptionHistoryEntry> GetEntries()
    {
        EnsureLoaded();
        lock (_lock) return _entries!.AsReadOnly();
    }

    public void AddEntry(string text, string provider, double durationSeconds, string? sourceFilePath = null)
    {
        EnsureLoaded();
        lock (_lock)
        {
            _entries!.Insert(0, new TranscriptionHistoryEntry
            {
                Text = text,
                TimestampUtc = DateTime.UtcNow,
                Provider = provider,
                DurationSeconds = durationSeconds,
                SourceFilePath = sourceFilePath
            });

            while (_entries.Count > _optionsMonitor.CurrentValue.App.MaxHistoryEntries)
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
            _logger.LogWarning(ex, "Failed to load transcription history — starting with empty list");
            if (File.Exists(_filePath))
                AtomicFileHelper.BackupCorruptFile(_filePath, _logger);
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

            var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
            await AtomicFileHelper.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save transcription history");
        }
    }

    public void Dispose()
    {
        _saveHelper.FlushSync();
        _saveHelper.Dispose();
    }

    private void EnsureLoaded()
    {
        if (_entries is not null) return;
        throw new InvalidOperationException(
            $"{nameof(TranscriptionHistoryService)} not initialized. Call LoadAsync() at startup.");
    }

    private void ScheduleSave() => _saveHelper.Schedule();
}
