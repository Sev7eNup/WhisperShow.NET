using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;

namespace WhisperShow.Core.Services.Transcription;

public class LocalTranscriptionService : ITranscriptionService, IDisposable
{
    private readonly ILogger<LocalTranscriptionService> _logger;
    private readonly LocalWhisperOptions _options;
    private WhisperFactory? _factory;
    private string? _loadedModelPath;
    private bool _disposed;

    public string ProviderName => "Lokal (Whisper.net)";

    public bool IsAvailable
    {
        get
        {
            var modelPath = GetModelPath();
            return modelPath is not null && File.Exists(modelPath);
        }
    }

    public LocalTranscriptionService(
        ILogger<LocalTranscriptionService> logger,
        IOptions<WhisperShowOptions> options)
    {
        _logger = logger;
        _options = options.Value.Local;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        byte[] audioData,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var modelPath = GetModelPath()
            ?? throw new InvalidOperationException("No local Whisper model found. Please download a model first.");

        EnsureFactoryLoaded(modelPath);

        var builder = _factory!.CreateBuilder()
            .WithLanguage(language ?? "auto");

        using var processor = builder.Build();
        using var stream = new MemoryStream(audioData);

        _logger.LogInformation("Processing audio locally ({Size} bytes, model: {Model})",
            audioData.Length, _options.ModelName);

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

    private string? GetModelPath()
    {
        var dir = _options.GetModelDirectory();
        var path = Path.Combine(dir, _options.ModelName);
        return File.Exists(path) ? path : null;
    }

    private void EnsureFactoryLoaded(string modelPath)
    {
        if (_factory is not null && _loadedModelPath == modelPath)
            return;

        _factory?.Dispose();
        _logger.LogInformation("Loading Whisper model from {Path}", modelPath);
        _factory = WhisperFactory.FromPath(modelPath);
        _loadedModelPath = modelPath;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _factory?.Dispose();
    }
}
