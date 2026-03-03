namespace WriteSpeech.Core.Services;

/// <summary>
/// Writes file content atomically by first writing to a .tmp file, then renaming.
/// Prevents data corruption if the process crashes or power is lost mid-write.
/// </summary>
public static class AtomicFileHelper
{
    public static async Task WriteAllTextAsync(string filePath, string content)
    {
        var tempPath = filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, content).ConfigureAwait(false);
        File.Move(tempPath, filePath, overwrite: true);
    }
}
