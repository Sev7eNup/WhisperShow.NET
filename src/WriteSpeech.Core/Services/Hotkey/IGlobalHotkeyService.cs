namespace WriteSpeech.Core.Services.Hotkey;

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? ToggleHotkeyPressed;
    event EventHandler? PushToTalkHotkeyPressed;
    event EventHandler? PushToTalkHotkeyReleased;
    event EventHandler? EscapePressed;
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
