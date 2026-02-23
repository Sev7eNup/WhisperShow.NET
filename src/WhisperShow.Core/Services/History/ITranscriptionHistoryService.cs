using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.History;

public interface ITranscriptionHistoryService : IDisposable
{
    IReadOnlyList<TranscriptionHistoryEntry> GetEntries();
    void AddEntry(string text, string provider, double durationSeconds);
    void RemoveEntry(TranscriptionHistoryEntry entry);
    void Clear();
    Task LoadAsync();
    Task SaveAsync();
}
