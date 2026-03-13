using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.ModelManagement;

/// <summary>
/// Manages NVIDIA Parakeet model directories, each containing encoder/decoder/joiner ONNX files
/// and a tokens file. Models are downloaded from HuggingFace.
/// </summary>
public interface IParakeetModelManager
{
    /// <summary>Gets the list of all known Parakeet models, both downloaded and not.</summary>
    IReadOnlyList<ParakeetModelInfo> GetAllModels();

    /// <summary>Gets the list of locally downloaded Parakeet models with all required files present.</summary>
    IReadOnlyList<ParakeetModelInfo> GetAvailableModels();

    /// <summary>Downloads a Parakeet model by name, fetching all required files.</summary>
    Task DownloadModelAsync(string modelName, IProgress<float>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes the specified Parakeet model directory from disk.</summary>
    void DeleteModel(ParakeetModelInfo model);

    /// <summary>Gets the directory path where Parakeet models are stored.</summary>
    string ModelDirectory { get; }
}
