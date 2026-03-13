using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;

namespace WriteSpeech.Core.Services.Modes;

public class ModeService : IModeService
{
    public event Action? ModesChanged;

    private readonly ILogger<ModeService> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly string _filePath;
    private readonly List<CorrectionMode> _modes = [];
    private readonly Lock _lock = new();
    private readonly DebouncedSaveHelper _saveHelper;
    private bool _loaded;
    private string? _activeModeName;
    private bool _autoSwitchEnabled;

    public ModeService(ILogger<ModeService> logger, IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WriteSpeech", "modes.json");
        _saveHelper = new DebouncedSaveHelper(SaveAsync, logger, 300);

        var options = optionsMonitor.CurrentValue;
        _activeModeName = options.TextCorrection.ActiveMode;
        _autoSwitchEnabled = options.TextCorrection.AutoSwitchMode;
    }

    public string? ActiveModeName
    {
        get { lock (_lock) return _activeModeName; }
    }

    public bool AutoSwitchEnabled
    {
        get { lock (_lock) return _autoSwitchEnabled; }
        set { lock (_lock) _autoSwitchEnabled = value; }
    }

    public IReadOnlyList<CorrectionMode> GetModes()
    {
        EnsureLoaded();
        lock (_lock) return _modes.ToList();
    }

