namespace WhisperShow.Core.Services.TextInsertion;

public interface IWindowFocusService
{
    IntPtr GetForegroundWindow();
    Task RestoreFocusAsync(IntPtr windowHandle);
}
