namespace WriteSpeech.Core.Services.Snippets;

/// <summary>A trigger-replacement pair for text substitution after transcription.</summary>
public record SnippetEntry(string Trigger, string Replacement);

/// <summary>
/// Manages trigger-to-replacement text substitutions applied after transcription.
/// Uses compiled regex with word boundary matching for accurate replacements.
/// </summary>
public interface ISnippetService : IDisposable
{
    /// <summary>Gets all registered snippet entries.</summary>
    IReadOnlyList<SnippetEntry> GetSnippets();

    /// <summary>Adds a new snippet with the specified trigger and replacement text.</summary>
    void AddSnippet(string trigger, string replacement);

    /// <summary>Updates an existing snippet, changing its trigger and/or replacement.</summary>
    void UpdateSnippet(string oldTrigger, string newTrigger, string newReplacement);

    /// <summary>Removes the snippet with the specified trigger.</summary>
    void RemoveSnippet(string trigger);

    /// <summary>Applies all registered snippet replacements to the given text.</summary>
    string ApplySnippets(string text);

    /// <summary>Loads snippets from the persisted JSON file.</summary>
    Task LoadAsync();

    /// <summary>Persists the current snippets to the JSON file.</summary>
    Task SaveAsync();
}