    public void AddMode(string name, string systemPrompt, IReadOnlyList<string> appPatterns, string? targetLanguage = null)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(systemPrompt)) return;
        name = name.Trim();

        EnsureLoaded();
        lock (_lock)
        {
            if (_modes.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
            _modes.Add(new CorrectionMode(name, systemPrompt.Trim(), appPatterns, IsBuiltIn: false, TargetLanguage: targetLanguage));
        }

        _saveHelper.Schedule();
        ModesChanged?.Invoke();
    }

    public void UpdateMode(string oldName, string newName, string systemPrompt, IReadOnlyList<string> appPatterns, string? targetLanguage = null)
    {
        if (string.IsNullOrWhiteSpace(newName) || string.IsNullOrWhiteSpace(systemPrompt)) return;
        newName = newName.Trim();

        EnsureLoaded();
        lock (_lock)
        {
            var index = _modes.FindIndex(m => m.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;

            var existing = _modes[index];

            // Don't allow renaming to conflict with another mode
            if (!oldName.Equals(newName, StringComparison.OrdinalIgnoreCase)
                && _modes.Any(m => m.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                return;

            _modes[index] = new CorrectionMode(
                existing.IsBuiltIn ? existing.Name : newName,
                systemPrompt.Trim(),
                appPatterns,
                existing.IsBuiltIn,
                targetLanguage);

            // Update active mode name if it was renamed
            if (_activeModeName is not null
                && _activeModeName.Equals(oldName, StringComparison.OrdinalIgnoreCase)
                && !existing.IsBuiltIn)
                _activeModeName = newName;
        }

        _saveHelper.Schedule();
        ModesChanged?.Invoke();
    }

    public void RemoveMode(string name)
    {
        EnsureLoaded();
        lock (_lock)
        {
            var index = _modes.FindIndex(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            if (_modes[index].IsBuiltIn) return; // Cannot remove built-in modes

            _modes.RemoveAt(index);

            // Reset active mode if the removed mode was active
            if (_activeModeName is not null && _activeModeName.Equals(name, StringComparison.OrdinalIgnoreCase))
                _activeModeName = null;
        }

        _saveHelper.Schedule();
        ModesChanged?.Invoke();
    }

    public void SetActiveMode(string? name)
    {
        lock (_lock) _activeModeName = name;
    }

    public string? ResolveSystemPrompt(string? processName)
    {
        var mode = ResolveMode(processName);
        if (mode is null || mode.Name == CorrectionModeDefaults.DefaultModeName)
            return null; // Let services use their own default
        return mode.SystemPrompt;
    }

    public string? ResolveCombinedSystemPrompt(string? processName)
    {
        // For combined audio model, use the same mode prompt
        // The combined service will use its own default if null is returned
        return ResolveSystemPrompt(processName);
    }

    public string? ResolveTargetLanguage(string? processName)
    {
        var mode = ResolveMode(processName);
        return mode?.TargetLanguage;
    }

    public async Task LoadAsync()
    {
        try
        {
            // Start with built-in modes
            lock (_lock)
            {
                _modes.Clear();
                _modes.AddRange(CorrectionModeDefaults.BuiltInModes);
            }

            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
                var saved = JsonSerializer.Deserialize<ModeFileData>(json, JsonOptions);
                if (saved?.Modes is not null)
                {
                    lock (_lock)
                    {
                        foreach (var savedMode in saved.Modes)
                        {
                            var builtInIndex = _modes.FindIndex(m =>
                                m.IsBuiltIn && m.Name.Equals(savedMode.Name, StringComparison.OrdinalIgnoreCase));

                            if (builtInIndex >= 0)
                            {
                                // Override built-in mode's prompt and patterns
                                var existing = _modes[builtInIndex];
                                _modes[builtInIndex] = existing with
                                {
                                    SystemPrompt = savedMode.SystemPrompt,
                                    AppPatterns = savedMode.AppPatterns ?? existing.AppPatterns,
                                    TargetLanguage = savedMode.TargetLanguage ?? existing.TargetLanguage
                                };
                            }
                            else
                            {
                                // Add custom mode
                                _modes.Add(new CorrectionMode(
                                    savedMode.Name,
                                    savedMode.SystemPrompt,
                                    savedMode.AppPatterns ?? [],
                                    IsBuiltIn: false,
                                    TargetLanguage: savedMode.TargetLanguage));
                            }
                        }
                    }
                }
            }

            _loaded = true;
            _logger.LogInformation("Loaded {Count} correction modes", _modes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load correction modes — starting with defaults");
            if (File.Exists(_filePath))
                AtomicFileHelper.BackupCorruptFile(_filePath, _logger);
            _loaded = true;
        }
    }

    public void Dispose()
    {
        _saveHelper.FlushSync();
        _saveHelper.Dispose();
    }

    private CorrectionMode? ResolveMode(string? processName)
    {
        EnsureLoaded();
        lock (_lock)
        {
            // Auto-switch: match process name against app patterns
            if (_autoSwitchEnabled && !string.IsNullOrEmpty(processName))
            {
                foreach (var mode in _modes)
                {
                    if (mode.AppPatterns.Any(p =>
                        p.Equals(processName, StringComparison.OrdinalIgnoreCase)))
                        return mode;
                }
            }

            // Pinned mode or default
            if (_activeModeName is not null)
            {
                var pinned = _modes.FirstOrDefault(m =>
                    m.Name.Equals(_activeModeName, StringComparison.OrdinalIgnoreCase));
                if (pinned is not null) return pinned;
            }

            // Fall back to Default mode
            return _modes.FirstOrDefault(m => m.Name == CorrectionModeDefaults.DefaultModeName);
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);

            List<SavedMode> toSave;
            lock (_lock)
            {
                // Save modified built-in modes and all custom modes
                toSave = _modes
                    .Where(m => !m.IsBuiltIn || HasBeenModified(m))
                    .Select(m => new SavedMode(m.Name, m.SystemPrompt, m.AppPatterns.ToList(), m.TargetLanguage))
                    .ToList();
            }

            var data = new ModeFileData(toSave);
            var json = JsonSerializer.Serialize(data, JsonOptions);
            await AtomicFileHelper.WriteAllTextAsync(_filePath, json);
            _logger.LogDebug("Saved {Count} correction modes", toSave.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save correction modes");
        }
    }

    private static bool HasBeenModified(CorrectionMode mode)
    {
        var builtIn = CorrectionModeDefaults.BuiltInModes
            .FirstOrDefault(b => b.Name.Equals(mode.Name, StringComparison.OrdinalIgnoreCase));
        if (builtIn is null) return true;
        return builtIn.SystemPrompt != mode.SystemPrompt
            || !builtIn.AppPatterns.SequenceEqual(mode.AppPatterns)
            || builtIn.TargetLanguage != mode.TargetLanguage;
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        throw new InvalidOperationException(
            $"{nameof(ModeService)} not initialized. Call LoadAsync() at startup.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private record SavedMode(string Name, string SystemPrompt, List<string>? AppPatterns, string? TargetLanguage = null);
    private record ModeFileData(List<SavedMode> Modes);
}
