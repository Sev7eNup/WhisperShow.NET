using Microsoft.Extensions.Logging;
using WriteSpeech.Core.Services.TextInsertion;

namespace WriteSpeech.App.Services;

public class WindowFocusService : IWindowFocusService
{
    private readonly ILogger<WindowFocusService> _logger;

    public WindowFocusService(ILogger<WindowFocusService> logger)
    {
        _logger = logger;
    }

    public IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public async Task RestoreFocusAsync(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return;

        var foregroundThread = NativeMethods.GetWindowThreadProcessId(
            NativeMethods.GetForegroundWindow(), out _);
        var currentThread = NativeMethods.GetCurrentThreadId();

        bool attached = false;
        if (foregroundThread != currentThread)
        {
            attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
            if (!attached)
                _logger.LogDebug("AttachThreadInput failed for threads {Current} -> {Foreground}",
                    currentThread, foregroundThread);
        }

        if (!NativeMethods.SetForegroundWindow(windowHandle))
            _logger.LogDebug("SetForegroundWindow failed for handle 0x{Handle:X}", windowHandle.ToInt64());

        if (attached)
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);

        await Task.Delay(150);
    }
}
