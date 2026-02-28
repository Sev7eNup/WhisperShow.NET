using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.ModelManagement;

public class VadModelManager : IVadModelManager
{
    private const string DownloadUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx";

    private const string ModelFileName = "silero_vad.onnx";
    private const long ModelSizeBytes = 629_000;

    private readonly ILogger<VadModelManager> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly ModelDownloadHelper _downloadHelper;

    public string ModelDirectory => _optionsMonitor.CurrentValue.Audio.VoiceActivity.GetModelDirectory();

    public bool IsModelDownloaded => File.Exists(Path.Combine(ModelDirectory, ModelFileName));

    public VadModelManager(
        ILogger<VadModelManager> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor,
        ModelDownloadHelper downloadHelper)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _downloadHelper = downloadHelper;
    }

    public VadModelInfo GetModel()
    {
        var filePath = Path.Combine(ModelDirectory, ModelFileName);
        return new VadModelInfo
        {
            Name = "Silero VAD",
            FileName = ModelFileName,
            SizeBytes = ModelSizeBytes,
            FilePath = File.Exists(filePath) ? filePath : null,
            DownloadUrl = DownloadUrl,
        };
    }

    public async Task DownloadModelAsync(
        IProgress<float>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ModelDirectory);

        var targetPath = Path.Combine(ModelDirectory, ModelFileName);
        if (File.Exists(targetPath))
        {
            _logger.LogInformation("Silero VAD model already exists at {Path}", targetPath);
            progress?.Report(1f);
            return;
        }

        _logger.LogInformation("Downloading Silero VAD model from {Url} to {Path}", DownloadUrl, targetPath);

        using var httpClient = _downloadHelper.CreateClient(TimeSpan.FromMinutes(5));
        using var response = await httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await _downloadHelper.DownloadToFileAsync(stream, targetPath, ModelSizeBytes, progress, cancellationToken);

        _logger.LogInformation("Silero VAD model downloaded successfully");
    }

    public void DeleteModel()
    {
        var filePath = Path.Combine(ModelDirectory, ModelFileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("Deleted Silero VAD model from {Path}", filePath);
        }
    }
}
