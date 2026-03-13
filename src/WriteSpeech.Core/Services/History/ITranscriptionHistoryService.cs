using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.History;

/// <summary>
/// Manages transcription history entries, persisted to a JSON file in AppData.
/// </summary>
public interface ITranscriptionHistoryService : IDisposable
{
    /// <summary>Returns all stored history entries.</summary>
    IReadOnlyList<TranscriptionHistoryEntry> GetEntries();
    /// <summary>Adds a new transcription entry to the history.</summary>
    /// <param name="sourceFilePath">Optional path to the source audio file for file-based transcriptions.</param>
    void AddEntry(string text, string provider, double durationSeconds, string? sourceFilePath = null);
    /// <summary>Removes a single entry from the history.</summary>
    void RemoveEntry(TranscriptionHistoryEntry entry);
    /// <summary>Removes all entries from the history.</summary>
    void Clear();
    /// <summary>Loads history entries from the persisted JSON file.</summary>
    Task LoadAsync();
    /// <summary>Saves the current history entries to the persisted JSON file.</summary>
    Task SaveAsync();
}
