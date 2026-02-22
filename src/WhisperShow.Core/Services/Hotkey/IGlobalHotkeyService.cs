namespace WhisperShow.Core.Services.Hotkey;

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? ToggleHotkeyPressed;
    event EventHandler? PushToTalkHotkeyPressed;
    event EventHandler? PushToTalkHotkeyReleased;
    void Register(IntPtr windowHandle);
    void Unregister();
    void UpdateToggleHotkey(string modifiers, string key);
    void UpdatePushToTalkHotkey(string modifiers, string key);
}
