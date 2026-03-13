using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.ModelManagement;

/// <summary>
/// Manages the Silero VAD (Voice Activity Detection) model file,
/// downloaded from GitHub releases.
/// </summary>
public interface IVadModelManager
{
    /// <summary>Gets the VAD model info, including download status and file path.</summary>
    VadModelInfo GetModel();

    /// <summary>Gets whether the VAD model file exists on disk.</summary>
    bool IsModelDownloaded { get; }

    /// <summary>Downloads the Silero VAD model file.</summary>
    Task DownloadModelAsync(IProgress<float>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes the VAD model file from disk.</summary>
    void DeleteModel();

    /// <summary>Gets the directory path where the VAD model is stored.</summary>
    string ModelDirectory { get; }
}
