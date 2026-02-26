using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;

namespace WriteSpeech.Core.Services.Transcription;

public class ParakeetTranscriptionService : ITranscriptionService, IDisposable
{
    private readonly ILogger<ParakeetTranscriptionService> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly Lock _loadLock = new();
    private OfflineRecognizer? _recognizer;
    private string? _loadedModelDir;
    private bool _disposed;

    public TranscriptionProvider ProviderType => TranscriptionProvider.Parakeet;
    public string ProviderName => "Lokal (Parakeet)";

    public bool IsAvailable
    {
        get
        {
            var opts = _optionsMonitor.CurrentValue.Parakeet;
            var modelDir = GetModelDir(opts);
            return modelDir is not null;
        }
    }

    public bool IsModelLoaded => _recognizer is not null;

    public ParakeetTranscriptionService(
        ILogger<ParakeetTranscriptionService> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        byte[] audioData,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var opts = _optionsMonitor.CurrentValue.Parakeet;

        var modelDir = GetModelDir(opts)
            ?? throw new InvalidOperationException(
                "No Parakeet model found. Please download a model first.");

        EnsureRecognizerLoaded(modelDir, opts);

        var samples = ConvertWavToFloatSamples(audioData);

        _logger.LogInformation(
            "Processing audio with Parakeet ({Samples} samples, model: {Model})",
            samples.Length, opts.ModelName);

        var text = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stream = _recognizer!.CreateStream();
            stream.AcceptWaveform(16000, samples);

            _recognizer.Decode(stream);

            return stream.Result.Text;
        }, cancellationToken);

        var result = text?.Trim() ?? "";

        _logger.LogInformation("Parakeet transcription completed: {Length} chars", result.Length);

        return new TranscriptionResult
        {
            Text = result,
            Language = "en"
        };
    }

    public void Preload()
    {
        var opts = _optionsMonitor.CurrentValue.Parakeet;
        var modelDir = GetModelDir(opts);
        if (modelDir is not null)
            EnsureRecognizerLoaded(modelDir, opts);
    }

    /// <summary>
    /// Converts 16-bit PCM WAV audio data to float samples normalized to [-1.0, 1.0].
    /// Expects a standard WAV file with a 44-byte header.
    /// </summary>
    internal static float[] ConvertWavToFloatSamples(byte[] wavData)
    {
        // Skip WAV header (standard 44 bytes)
        const int headerSize = 44;
        if (wavData.Length <= headerSize)
            return [];

        var dataLength = wavData.Length - headerSize;
        var sampleCount = dataLength / 2; // 16-bit = 2 bytes per sample
        var samples = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var offset = headerSize + i * 2;
            var sample = BitConverter.ToInt16(wavData, offset);
            samples[i] = sample / 32768f;
        }

        return samples;
    }

    private static string? GetModelDir(ParakeetOptions opts)
    {
        var baseDir = opts.GetModelDirectory();
        var modelDir = Path.Combine(baseDir, opts.ModelName);

        // Check if model directory exists with required files
        if (!Directory.Exists(modelDir))
            return null;

        var hasEncoder = File.Exists(Path.Combine(modelDir, "encoder.int8.onnx"));
        var hasDecoder = File.Exists(Path.Combine(modelDir, "decoder.int8.onnx"));
        var hasJoiner = File.Exists(Path.Combine(modelDir, "joiner.int8.onnx"));
        var hasTokens = File.Exists(Path.Combine(modelDir, "tokens.txt"));

        return hasEncoder && hasDecoder && hasJoiner && hasTokens ? modelDir : null;
    }

    private void EnsureRecognizerLoaded(string modelDir, ParakeetOptions opts)
    {
        lock (_loadLock)
        {
            if (_recognizer is not null && _loadedModelDir == modelDir)
                return;

            _recognizer?.Dispose();

            var config = new OfflineRecognizerConfig();
            config.ModelConfig.Transducer.Encoder = Path.Combine(modelDir, "encoder.int8.onnx");
            config.ModelConfig.Transducer.Decoder = Path.Combine(modelDir, "decoder.int8.onnx");
            config.ModelConfig.Transducer.Joiner = Path.Combine(modelDir, "joiner.int8.onnx");
            config.ModelConfig.Tokens = Path.Combine(modelDir, "tokens.txt");
            config.ModelConfig.ModelType = "nemo_transducer";
            config.ModelConfig.NumThreads = opts.NumThreads;
            config.ModelConfig.Provider = opts.GpuAcceleration ? "cuda" : "cpu";

            _logger.LogInformation(
                "Loading Parakeet model from {Path} (GPU: {Gpu}, Threads: {Threads})",
                modelDir, opts.GpuAcceleration, opts.NumThreads);

            _recognizer = new OfflineRecognizer(config);
            _loadedModelDir = modelDir;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _recognizer?.Dispose();
    }
}
