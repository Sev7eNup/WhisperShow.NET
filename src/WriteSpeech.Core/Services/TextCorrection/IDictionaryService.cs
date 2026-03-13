namespace WriteSpeech.Core.Services.TextCorrection;

/// <summary>
/// Manages a custom word dictionary whose entries are injected into correction prompts
/// to improve recognition of proper nouns, brand names, and technical terms.
/// </summary>
public interface IDictionaryService : IDisposable
{
    /// <summary>Gets all dictionary entries.</summary>
    IReadOnlyList<string> GetEntries();

    /// <summary>Adds a word to the dictionary.</summary>
    void AddEntry(string word);

    /// <summary>Removes a word from the dictionary.</summary>
    void RemoveEntry(string word);

    /// <summary>Builds a formatted string of dictionary entries for injection into correction prompts.</summary>
    string BuildPromptFragment();

    /// <summary>Loads the dictionary from its persisted JSON file.</summary>
    Task LoadAsync();

    /// <summary>Saves the dictionary to its persisted JSON file.</summary>
    Task SaveAsync();
}
