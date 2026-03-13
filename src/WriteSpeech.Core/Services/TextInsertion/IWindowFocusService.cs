namespace WriteSpeech.Core.Services.TextInsertion;

/// <summary>
/// Manages window focus operations — retrieves the foreground window handle,
/// restores focus to a previous window, and resolves process names from window handles.
/// </summary>
public interface IWindowFocusService
{
    /// <summary>Gets the handle of the currently active foreground window.</summary>
    IntPtr GetForegroundWindow();

    /// <summary>Restores focus to the window identified by the given handle using SetForegroundWindow and AttachThreadInput.</summary>
    /// <returns>True if focus was successfully restored.</returns>
    Task<bool> RestoreFocusAsync(IntPtr windowHandle);

    /// <summary>Gets the process name associated with the given window handle.</summary>
    string? GetProcessName(IntPtr windowHandle);
}
