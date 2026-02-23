using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.ModelManagement;

public class CorrectionModelManager : ICorrectionModelManager
{
    private readonly ILogger<CorrectionModelManager> _logger;
    private readonly TextCorrectionOptions _options;
    private readonly ModelDownloadHelper _downloadHelper;

    private static readonly (string Name, string FileName, long SizeBytes, string DownloadUrl)[] KnownModels =
    [
        (
            "Gemma 3 1B IT",
            "google_gemma-3-1b-it-Q4_K_M.gguf",
            806_000_000L,
            "https://huggingface.co/bartowski/google_gemma-3-1b-it-GGUF/resolve/main/google_gemma-3-1b-it-Q4_K_M.gguf"
        ),
        (
            "Gemma 2 2B IT",
            "gemma-2-2b-it-Q4_K_M.gguf",
            1_600_000_000L,
            "https://huggingface.co/bartowski/gemma-2-2b-it-GGUF/resolve/main/gemma-2-2b-it-Q4_K_M.gguf"
        ),
        (
            "Qwen 2.5 3B Instruct",
            "qwen2.5-3b-instruct-q4_k_m.gguf",
            2_000_000_000L,
            "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf"
        ),
        (
            "Phi-3.5 Mini 3.8B",
            "Phi-3.5-mini-instruct-Q4_K_M.gguf",
            2_400_000_000L,
            "https://huggingface.co/bartowski/Phi-3.5-mini-instruct-GGUF/resolve/main/Phi-3.5-mini-instruct-Q4_K_M.gguf"
        ),
    ];

    public string ModelDirectory => _options.GetLocalModelDirectory();

    public CorrectionModelManager(
        ILogger<CorrectionModelManager> logger,
        IOptions<WhisperShowOptions> options,
        ModelDownloadHelper downloadHelper)
    {
        _logger = logger;
        _options = options.Value.TextCorrection;
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

        await _downloadHelper.DownloadToFileAsync(downloadStream, targetPath, contentLength, progress, cancellationToken);

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
