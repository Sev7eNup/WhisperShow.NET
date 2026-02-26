using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.ModelManagement;

public interface IParakeetModelManager
{
    IReadOnlyList<ParakeetModelInfo> GetAllModels();
    IReadOnlyList<ParakeetModelInfo> GetAvailableModels();
    Task DownloadModelAsync(string modelName, IProgress<float>? progress = null, CancellationToken cancellationToken = default);
    void DeleteModel(ParakeetModelInfo model);
    string ModelDirectory { get; }
}
