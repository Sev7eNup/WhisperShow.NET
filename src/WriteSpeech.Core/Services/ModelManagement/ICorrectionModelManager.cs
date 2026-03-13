using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.ModelManagement;

/// <summary>
/// Manages GGUF correction model files used for local LLM-based text correction.
/// </summary>
public interface ICorrectionModelManager
{
    /// <summary>Gets the list of all known correction models, both downloaded and not.</summary>
    IReadOnlyList<CorrectionModelInfo> GetAllModels();

    /// <summary>Downloads a correction model by file name.</summary>
    Task DownloadModelAsync(string fileName, IProgress<float>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes the specified correction model file from disk.</summary>
    void DeleteModel(CorrectionModelInfo model);

    /// <summary>Gets the directory path where correction models are stored.</summary>
    string ModelDirectory { get; }
}
