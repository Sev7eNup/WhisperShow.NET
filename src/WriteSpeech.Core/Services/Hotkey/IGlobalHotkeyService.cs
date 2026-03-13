namespace WriteSpeech.Core.Services.Hotkey;

/// <summary>Carries the name of the mouse button that was captured during hotkey configuration.</summary>
public record MouseButtonCapturedEventArgs(string Button);

/// <summary>
/// Global hotkey service supporting keyboard and mouse button hotkeys.
/// Two implementations exist: Win32 RegisterHotKey API and low-level hooks,
/// switchable at runtime via <see cref="SwitchMethod"/>.
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>Raised when the toggle recording hotkey is pressed.</summary>
    event EventHandler? ToggleHotkeyPressed;
    /// <summary>Raised when the push-to-talk hotkey is pressed down.</summary>
    event EventHandler? PushToTalkHotkeyPressed;
    /// <summary>Raised when the push-to-talk hotkey is released.</summary>
    event EventHandler? PushToTalkHotkeyReleased;
    /// <summary>Raised when the Escape key is pressed.</summary>
    event EventHandler? EscapePressed;
    /// <summary>Raised when a mouse button is captured during hotkey configuration in settings.</summary>
    event EventHandler<MouseButtonCapturedEventArgs>? MouseButtonCaptured;
    /// <summary>Gets or sets whether hotkey actions are temporarily suppressed.</summary>
    bool SuppressActions { get; set; }
    /// <summary>Registers hotkeys using the given window handle for message processing.</summary>
    void Register(IntPtr windowHandle);
    /// <summary>Unregisters all active hotkeys.</summary>
    void Unregister();
    /// <summary>Updates the toggle recording hotkey binding (keyboard only).</summary>
    void UpdateToggleHotkey(string modifiers, string key);
    /// <summary>Updates the push-to-talk hotkey binding (keyboard only).</summary>
    void UpdatePushToTalkHotkey(string modifiers, string key);
    /// <summary>Updates the toggle recording hotkey binding with optional mouse button support.</summary>
    void UpdateToggleHotkey(string modifiers, string? key, string? mouseButton)
        => UpdateToggleHotkey(modifiers, key ?? "");
    /// <summary>Updates the push-to-talk hotkey binding with optional mouse button support.</summary>
    void UpdatePushToTalkHotkey(string modifiers, string? key, string? mouseButton)
        => UpdatePushToTalkHotkey(modifiers, key ?? "");
    /// <summary>Registers a global Escape key hook for cancelling recording.</summary>
    void RegisterEscapeHotkey();
    /// <summary>Unregisters the Escape key hook.</summary>
    void UnregisterEscapeHotkey();
    /// <summary>Hot-swaps between hotkey methods ("RegisterHotKey" or "LowLevelHook") at runtime.</summary>
    void SwitchMethod(string method) { }
}
