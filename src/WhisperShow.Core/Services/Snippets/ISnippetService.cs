namespace WhisperShow.Core.Services.Snippets;

public record SnippetEntry(string Trigger, string Replacement);

public interface ISnippetService
{
    IReadOnlyList<SnippetEntry> GetSnippets();
    void AddSnippet(string trigger, string replacement);
    void RemoveSnippet(string trigger);
    string ApplySnippets(string text);
    Task LoadAsync();
    Task SaveAsync();
}
