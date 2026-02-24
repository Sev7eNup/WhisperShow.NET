using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WhisperShow.Core.Services;
using WhisperShow.Core.Services.Configuration;

namespace WhisperShow.App.Services;

public class SettingsPersistenceService : ISettingsPersistenceService, IDisposable
{
    private readonly ILogger<SettingsPersistenceService> _logger;
    private readonly string _filePath;
    private readonly Lock _lock = new();
    private Action<JsonNode>? _pendingMutator;
    private readonly DebouncedSaveHelper _saveHelper;

    public SettingsPersistenceService(ILogger<SettingsPersistenceService> logger)
        : this(logger, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json"), 300)
    {
    }

    internal SettingsPersistenceService(ILogger<SettingsPersistenceService> logger, string filePath, int debounceMs = 300)
    {
        _logger = logger;
        _filePath = filePath;
        _saveHelper = new DebouncedSaveHelper(FlushAsync, logger, debounceMs);
    }

    public void ScheduleUpdate(Action<JsonNode> mutator)
    {
        lock (_lock)
        {
            var previous = _pendingMutator;
            _pendingMutator = previous is null
                ? mutator
                : section => { previous(section); mutator(section); };
        }
        _saveHelper.Schedule();
    }

    private async Task FlushAsync()
    {
        Action<JsonNode> mutator;
        lock (_lock)
        {
            if (_pendingMutator is null) return;
            mutator = _pendingMutator;
            _pendingMutator = null;
        }

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip
        })!;

        var section = doc["WhisperShow"]!;
        mutator(section);

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(_filePath, doc.ToJsonString(options));
        _logger.LogInformation("Settings saved to appsettings.json");
    }

    public void Dispose() => _saveHelper.Dispose();
}
