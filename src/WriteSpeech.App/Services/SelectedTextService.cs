using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using WriteSpeech.Core.Services.TextInsertion;

namespace WriteSpeech.App.Services;

public class SelectedTextService : ISelectedTextService
{
    private readonly ILogger<SelectedTextService> _logger;

    public SelectedTextService(ILogger<SelectedTextService> logger)
    {
        _logger = logger;
    }

    public async Task<string?> ReadSelectedTextAsync()
    {
        string? selectedText = null;
        IDataObject? previousClipboard = null;

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

            await Task.Delay(30);

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
            await Task.Delay(100);

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
