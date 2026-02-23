namespace WhisperShow.Core.Services.TextCorrection;

public interface IDictionaryService : IDisposable
{
    IReadOnlyList<string> GetEntries();
    void AddEntry(string word);
    void RemoveEntry(string word);
    string BuildPromptFragment();
    Task LoadAsync();
    Task SaveAsync();
}
