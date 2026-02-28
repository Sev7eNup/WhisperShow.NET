using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.ModelManagement;

public class ParakeetModelManager : IParakeetModelManager
{
    private const string HuggingFaceBaseUrl =
        "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/main";

    private readonly ILogger<ParakeetModelManager> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly ModelDownloadHelper _downloadHelper;

    private static readonly (string Name, string DirectoryName, long SizeBytes)[] KnownModels =
    [
        ("Parakeet TDT 0.6B v2 (int8)", "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8", 631_000_000),
    ];

    private static readonly (string FileName, long SizeBytes, string? Sha256)[] ModelFiles =
    [
        ("encoder.int8.onnx", 622_000_000, "5c5b211a279e6cfc78b528e6cee6d05d1df3b0c5c26a889d3901c8d1d185d416"),
        ("decoder.int8.onnx", 7_000_000, "a793c390f54bb5a0f3db9825b14b612ae81d8b3e3398fefe72a8ce98e5d05446"),
        ("joiner.int8.onnx", 2_000_000, "574a3af8df9c2745aebc54f8485b1ec63a876d0f66bbb6244ff846c7ca1cdcd8"),
        ("tokens.txt", 9_000, null), // not LFS-tracked, no hash available
    ];

    public string ModelDirectory => _optionsMonitor.CurrentValue.Parakeet.GetModelDirectory();

    public ParakeetModelManager(
        ILogger<ParakeetModelManager> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor,
        ModelDownloadHelper downloadHelper)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _downloadHelper = downloadHelper;
    }

    public IReadOnlyList<ParakeetModelInfo> GetAllModels()
    {
        return KnownModels.Select(m =>
        {
            var dirPath = Path.Combine(ModelDirectory, m.DirectoryName);
            var exists = Directory.Exists(dirPath);

            return new ParakeetModelInfo
            {
                Name = m.Name,
                FileName = m.DirectoryName,
                DirectoryName = m.DirectoryName,
                SizeBytes = m.SizeBytes,
                FilePath = exists ? dirPath : null,
                DownloadUrl = $"{HuggingFaceBaseUrl}",
            };
        }).ToList();
    }

    public IReadOnlyList<ParakeetModelInfo> GetAvailableModels()
    {
        return GetAllModels().Where(m => m.IsDirectoryComplete).ToList();
    }

    public async Task DownloadModelAsync(
        string modelName,
        IProgress<float>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var modelInfo = KnownModels.FirstOrDefault(m => m.DirectoryName == modelName);
        if (modelInfo == default)
            throw new ArgumentException($"Unknown Parakeet model: {modelName}", nameof(modelName));

        var targetDir = Path.Combine(ModelDirectory, modelInfo.DirectoryName);
        Directory.CreateDirectory(targetDir);

        _logger.LogInformation("Downloading Parakeet model {Name} to {Path}", modelInfo.Name, targetDir);

        var totalSize = ModelFiles.Sum(f => f.SizeBytes);
        long downloadedSoFar = 0;

        using var httpClient = _downloadHelper.CreateClient(TimeSpan.FromMinutes(30));

        foreach (var (fileName, fileSize, sha256) in ModelFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetPath = Path.Combine(targetDir, fileName);
            if (File.Exists(targetPath))
            {
                _logger.LogInformation("Skipping {File} (already exists)", fileName);
                downloadedSoFar += fileSize;
                progress?.Report((float)downloadedSoFar / totalSize);
                continue;
            }

            var url = $"{HuggingFaceBaseUrl}/{fileName}";
            _logger.LogInformation("Downloading {File} from {Url}", fileName, url);

            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var captured = downloadedSoFar;
            var fileProgress = new Progress<float>(p =>
                progress?.Report((float)(captured + (long)(p * fileSize)) / totalSize));

            await _downloadHelper.DownloadToFileAsync(stream, targetPath, fileSize, fileProgress, cancellationToken, expectedSha256: sha256);

            downloadedSoFar += fileSize;
            progress?.Report((float)downloadedSoFar / totalSize);
        }

        _logger.LogInformation("Parakeet model {Name} downloaded successfully", modelInfo.Name);
    }

    public void DeleteModel(ParakeetModelInfo model)
    {
        if (model.FilePath is not null && Directory.Exists(model.FilePath))
        {
            Directory.Delete(model.FilePath, recursive: true);
            _logger.LogInformation("Deleted Parakeet model {Name} from {Path}", model.Name, model.FilePath);
        }
    }
}
