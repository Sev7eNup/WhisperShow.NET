using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WhisperShow.Core.Services.TextCorrection;

public class DictionaryService : IDictionaryService
{
    private readonly ILogger<DictionaryService> _logger;
    private readonly string _filePath;
    private readonly List<string> _entries = [];
    private readonly Lock _lock = new();
    private readonly DebouncedSaveHelper _saveHelper;
    private bool _loaded;

    public DictionaryService(ILogger<DictionaryService> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperShow", "custom-dictionary.json");
        _saveHelper = new DebouncedSaveHelper(SaveAsync, logger, 300);
    }

    public IReadOnlyList<string> GetEntries()
    {
        EnsureLoaded();
        lock (_lock) return _entries.ToList();
    }

    public void AddEntry(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return;
        word = word.Trim();

        EnsureLoaded();
        lock (_lock)
        {
            if (_entries.Contains(word, StringComparer.OrdinalIgnoreCase)) return;
            _entries.Add(word);
        }

        _saveHelper.Schedule();
    }

    public void RemoveEntry(string word)
    {
        EnsureLoaded();
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Equals(word, StringComparison.OrdinalIgnoreCase));
        }

        _saveHelper.Schedule();
    }

    public string BuildPromptFragment()
    {
        EnsureLoaded();
        List<string> snapshot;
        lock (_lock) snapshot = [.. _entries];

        if (snapshot.Count == 0) return "";

        return $"\nIMPORTANT: Use these exact spellings when they appear in the transcription: {string.Join(", ", snapshot)}";
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
            var entries = JsonSerializer.Deserialize<List<string>>(json);
            if (entries is not null)
            {
                lock (_lock)
                {
                    _entries.Clear();
                    _entries.AddRange(entries);
                }
            }

            _loaded = true;
            _logger.LogInformation("Loaded {Count} custom dictionary entries", _entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load custom dictionary");
            _loaded = true;
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);

            List<string> snapshot;
            lock (_lock) snapshot = [.. _entries];

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
            _logger.LogDebug("Saved {Count} custom dictionary entries", snapshot.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save custom dictionary");
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        throw new InvalidOperationException(
            $"{nameof(DictionaryService)} not initialized. Call LoadAsync() at startup.");
    }
}
