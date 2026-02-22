using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using WhisperShow.Core.Services.TextInsertion;

namespace WhisperShow.App.Services;

public class TextInsertionService : ITextInsertionService
{
    private readonly ILogger<TextInsertionService> _logger;

    public TextInsertionService(ILogger<TextInsertionService> logger)
    {
        _logger = logger;
    }

    public async Task InsertTextAsync(string text)
    {
        _logger.LogInformation("Inserting text via clipboard ({Length} chars)", text.Length);

        try
        {
            // Set text to clipboard on the STA thread
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text));
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
    }
}
