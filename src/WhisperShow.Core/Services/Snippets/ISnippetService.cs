namespace WhisperShow.Core.Services.Snippets;

public record SnippetEntry(string Trigger, string Replacement);

public interface ISnippetService : IDisposable
{
    IReadOnlyList<SnippetEntry> GetSnippets();
    void AddSnippet(string trigger, string replacement);
    void UpdateSnippet(string oldTrigger, string newTrigger, string newReplacement);
    void RemoveSnippet(string trigger);
    string ApplySnippets(string text);
    Task LoadAsync();
    Task SaveAsync();
}
