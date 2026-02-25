using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.Modes;

public interface IModeService : IDisposable
{
    event Action? ModesChanged;
    IReadOnlyList<CorrectionMode> GetModes();
    string? ActiveModeName { get; }
    bool AutoSwitchEnabled { get; set; }

    void AddMode(string name, string systemPrompt, IReadOnlyList<string> appPatterns);
    void UpdateMode(string oldName, string newName, string systemPrompt, IReadOnlyList<string> appPatterns);
    void RemoveMode(string name);
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

    Task LoadAsync();
}
