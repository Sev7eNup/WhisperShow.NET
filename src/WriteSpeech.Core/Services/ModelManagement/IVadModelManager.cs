using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.ModelManagement;

public interface IVadModelManager
{
    VadModelInfo GetModel();
    bool IsModelDownloaded { get; }
    Task DownloadModelAsync(IProgress<float>? progress = null, CancellationToken cancellationToken = default);
    void DeleteModel();
    string ModelDirectory { get; }
}
