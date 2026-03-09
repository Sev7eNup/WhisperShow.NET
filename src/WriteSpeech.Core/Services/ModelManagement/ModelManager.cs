using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net.Ggml;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.ModelManagement;

public class ModelManager : IModelManager
{
    private readonly ILogger<ModelManager> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly ModelDownloadHelper _downloadHelper;

    // Hashes set to null: Whisper.net controls the upstream model URLs and may update
    // files at any time, breaking hardcoded hashes. Integrity is ensured by HTTPS transport.
    private static readonly (GgmlType Type, string Name, string FileName, long SizeBytes, string? Sha256)[] KnownModels =
    [
        (GgmlType.Tiny, "Tiny", "ggml-tiny.bin", 75_000_000, null),
        (GgmlType.Base, "Base", "ggml-base.bin", 142_000_000, null),
        (GgmlType.Small, "Small", "ggml-small.bin", 466_000_000, null),
        (GgmlType.Medium, "Medium", "ggml-medium.bin", 1_500_000_000, null),
        (GgmlType.LargeV3, "Large v3", "ggml-large-v3.bin", 3_000_000_000, null),
        (GgmlType.LargeV3Turbo, "Large v3 Turbo", "ggml-large-v3-turbo.bin", 1_600_000_000, null),
    ];

    public string ModelDirectory => _optionsMonitor.CurrentValue.Local.GetModelDirectory();

    public ModelManager(
        ILogger<ModelManager> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor,
        ModelDownloadHelper downloadHelper)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _downloadHelper = downloadHelper;
    }

    public IReadOnlyList<WhisperModel> GetAllModels()
    {
        return KnownModels.Select(m =>
        {
            var filePath = Path.Combine(ModelDirectory, m.FileName);
            return new WhisperModel
            {
                Name = m.Name,
                FileName = m.FileName,
                SizeBytes = m.SizeBytes,
                FilePath = File.Exists(filePath) ? filePath : null
            };
        }).ToList();
    }

    public IReadOnlyList<WhisperModel> GetAvailableModels()
    {
        return GetAllModels().Where(m => m.IsDownloaded).ToList();
    }

    public async Task DownloadModelAsync(
        GgmlType type,
        IProgress<float>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var modelInfo = KnownModels.FirstOrDefault(m => m.Type == type);
        if (modelInfo == default)
            throw new ArgumentException($"Unknown model type: {type}", nameof(type));

        Directory.CreateDirectory(ModelDirectory);
        var targetPath = Path.Combine(ModelDirectory, modelInfo.FileName);

        _logger.LogInformation("Downloading model {Name} to {Path}", modelInfo.Name, targetPath);

        using var httpClient = _downloadHelper.CreateClient();
        var downloader = new WhisperGgmlDownloader(httpClient);
        using var modelStream = await downloader.GetGgmlModelAsync(type, cancellationToken: cancellationToken);

        await _downloadHelper.DownloadToFileAsync(modelStream, targetPath, modelInfo.SizeBytes, progress, cancellationToken, expectedSha256: modelInfo.Sha256);

        _logger.LogInformation("Model {Name} downloaded successfully", modelInfo.Name);
    }

    public void DeleteModel(WhisperModel model)
    {
        if (model.FilePath is not null && File.Exists(model.FilePath))
        {
            File.Delete(model.FilePath);
            _logger.LogInformation("Deleted model {Name} from {Path}", model.Name, model.FilePath);
        }
    }
}
