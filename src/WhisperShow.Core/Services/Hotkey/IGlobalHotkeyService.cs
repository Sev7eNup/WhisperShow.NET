namespace WhisperShow.Core.Services.Hotkey;

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;
    void Register(IntPtr windowHandle);
    void Unregister();
}
