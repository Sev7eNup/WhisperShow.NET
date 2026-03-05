using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using WriteSpeech.Core.Services.TextInsertion;

namespace WriteSpeech.App.Services;

/// <summary>
/// Inserts text at the current cursor position in any Windows application by leveraging
/// the system clipboard and simulated keystrokes.
///
/// Implementation approach:
/// 1. Saves the current clipboard contents (so the user's clipboard is not destroyed).
/// 2. Places the transcribed text onto the clipboard via <see cref="Clipboard.SetText"/>.
/// 3. Waits briefly (50 ms) for the clipboard to settle — some applications poll the clipboard asynchronously.
/// 4. Synthesizes a Ctrl+V keystroke sequence using the Win32 <c>SendInput</c> API, which injects
///    hardware-level keyboard events into the input queue of the foreground window.
/// 5. Waits again (100 ms) for the target application to process the paste.
/// 6. Restores the original clipboard contents in a <c>finally</c> block to guarantee cleanup
///    even if an exception occurs.
///
/// All clipboard operations are dispatched to the WPF UI thread because the clipboard is
/// per-thread (STA) and must be accessed from the thread that owns it.
/// </summary>
public class TextInsertionService : ITextInsertionService
{
    private readonly ILogger<TextInsertionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextInsertionService"/> class.
    /// </summary>
    public TextInsertionService(ILogger<TextInsertionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Inserts the specified text at the current cursor position in the foreground window.
    /// The text is placed on the clipboard and pasted via a simulated Ctrl+V keystroke.
    /// The previous clipboard contents are saved before the operation and restored afterward
    /// in a <c>finally</c> block, so the user's clipboard is not permanently modified.
    /// </summary>
    /// <param name="text">The text to insert. Must not be null.</param>
    public async Task InsertTextAsync(string text)
    {
        _logger.LogInformation("Inserting text via clipboard ({Length} chars)", text.Length);

        IDataObject? previousClipboard = null;

        try
        {
            try
            {
                // Save and restore clipboard on the STA thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try { previousClipboard = Clipboard.GetDataObject(); } catch (Exception ex) { _logger.LogDebug(ex, "Clipboard read failed before text insertion"); }
                    Clipboard.SetText(text);
                });
            }
            catch (COMException ex)
            {
                _logger.LogError(ex, "Failed to set clipboard text");
                throw;
            }

            // Brief delay for clipboard to settle
            await Task.Delay(50);

            // Simulate Ctrl+V
            var inputs = new NativeMethods.INPUT[4];
            int size = Marshal.SizeOf<NativeMethods.INPUT>();

            // Ctrl down
            inputs[0].Type = NativeMethods.INPUT_KEYBOARD;
            inputs[0].Union.Keyboard.VirtualKey = NativeMethods.VK_CONTROL;

            // V down
            inputs[1].Type = NativeMethods.INPUT_KEYBOARD;
            inputs[1].Union.Keyboard.VirtualKey = NativeMethods.VK_V;

            // V up
            inputs[2].Type = NativeMethods.INPUT_KEYBOARD;
            inputs[2].Union.Keyboard.VirtualKey = NativeMethods.VK_V;
            inputs[2].Union.Keyboard.Flags = NativeMethods.KEYEVENTF_KEYUP;

            // Ctrl up
            inputs[3].Type = NativeMethods.INPUT_KEYBOARD;
            inputs[3].Union.Keyboard.VirtualKey = NativeMethods.VK_CONTROL;
            inputs[3].Union.Keyboard.Flags = NativeMethods.KEYEVENTF_KEYUP;

            var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, size);
            if (sent != inputs.Length)
            {
                _logger.LogWarning("SendInput returned {Sent}/{Expected} — keystrokes may not have been delivered",
                    sent, inputs.Length);
            }

            // Wait for the paste to complete before restoring (shorter = less clipboard exposure)
            await Task.Delay(100);
        }
        finally
        {
            // Always restore previous clipboard content, even if SendInput or delays fail
            if (previousClipboard is not null)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() => Clipboard.SetDataObject(previousClipboard, copy: true));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not restore previous clipboard content");
                }
            }
        }
    }
}
