using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.IDE;

/// <summary>
/// Detects the active IDE (VS Code, Cursor, Windsurf) from a window handle
/// by inspecting the process name and window title.
/// </summary>
public interface IIDEDetectionService
{
    /// <summary>Returns IDE information for the given window, or null if no supported IDE is detected.</summary>
    IDEInfo? DetectIDE(IntPtr windowHandle);
}
