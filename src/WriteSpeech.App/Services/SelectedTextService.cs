using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Services.TextInsertion;

namespace WriteSpeech.App.Services;

/// <summary>
/// Reads the currently selected text from the foreground window by simulating a Ctrl+C
/// keystroke and reading the result from the clipboard.
///
/// Implementation approach:
/// 1. Saves the current clipboard contents and clears the clipboard.
/// 2. Synthesizes Ctrl+C via Win32 <c>SendInput</c> to copy the selection in the active window.
/// 3. Waits for the copy to complete, then reads the clipboard text.
/// 4. Restores the original clipboard contents in a <c>finally</c> block.
///
/// If no text is selected, the clipboard remains empty after Ctrl+C and the method returns null.
/// All clipboard operations are dispatched to the WPF UI thread (STA requirement).
/// </summary>
public class SelectedTextService : ISelectedTextService
{
    private readonly ILogger<SelectedTextService> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectedTextService"/> class.
    /// </summary>
    public SelectedTextService(ILogger<SelectedTextService> logger, IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    /// <summary>
    /// Reads the currently selected text from the foreground window.
    /// Simulates Ctrl+C, reads the clipboard, and restores the original clipboard contents.
    /// Returns <c>null</c> if no text is selected or if clipboard access fails.
    /// </summary>
    /// <returns>The selected text, or <c>null</c> if no text is selected.</returns>
    public async Task<string?> ReadSelectedTextAsync()
    {
        string? selectedText = null;
        IDataObject? previousClipboard = null;
        var timing = _optionsMonitor.CurrentValue.Timing;

        try
        {
            // Save existing clipboard content, then clear
            bool clipboardCleared = false;
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    previousClipboard = Clipboard.GetDataObject();
                    Clipboard.Clear();
                    clipboardCleared = !Clipboard.ContainsText();
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Clipboard access failed during selected text capture"); }
            });

            if (!clipboardCleared)
            {
                _logger.LogWarning("Clipboard could not be cleared, skipping selected text detection");
                return null;
            }

            await Task.Delay(timing.PreCopyWaitMs);

            // Simulate Ctrl+C via SendInput
            var inputs = new NativeMethods.INPUT[4];
            int size = Marshal.SizeOf<NativeMethods.INPUT>();

            // Ctrl down
            inputs[0].Type = NativeMethods.INPUT_KEYBOARD;
            inputs[0].Union.Keyboard.VirtualKey = NativeMethods.VK_CONTROL;

            // C down
            inputs[1].Type = NativeMethods.INPUT_KEYBOARD;
            inputs[1].Union.Keyboard.VirtualKey = NativeMethods.VK_C;

            // C up
            inputs[2].Type = NativeMethods.INPUT_KEYBOARD;
            inputs[2].Union.Keyboard.VirtualKey = NativeMethods.VK_C;
            inputs[2].Union.Keyboard.Flags = NativeMethods.KEYEVENTF_KEYUP;

            // Ctrl up
            inputs[3].Type = NativeMethods.INPUT_KEYBOARD;
            inputs[3].Union.Keyboard.VirtualKey = NativeMethods.VK_CONTROL;
            inputs[3].Union.Keyboard.Flags = NativeMethods.KEYEVENTF_KEYUP;

            var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, size);
            if (sent != inputs.Length)
            {
                _logger.LogWarning("SendInput for Ctrl+C returned {Sent}/{Expected}", sent, inputs.Length);
                return null;
            }

            // Wait for the copy operation to complete
            await Task.Delay(timing.PasteCompletionMs);

            // Read the clipboard content
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                    selectedText = Clipboard.GetText();
            });

            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                _logger.LogInformation("Captured selected text ({Length} chars)", selectedText.Length);
            }
        }
        catch (COMException ex)
        {
            _logger.LogWarning(ex, "Failed to read selected text from clipboard");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error reading selected text");
        }
        finally
        {
            // Restore previous clipboard content
            if (previousClipboard is not null)
            {
                try
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                        Clipboard.SetDataObject(previousClipboard, copy: true));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not restore previous clipboard content");
                }
            }
        }

        return string.IsNullOrWhiteSpace(selectedText) ? null : selectedText;
    }
}
