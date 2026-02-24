using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.IDE;

public interface IIDEDetectionService
{
    IDEInfo? DetectIDE(IntPtr windowHandle);
}
