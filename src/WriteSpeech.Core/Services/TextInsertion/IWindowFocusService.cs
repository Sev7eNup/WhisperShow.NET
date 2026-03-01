namespace WriteSpeech.Core.Services.TextInsertion;

public interface IWindowFocusService
{
    IntPtr GetForegroundWindow();
    Task<bool> RestoreFocusAsync(IntPtr windowHandle);
    string? GetProcessName(IntPtr windowHandle);
}
