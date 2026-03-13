using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Configuration;

namespace WriteSpeech.App.Services;

/// <summary>
/// Centralized service for persisting application settings to <c>appsettings.json</c>.
///
/// Instead of each settings page writing to the file independently (which would cause
/// race conditions and partial writes), all setting changes go through <see cref="ScheduleUpdate"/>,
/// which accepts a <see cref="JsonNode"/> mutator function.
///
/// Key design: multiple <see cref="ScheduleUpdate"/> calls that arrive before the debounce
/// interval (300 ms) expires are composed into a single mutator function. When the debounce
/// fires, the service reads the current JSON file, applies all accumulated mutations to
/// the "WriteSpeech" section, and writes the result atomically (via <see cref="AtomicFileHelper"/>:
/// write to <c>.tmp</c> file, then rename). This atomic write prevents <c>FileSystemWatcher</c>
/// (used by <c>IOptionsMonitor</c>) from seeing truncated/empty files.
///
/// Thread safety: a <see cref="Lock"/> protects mutator composition, and a <see cref="SemaphoreSlim"/>
/// ensures only one flush operation runs at a time.
/// </summary>
public class SettingsPersistenceService : ISettingsPersistenceService, IDisposable
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private readonly ILogger<SettingsPersistenceService> _logger;
    private readonly string _filePath;
    private readonly Lock _mutatorLock = new();
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    private Action<JsonNode>? _pendingMutator;
    private readonly DebouncedSaveHelper _saveHelper;

    /// <summary>
    /// Initializes the service with the default <c>appsettings.json</c> path
    /// (in the application's base directory) and a 300 ms debounce interval.
    /// </summary>
    public SettingsPersistenceService(ILogger<SettingsPersistenceService> logger)
        : this(logger, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json"), 300)
    {
    }

    /// <summary>
    /// Initializes the service with a custom file path and debounce interval.
    /// Used by tests to point at a temporary file with a shorter debounce.
    /// </summary>
    internal SettingsPersistenceService(ILogger<SettingsPersistenceService> logger, string filePath, int debounceMs = 300)
    {
        _logger = logger;
        _filePath = filePath;
        _saveHelper = new DebouncedSaveHelper(FlushCoreAsync, logger, debounceMs);
    }

    /// <summary>
    /// Schedules a setting change by providing a mutator that modifies the "WriteSpeech"
    /// <see cref="JsonNode"/> section. Multiple calls before the debounce flush are composed —
    /// all mutators will be applied to the same JSON document in order.
    /// </summary>
    /// <param name="mutator">
    /// A function that receives the "WriteSpeech" JSON section and modifies it in place.
    /// Example: <c>node => node["Language"] = "de"</c>
    /// </param>
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

    /// <summary>
    /// Forces an immediate flush of any pending setting changes.
    /// Used during application shutdown or when settings must be persisted immediately.
    /// </summary>
    public Task FlushAsync() => _saveHelper.FlushAsync();

    /// <summary>
    /// Synchronously flushes pending changes and disposes the debounce helper and semaphore.
    /// </summary>
    public void Dispose()
    {
        _saveHelper.FlushSync();
        _saveHelper.Dispose();
        _flushSemaphore.Dispose();
    }
}
