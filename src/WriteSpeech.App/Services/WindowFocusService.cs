using System.Diagnostics;
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

    public async Task<bool> RestoreFocusAsync(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return false;

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

        NativeMethods.SetForegroundWindow(windowHandle);

        if (attached)
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);

        await Task.Delay(150);

        // Verify focus was restored
        if (NativeMethods.GetForegroundWindow() == windowHandle)
            return true;

        // Retry once
        _logger.LogDebug("Focus verification failed, retrying SetForegroundWindow for 0x{Handle:X}",
            windowHandle.ToInt64());
        NativeMethods.SetForegroundWindow(windowHandle);
        await Task.Delay(100);

        var success = NativeMethods.GetForegroundWindow() == windowHandle;
        if (!success)
            _logger.LogWarning("Failed to restore focus to window 0x{Handle:X} after retry",
                windowHandle.ToInt64());
        return success;
    }

    public string? GetProcessName(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return null;
        try
        {
            NativeMethods.GetWindowThreadProcessId(windowHandle, out var pid);
            if (pid == 0) return null;
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get process name for window 0x{Handle:X}", windowHandle.ToInt64());
            return null;
        }
    }
}
