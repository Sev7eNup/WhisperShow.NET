using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net.Ggml;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.ModelManagement;

public class ModelManager : IModelManager
{
    private readonly ILogger<ModelManager> _logger;
    private readonly LocalWhisperOptions _localOptions;

    private static readonly (GgmlType Type, string Name, string FileName, long SizeBytes)[] KnownModels =
    [
        (GgmlType.Tiny, "Tiny", "ggml-tiny.bin", 75_000_000),
        (GgmlType.Base, "Base", "ggml-base.bin", 142_000_000),
        (GgmlType.Small, "Small", "ggml-small.bin", 466_000_000),
        (GgmlType.Medium, "Medium", "ggml-medium.bin", 1_500_000_000),
        (GgmlType.LargeV3, "Large v3", "ggml-large-v3.bin", 3_000_000_000),
    ];

    public string ModelDirectory => _localOptions.GetModelDirectory();

    public ModelManager(
        ILogger<ModelManager> logger,
        IOptions<WhisperShowOptions> options)
    {
        _logger = logger;
        _localOptions = options.Value.Local;
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

        using var httpClient = new HttpClient();
        var downloader = new WhisperGgmlDownloader(httpClient);
        using var modelStream = await downloader.GetGgmlModelAsync(type, cancellationToken: cancellationToken);
        using var fileStream = File.Create(targetPath);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await modelStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;
            progress?.Report((float)totalRead / modelInfo.SizeBytes);
        }

        _logger.LogInformation("Model {Name} downloaded successfully ({Size} bytes)", modelInfo.Name, totalRead);
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
