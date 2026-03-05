using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Services.TextInsertion;

namespace WriteSpeech.App.Services;

/// <summary>
/// Manages window focus using Win32 P/Invoke APIs. Used to capture the foreground window
/// before recording starts and to restore focus to it after transcribed text is inserted.
///
/// Key Win32 detail: <c>SetForegroundWindow</c> is restricted by Windows — a process can
/// only set the foreground window if it already owns the foreground. To work around this,
/// <see cref="RestoreFocusAsync"/> uses <c>AttachThreadInput</c> to temporarily attach the
/// calling thread's input queue to the foreground thread's input queue. This gives the caller
/// the privileges needed to call <c>SetForegroundWindow</c> successfully. The attachment is
/// always detached after the call, regardless of success.
/// </summary>
public class WindowFocusService : IWindowFocusService
{
    private readonly ILogger<WindowFocusService> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowFocusService"/> class.
    /// </summary>
    public WindowFocusService(ILogger<WindowFocusService> logger, IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    /// <summary>
    /// Returns the native window handle (HWND) of the current foreground window
    /// via Win32 <c>GetForegroundWindow</c>.
    /// </summary>
    public IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    /// <summary>
    /// Restores keyboard focus to the specified window. Uses <c>AttachThreadInput</c> to
    /// temporarily share the input queue with the current foreground thread, allowing
    /// <c>SetForegroundWindow</c> to succeed even when the calling process does not own
    /// the foreground. Retries once after a 150 ms delay if the initial attempt fails.
    /// </summary>
    /// <param name="windowHandle">The HWND of the window to bring to the foreground.</param>
    /// <returns><c>true</c> if the window is now in the foreground; <c>false</c> otherwise.</returns>
    public async Task<bool> RestoreFocusAsync(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || !NativeMethods.IsWindow(windowHandle)) return false;
        var timing = _optionsMonitor.CurrentValue.Timing;

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

        await Task.Delay(timing.FocusRestoreMs);

        // Verify focus was restored
        if (NativeMethods.GetForegroundWindow() == windowHandle)
            return true;

        // Retry once
        _logger.LogDebug("Focus verification failed, retrying SetForegroundWindow for 0x{Handle:X}",
            windowHandle.ToInt64());
        NativeMethods.SetForegroundWindow(windowHandle);
        await Task.Delay(timing.FocusRetryMs);

        var success = NativeMethods.GetForegroundWindow() == windowHandle;
        if (!success)
            _logger.LogWarning("Failed to restore focus to window 0x{Handle:X} after retry",
                windowHandle.ToInt64());
        return success;
    }

    /// <summary>
    /// Retrieves the process name (e.g., "Code", "Slack") of the application that owns
    /// the specified window handle. Used for IDE detection and correction mode auto-switching.
    /// Returns <c>null</c> if the handle is invalid or the process has exited.
    /// </summary>
    /// <param name="windowHandle">The HWND of the window to inspect.</param>
    /// <returns>The process name without extension, or <c>null</c> on failure.</returns>
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
