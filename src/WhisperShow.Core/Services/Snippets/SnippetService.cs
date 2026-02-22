using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace WhisperShow.Core.Services.Snippets;

public class SnippetService : ISnippetService
{
    private readonly ILogger<SnippetService> _logger;
    private readonly string _filePath;
    private readonly List<SnippetEntry> _snippets = [];
    private readonly Lock _lock = new();
    private bool _loaded;

    public SnippetService(ILogger<SnippetService> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperShow", "snippets.json");
    }

    public IReadOnlyList<SnippetEntry> GetSnippets()
    {
        EnsureLoaded();
        lock (_lock) return _snippets.ToList();
    }

    public void AddSnippet(string trigger, string replacement)
    {
        if (string.IsNullOrWhiteSpace(trigger) || string.IsNullOrWhiteSpace(replacement)) return;
        trigger = trigger.Trim();
        replacement = replacement.Trim();

        EnsureLoaded();
        lock (_lock)
        {
            if (_snippets.Any(s => s.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase))) return;
            _snippets.Add(new SnippetEntry(trigger, replacement));
        }

        _ = SaveAsync();
    }

    public void RemoveSnippet(string trigger)
    {
        EnsureLoaded();
        lock (_lock)
        {
            _snippets.RemoveAll(s => s.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
        }

        _ = SaveAsync();
    }

    public string ApplySnippets(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        EnsureLoaded();
        List<SnippetEntry> snapshot;
        lock (_lock) snapshot = [.. _snippets];

        if (snapshot.Count == 0) return text;

        foreach (var snippet in snapshot)
        {
            var pattern = @"\b" + Regex.Escape(snippet.Trigger) + @"\b";
            text = Regex.Replace(text, pattern, snippet.Replacement, RegexOptions.IgnoreCase);
        }

        return text;
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
            var snippets = JsonSerializer.Deserialize<List<SnippetEntry>>(json, JsonOptions);
            if (snippets is not null)
            {
                lock (_lock)
                {
                    _snippets.Clear();
                    _snippets.AddRange(snippets);
                }
            }

            _loaded = true;
            _logger.LogInformation("Loaded {Count} snippets", _snippets.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load snippets");
            _loaded = true;
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);

            List<SnippetEntry> snapshot;
            lock (_lock) snapshot = [.. _snippets];

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
            _logger.LogDebug("Saved {Count} snippets", snapshot.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save snippets");
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        LoadAsync().GetAwaiter().GetResult();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
