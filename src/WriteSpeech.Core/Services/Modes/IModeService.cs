using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.Modes;

/// <summary>
/// Manages context-aware correction modes with different system prompts
/// based on the active application. Supports built-in and custom modes
/// with auto-switching by foreground process name.
/// </summary>
public interface IModeService : IDisposable
{
    /// <summary>Raised when the modes collection or active mode changes.</summary>
    event Action? ModesChanged;
    /// <summary>Returns all available correction modes (built-in and custom).</summary>
    IReadOnlyList<CorrectionMode> GetModes();
    /// <summary>Gets the name of the currently pinned mode, or null for auto-switch.</summary>
    string? ActiveModeName { get; }
    /// <summary>Gets or sets whether mode auto-switching by active application is enabled.</summary>
    bool AutoSwitchEnabled { get; set; }

    /// <summary>Adds a new custom correction mode.</summary>
    void AddMode(string name, string systemPrompt, IReadOnlyList<string> appPatterns, string? targetLanguage = null);
    /// <summary>Updates an existing custom correction mode.</summary>
    void UpdateMode(string oldName, string newName, string systemPrompt, IReadOnlyList<string> appPatterns, string? targetLanguage = null);
    /// <summary>Removes a custom correction mode by name.</summary>
    void RemoveMode(string name);
    /// <summary>Pins a specific mode by name, or null to use auto-switch.</summary>
    void SetActiveMode(string? name);

    /// <summary>
    /// Returns the effective system prompt for text correction based on the active process.
    /// Returns null when the Default mode is active (services should use their own default).
    /// </summary>
    string? ResolveSystemPrompt(string? processName);

    /// <summary>
    /// Returns the effective system prompt for the combined audio model.
    /// Returns null when the Default mode is active.
    /// </summary>
    string? ResolveCombinedSystemPrompt(string? processName);

    /// <summary>
    /// Returns the target language for translation if the resolved mode has one configured.
    /// Returns null when no translation is needed.
    /// </summary>
    string? ResolveTargetLanguage(string? processName);

    /// <summary>Loads modes from the persisted JSON file in AppData.</summary>
    Task LoadAsync();
}
