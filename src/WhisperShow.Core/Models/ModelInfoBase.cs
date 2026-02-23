namespace WhisperShow.Core.Models;

public abstract class ModelInfoBase
{
    public required string Name { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
    public string? FilePath { get; init; }
    public bool IsDownloaded => FilePath is not null && File.Exists(FilePath);

    public string SizeDisplay => SizeBytes switch
    {
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F0} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):F0} MB",
        _ => $"{SizeBytes / (1024.0 * 1024 * 1024):F1} GB"
    };
}
