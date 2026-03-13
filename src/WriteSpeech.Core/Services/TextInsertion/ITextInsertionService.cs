namespace WriteSpeech.Core.Services.TextInsertion;

/// <summary>
/// Inserts transcribed text at the cursor position in the previously active window
/// via clipboard and simulated Ctrl+V keystroke.
/// </summary>
public interface ITextInsertionService
{
    /// <summary>Inserts the specified text at the current cursor position.</summary>
    Task InsertTextAsync(string text);
}
