namespace WriteSpeech.Core.Services.TextInsertion;

/// <summary>
/// Reads the currently selected text from the foreground window.
/// </summary>
public interface ISelectedTextService
{
    /// <summary>
    /// Simulates Ctrl+C to copy the selected text and reads it from the clipboard.
    /// Returns null if no text is selected.
    /// </summary>
    Task<string?> ReadSelectedTextAsync();
}
