using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.ModelManagement;

public class CorrectionModelManager : ICorrectionModelManager
{
    private readonly ILogger<CorrectionModelManager> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
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
            "Gemma 2 2B IT",
            "gemma-2-2b-it-Q4_K_M.gguf",
            1_600_000_000L,
            "https://huggingface.co/bartowski/gemma-2-2b-it-GGUF/resolve/main/gemma-2-2b-it-Q4_K_M.gguf",
            "90f9c2316393fb452b47988ffa7a411f0891e2c1a7178ae868ac4f70f96f7c8d"
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
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor,
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
