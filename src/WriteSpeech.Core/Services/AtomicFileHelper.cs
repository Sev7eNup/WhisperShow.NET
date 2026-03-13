using Microsoft.Extensions.Logging;

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

    /// <summary>
    /// Creates a timestamped .corrupt backup of a file that failed to deserialize,
    /// so the user doesn't silently lose data.
    /// </summary>
    public static void BackupCorruptFile(string filePath, ILogger logger)
    {
        try
        {
            var backupPath = filePath + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Copy(filePath, backupPath, overwrite: true);
            logger.LogWarning("Corrupt data file backed up to {BackupPath}", backupPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create backup of corrupt file {FilePath}", filePath);
        }
    }
}
