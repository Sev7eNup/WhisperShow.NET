using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Voxwright.Core.Configuration;
using Voxwright.Core.Models;

namespace Voxwright.Core.Services.ModelManagement;

public class CorrectionModelManager : ICorrectionModelManager
{
    private readonly ILogger<CorrectionModelManager> _logger;
    private readonly IOptionsMonitor<VoxwrightOptions> _optionsMonitor;
    private readonly ModelDownloadHelper _downloadHelper;

    private static readonly (string Name, string FileName, long SizeBytes, string DownloadUrl, string? Sha256)[] KnownModels =
    [
        (
            "Gemma 3 1B IT",
            "google_gemma-3-1b-it-Q4_K_M.gguf",
            806_000_000L,
            "https://huggingface.co/bartowski/google_gemma-3-1b-it-GGUF/resolve/main/google_gemma-3-1b-it-Q4_K_M.gguf",
            "fa976b45909413d8d418c841e6800bdb28bfc73a5cae99cfb30deb2c4c7da87d"
        ),
        (
            "Gemma 4 E2B IT",
            "google_gemma-4-E2B-it-Q4_K_M.gguf",
            3_462_673_376L,
            "https://huggingface.co/bartowski/google_gemma-4-E2B-it-GGUF/resolve/main/google_gemma-4-E2B-it-Q4_K_M.gguf",
            "5efe645db4e1909c7a1f4a9608df18e6c14383f5e86777fc49f769f9ba7d5fdf"
        ),
        (
            "Qwen 2.5 3B Instruct",
            "qwen2.5-3b-instruct-q4_k_m.gguf",
            2_000_000_000L,
            "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf",
            "5ae0c201348e276d543a1e5c0e053370e32415774095c677319f62b302b1620a"
        ),
        (
            "Phi-3.5 Mini 3.8B",
            "Phi-3.5-mini-instruct-Q4_K_M.gguf",
            2_400_000_000L,
            "https://huggingface.co/bartowski/Phi-3.5-mini-instruct-GGUF/resolve/main/Phi-3.5-mini-instruct-Q4_K_M.gguf",
            "216e0385d8d2da14827e44b4482f0d2885e041d99bb1103c60092eedd2da1284"
        ),
    ];

    public string ModelDirectory => _optionsMonitor.CurrentValue.TextCorrection.GetLocalModelDirectory();

    public CorrectionModelManager(
        ILogger<CorrectionModelManager> logger,
        IOptionsMonitor<VoxwrightOptions> optionsMonitor,
        ModelDownloadHelper downloadHelper)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _downloadHelper = downloadHelper;
    }

    public IReadOnlyList<CorrectionModelInfo> GetAllModels()
    {
        return KnownModels.Select(m =>
        {
            var filePath = Path.Combine(ModelDirectory, m.FileName);
            return new CorrectionModelInfo
            {
                Name = m.Name,
                FileName = m.FileName,
                SizeBytes = m.SizeBytes,
                DownloadUrl = m.DownloadUrl,
                FilePath = File.Exists(filePath) ? filePath : null
            };
        }).ToList();
    }

    public async Task DownloadModelAsync(
        string fileName,
        IProgress<float>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var modelInfo = KnownModels.FirstOrDefault(m => m.FileName == fileName);
        if (modelInfo == default)
            throw new ArgumentException($"Unknown correction model: {fileName}", nameof(fileName));

        Directory.CreateDirectory(ModelDirectory);
        var targetPath = Path.Combine(ModelDirectory, modelInfo.FileName);

        _logger.LogInformation("Downloading correction model {Name} to {Path}", modelInfo.Name, targetPath);

        using var httpClient = _downloadHelper.CreateClient(TimeSpan.FromHours(2));
        using var response = await httpClient.GetAsync(modelInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength ?? modelInfo.SizeBytes;
        await using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        await _downloadHelper.DownloadToFileAsync(downloadStream, targetPath, contentLength, progress, cancellationToken, expectedSha256: modelInfo.Sha256);

        _logger.LogInformation("Correction model {Name} downloaded successfully", modelInfo.Name);
    }

    public void DeleteModel(CorrectionModelInfo model)
    {
        if (model.FilePath is not null && File.Exists(model.FilePath))
        {
            File.Delete(model.FilePath);
            _logger.LogInformation("Deleted correction model {Name} from {Path}", model.Name, model.FilePath);
        }
    }
}
