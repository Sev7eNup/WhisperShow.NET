using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;
using Voxwright.Core.Configuration;
using Voxwright.Core.Models;

namespace Voxwright.Core.Services.Transcription;

public class ParakeetTranscriptionService : ITranscriptionService, IDisposable
{
    private readonly ILogger<ParakeetTranscriptionService> _logger;
    private readonly IOptionsMonitor<VoxwrightOptions> _optionsMonitor;
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
        IOptionsMonitor<VoxwrightOptions> optionsMonitor)
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

            using var stream = _recognizer!.CreateStream();
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
    /// Parses the WAV header to find the actual data chunk offset.
    /// </summary>
    internal static float[] ConvertWavToFloatSamples(byte[] wavData)
    {
        var dataOffset = FindDataChunkOffset(wavData);
        if (dataOffset < 0 || dataOffset >= wavData.Length)
            return [];

        var dataLength = wavData.Length - dataOffset;
        var sampleCount = dataLength / 2; // 16-bit = 2 bytes per sample
        var samples = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var offset = dataOffset + i * 2;
            var sample = BitConverter.ToInt16(wavData, offset);
            samples[i] = sample / 32768f;
        }

        return samples;
    }

    /// <summary>
    /// Scans for the "data" chunk in a WAV file and returns the offset of the audio data.
    /// Falls back to 44 bytes if the header cannot be parsed.
    /// </summary>
    internal static int FindDataChunkOffset(byte[] wavData)
    {
        const int fallbackHeaderSize = 44;

        // Minimum valid WAV: RIFF(4) + size(4) + WAVE(4) + fmt (8+16) + data(8) = 44
        if (wavData.Length < 44)
            return -1;

        // Verify RIFF header
        if (wavData[0] != 'R' || wavData[1] != 'I' || wavData[2] != 'F' || wavData[3] != 'F')
            return fallbackHeaderSize;

        // Scan chunks starting after "RIFF" + size + "WAVE" (12 bytes)
        int pos = 12;
        while (pos + 8 <= wavData.Length)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(wavData, pos, 4);
            var chunkSize = BitConverter.ToInt32(wavData, pos + 4);

            if (chunkId == "data")
                return pos + 8; // Audio data starts after chunk ID (4) + size (4)

            // Advance to next chunk (chunks are word-aligned)
            pos += 8 + chunkSize;
            if (chunkSize % 2 != 0) pos++; // Padding byte for odd-sized chunks
        }

        // "data" chunk not found, fall back to standard offset
        return fallbackHeaderSize;
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

    public void UnloadModel()
    {
        lock (_loadLock)
        {
            _recognizer?.Dispose();
            _recognizer = null;
            _loadedModelDir = null;
            _logger.LogInformation("Parakeet model unloaded");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _recognizer?.Dispose();
    }
}
