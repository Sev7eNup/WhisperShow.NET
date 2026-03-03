using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Configuration;

namespace WriteSpeech.App.Services;

public class SettingsPersistenceService : ISettingsPersistenceService, IDisposable
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private readonly ILogger<SettingsPersistenceService> _logger;
    private readonly string _filePath;
    private readonly Lock _mutatorLock = new();
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
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
        _saveHelper = new DebouncedSaveHelper(FlushCoreAsync, logger, debounceMs);
    }

    public void ScheduleUpdate(Action<JsonNode> mutator)
    {
        lock (_mutatorLock)
        {
            var previous = _pendingMutator;
            _pendingMutator = previous is null
                ? mutator
                : section => { previous(section); mutator(section); };
        }
        _saveHelper.Schedule();
    }

    private async Task FlushCoreAsync()
    {
        await _flushSemaphore.WaitAsync();
        try
        {
            Action<JsonNode> mutator;
            lock (_mutatorLock)
            {
                if (_pendingMutator is null) return;
                mutator = _pendingMutator;
                _pendingMutator = null;
            }

            var json = await File.ReadAllTextAsync(_filePath);
            var doc = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip
            });

            if (doc is null)
            {
                _logger.LogError("Failed to parse appsettings.json — skipping save");
                return;
            }

            var section = doc["WriteSpeech"];
            if (section is null)
            {
                _logger.LogError("appsettings.json is missing the 'WriteSpeech' section — skipping save");
                return;
            }

            mutator(section);

            // Write atomically: temp file + rename to avoid FileSystemWatcher seeing truncated data
            await AtomicFileHelper.WriteAllTextAsync(_filePath, doc.ToJsonString(s_jsonOptions));
            _logger.LogInformation("Settings saved to appsettings.json");
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }

    public Task FlushAsync() => _saveHelper.FlushAsync();

    public void Dispose()
    {
        _saveHelper.FlushSync();
        _saveHelper.Dispose();
        _flushSemaphore.Dispose();
    }
}
