using WhisperShow.Core.Services.TextInsertion;

namespace WhisperShow.App.Services;

public class WindowFocusService : IWindowFocusService
{
    public IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public async Task RestoreFocusAsync(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return;

        var foregroundThread = NativeMethods.GetWindowThreadProcessId(
            NativeMethods.GetForegroundWindow(), out _);
        var currentThread = NativeMethods.GetCurrentThreadId();

        if (foregroundThread != currentThread)
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

        NativeMethods.SetForegroundWindow(windowHandle);

        if (foregroundThread != currentThread)
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);

        await Task.Delay(150);
    }
}
