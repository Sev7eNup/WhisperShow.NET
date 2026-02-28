using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;
using WriteSpeech.Core.Configuration;

namespace WriteSpeech.Core.Services.Audio;

public class VoiceActivityService : IVoiceActivityService
{
    private readonly ILogger<VoiceActivityService> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly Lock _loadLock = new();
    private VoiceActivityDetector? _detector;
    private string? _loadedModelPath;
    private bool _wasSpeechActive;
    private bool _disposed;

    public event EventHandler? SpeechStarted;
    public event EventHandler? SilenceDetected;

    public bool IsSpeechActive => _wasSpeechActive;
    public bool IsModelLoaded => _detector is not null;

    public VoiceActivityService(
        ILogger<VoiceActivityService> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    public void EnsureModelLoaded()
    {
        var opts = _optionsMonitor.CurrentValue.Audio.VoiceActivity;
        var modelPath = Path.Combine(opts.GetModelDirectory(), "silero_vad.onnx");

        if (!File.Exists(modelPath))
            throw new InvalidOperationException(
                $"Silero VAD model not found at {modelPath}. Please download the model first.");

        lock (_loadLock)
        {
            if (_detector is not null && _loadedModelPath == modelPath)
                return;

            _detector?.Dispose();

            var config = new VadModelConfig();
            config.SileroVad.Model = modelPath;
            config.SileroVad.Threshold = opts.Threshold;
            config.SileroVad.MinSilenceDuration = opts.SilenceDurationSeconds;
            config.SileroVad.MinSpeechDuration = 0.25f;
            config.SileroVad.MaxSpeechDuration = 600f;
            config.SileroVad.WindowSize = 512;
            config.SampleRate = 16000;
            config.NumThreads = 1;
            config.Provider = "cpu";

            _logger.LogInformation(
                "Loading Silero VAD model from {Path} (Threshold: {Threshold}, SilenceDuration: {Silence}s)",
                modelPath, opts.Threshold, opts.SilenceDurationSeconds);

            _detector = new VoiceActivityDetector(config, bufferSizeInSeconds: 120f);
            _loadedModelPath = modelPath;
            _wasSpeechActive = false;
        }
    }

    public void ProcessAudioChunk(float[] samples)
    {
        if (_detector is null)
            return;

        _detector.AcceptWaveform(samples);

        var isSpeech = _detector.IsSpeechDetected();

        // Transition: silence → speech
        if (isSpeech && !_wasSpeechActive)
        {
            _wasSpeechActive = true;
            _logger.LogDebug("VAD: Speech started");
            SpeechStarted?.Invoke(this, EventArgs.Empty);
        }

        // Check for completed speech segments (silence after speech exceeded threshold)
        if (!_detector.IsEmpty())
        {
            // Drain all completed segments
            while (!_detector.IsEmpty())
            {
                _detector.Pop();
            }

            // Only fire SilenceDetected if we were in a speech state
            if (_wasSpeechActive)
            {
                _wasSpeechActive = false;
                _logger.LogDebug("VAD: Silence detected after speech");
                SilenceDetected?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (!isSpeech && _wasSpeechActive)
        {
            // Speech flag dropped but no segment yet — VAD is still accumulating silence.
            // The segment will appear once MinSilenceDuration is reached.
        }
    }

    public void Reset()
    {
        if (_detector is not null)
        {
            // Reload to reset internal state
            var opts = _optionsMonitor.CurrentValue.Audio.VoiceActivity;
            var modelPath = Path.Combine(opts.GetModelDirectory(), "silero_vad.onnx");

            lock (_loadLock)
            {
                _detector.Dispose();

                var config = new VadModelConfig();
                config.SileroVad.Model = modelPath;
                config.SileroVad.Threshold = opts.Threshold;
                config.SileroVad.MinSilenceDuration = opts.SilenceDurationSeconds;
                config.SileroVad.MinSpeechDuration = 0.25f;
                config.SileroVad.MaxSpeechDuration = 600f;
                config.SileroVad.WindowSize = 512;
                config.SampleRate = 16000;
                config.NumThreads = 1;
                config.Provider = "cpu";

                _detector = new VoiceActivityDetector(config, bufferSizeInSeconds: 120f);
                _wasSpeechActive = false;
            }
        }
    }

    public void UnloadModel()
    {
        lock (_loadLock)
        {
            _detector?.Dispose();
            _detector = null;
            _loadedModelPath = null;
            _wasSpeechActive = false;
            _logger.LogInformation("Silero VAD model unloaded");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _detector?.Dispose();
    }
}
