namespace WriteSpeech.Core.Models;

/// <summary>
/// Abstract base class representing metadata for a downloadable AI model.
/// Subclasses describe specific model types (Whisper GGML, GGUF correction models,
/// Parakeet ONNX models, Silero VAD). Used by model management UI to display
/// available models, track download status, and show file sizes.
/// </summary>
public abstract class ModelInfoBase
{
    /// <summary>Human-readable display name of the model (e.g., "Small", "Medium", "Large").</summary>
    public required string Name { get; init; }

    /// <summary>The filename (or directory name) used to store the model on disk.</summary>
    public required string FileName { get; init; }

    /// <summary>Total size of the model download in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Absolute path to the downloaded model on disk, or <c>null</c> if the model has not been downloaded yet.</summary>
    public string? FilePath { get; init; }

    /// <summary>Whether the model file exists at <see cref="FilePath"/>.</summary>
    public bool IsDownloaded => FilePath is not null && File.Exists(FilePath);

    /// <summary>Human-readable size string (e.g., "141 MB", "1.5 GB"), automatically scaled to the appropriate unit.</summary>
    public string SizeDisplay => SizeBytes switch
    {
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F0} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):F0} MB",
        _ => $"{SizeBytes / (1024.0 * 1024 * 1024):F1} GB"
    };
}
