using System.Runtime.InteropServices;
using System.Windows;
using WhisperShow.Core.Services.TextInsertion;

namespace WhisperShow.App.Services;

public class TextInsertionService : ITextInsertionService
{
    public async Task InsertTextAsync(string text)
    {
        // Set text to clipboard on the STA thread
        Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text));

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

        NativeMethods.SendInput((uint)inputs.Length, inputs, size);
    }
}
