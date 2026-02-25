namespace WriteSpeech.Core.Services.Hotkey;

public record MouseButtonCapturedEventArgs(string Button);

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? ToggleHotkeyPressed;
    event EventHandler? PushToTalkHotkeyPressed;
    event EventHandler? PushToTalkHotkeyReleased;
    event EventHandler? EscapePressed;
    event EventHandler<MouseButtonCapturedEventArgs>? MouseButtonCaptured;
    bool SuppressActions { get; set; }
    void Register(IntPtr windowHandle);
    void Unregister();
    void UpdateToggleHotkey(string modifiers, string key);
    void UpdatePushToTalkHotkey(string modifiers, string key);
    void UpdateToggleHotkey(string modifiers, string? key, string? mouseButton)
        => UpdateToggleHotkey(modifiers, key ?? "");
    void UpdatePushToTalkHotkey(string modifiers, string? key, string? mouseButton)
        => UpdatePushToTalkHotkey(modifiers, key ?? "");
    void RegisterEscapeHotkey();
    void UnregisterEscapeHotkey();
    void SwitchMethod(string method) { }
}
