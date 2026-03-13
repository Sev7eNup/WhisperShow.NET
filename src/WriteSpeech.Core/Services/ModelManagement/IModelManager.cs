using Whisper.net.Ggml;
using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.ModelManagement;

/// <summary>
/// Manages Whisper GGML model files — lists available and downloaded models,
/// downloads new models by size, and deletes existing ones.
/// </summary>
public interface IModelManager
{
    /// <summary>Gets the list of locally downloaded Whisper models.</summary>
    IReadOnlyList<WhisperModel> GetAvailableModels();

    /// <summary>Gets the list of all known Whisper models, both downloaded and not.</summary>
    IReadOnlyList<WhisperModel> GetAllModels();

    /// <summary>Downloads a Whisper model of the specified GGML type.</summary>
    Task DownloadModelAsync(GgmlType type, IProgress<float>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes the specified Whisper model file from disk.</summary>
    void DeleteModel(WhisperModel model);

    /// <summary>Gets the directory path where Whisper models are stored.</summary>
    string ModelDirectory { get; }
}
