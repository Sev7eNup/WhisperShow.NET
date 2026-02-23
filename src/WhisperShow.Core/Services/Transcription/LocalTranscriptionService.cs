using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net;
using Whisper.net.LibraryLoader;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.Transcription;

public class LocalTranscriptionService : ITranscriptionService, IDisposable
{
    private readonly ILogger<LocalTranscriptionService> _logger;
    private readonly IOptionsMonitor<WhisperShowOptions> _optionsMonitor;
    private readonly Lock _loadLock = new();
    private WhisperFactory? _factory;
    private string? _loadedModelPath;
    private bool _disposed;

    public TranscriptionProvider ProviderType => TranscriptionProvider.Local;
    public string ProviderName => "Lokal (Whisper.net)";

    public bool IsAvailable
    {
        get
        {
            var modelPath = GetModelPath(_optionsMonitor.CurrentValue.Local);
            return modelPath is not null;
        }
    }

    public bool IsModelLoaded => _factory is not null;

    public LocalTranscriptionService(
        ILogger<LocalTranscriptionService> logger,
        IOptionsMonitor<WhisperShowOptions> optionsMonitor)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        byte[] audioData,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var localOpts = _optionsMonitor.CurrentValue.Local;

        var modelPath = GetModelPath(localOpts)
            ?? throw new InvalidOperationException("No local Whisper model found. Please download a model first.");

        EnsureFactoryLoaded(modelPath, localOpts.GpuAcceleration);

        var builder = _factory!.CreateBuilder()
            .WithLanguage(language ?? "auto");

        using var processor = builder.Build();
        using var stream = new MemoryStream(audioData);

        _logger.LogInformation("Processing audio locally ({Size} bytes, model: {Model})",
            audioData.Length, localOpts.ModelName);

        var segments = new List<string>();

        await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
        {
            segments.Add(segment.Text);
        }

        var text = string.Join(" ", segments).Trim();

        _logger.LogInformation("Local transcription completed: {Length} chars", text.Length);

        return new TranscriptionResult
        {
            Text = text,
            Language = language
        };
    }

    public void Preload()
    {
        var localOpts = _optionsMonitor.CurrentValue.Local;
        var modelPath = GetModelPath(localOpts);
        if (modelPath is not null)
            EnsureFactoryLoaded(modelPath, localOpts.GpuAcceleration);
    }

    public void Preload(string modelName)
    {
        var localOpts = _optionsMonitor.CurrentValue.Local;
        var dir = localOpts.GetModelDirectory();
        var path = Path.Combine(dir, modelName);
        if (File.Exists(path))
            EnsureFactoryLoaded(path, localOpts.GpuAcceleration);
    }

    private static string? GetModelPath(LocalWhisperOptions localOpts)
    {
        var dir = localOpts.GetModelDirectory();
        var path = Path.Combine(dir, localOpts.ModelName);
        return File.Exists(path) ? path : null;
    }

    private void EnsureFactoryLoaded(string modelPath, bool gpuAcceleration)
    {
        lock (_loadLock)
        {
            if (_factory is not null && _loadedModelPath == modelPath)
                return;

            _factory?.Dispose();

            // RuntimeOptions.RuntimeLibraryOrder is a static Whisper.net config.
            // Safe here: singleton service behind lock, set immediately before FromPath().
            // LocalTextCorrectionService uses LLamaSharp with its own ModelParams.GpuLayerCount.
            if (gpuAcceleration)
                RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cuda, RuntimeLibrary.Cpu];
            else
                RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];

            _logger.LogInformation("Loading Whisper model from {Path} (GPU: {Gpu})",
                modelPath, gpuAcceleration);
            _factory = WhisperFactory.FromPath(modelPath);
            _loadedModelPath = modelPath;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _factory?.Dispose();
    }
}
