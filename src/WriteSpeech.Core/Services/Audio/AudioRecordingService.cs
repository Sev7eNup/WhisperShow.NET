using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using WriteSpeech.Core.Configuration;

namespace WriteSpeech.Core.Services.Audio;

public class AudioRecordingService : IAudioRecordingService
{
    private readonly ILogger<AudioRecordingService> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly IVoiceActivityService? _vadService;
    private const int AudioBufferMilliseconds = 50;

    private readonly Lock _recordingLock = new();
    private WaveInEvent? _waveIn;
    private WaveFormat? _waveFormat;
    private MemoryStream? _memoryStream;
    private WaveFileWriter? _waveFileWriter;
    private System.Timers.Timer? _maxDurationTimer;
    private bool _disposed;

    // Listening mode state
    private bool _isListening;
    private bool _isRecording;

    // Circular pre-buffer for capturing audio before speech detection
    private byte[]? _preBuffer;
    private int _preBufferWritePos;
    private int _preBufferLength;
    private int _preBufferCapacity;

    public event EventHandler<float>? AudioLevelChanged;
    public event EventHandler<Exception>? RecordingError;
    public event EventHandler? MaxDurationReached;
    public event EventHandler? SpeechStarted;
    public event EventHandler? SilenceDetected;

    public bool IsRecording => _isRecording;
    public bool IsListening => _isListening;

    public AudioRecordingService(
        ILogger<AudioRecordingService> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor,
        IVoiceActivityService? vadService = null)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _vadService = vadService;

        if (_vadService is not null)
        {
            _vadService.SpeechStarted += OnVadSpeechStarted;
            _vadService.SilenceDetected += OnVadSilenceDetected;
        }
    }

    public Task StartRecordingAsync()
    {
        if (_isRecording)
            throw new InvalidOperationException("Already recording.");

        var audioOptions = _optionsMonitor.CurrentValue.Audio;
        _waveFormat = new WaveFormat(audioOptions.SampleRate, 16, 1);

        _memoryStream = new MemoryStream();
        _waveFileWriter = new WaveFileWriter(_memoryStream, _waveFormat);
        _isRecording = true;

        if (_waveIn is null)
        {
            // Normal recording (non-VAD) — open mic
            _waveIn = new WaveInEvent
            {
                WaveFormat = _waveFormat,
                BufferMilliseconds = AudioBufferMilliseconds,
                DeviceNumber = audioOptions.DeviceIndex
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            try
            {
                _waveIn.StartRecording();
            }
            catch
            {
                _isRecording = false;
                CleanupRecordingResources();
                throw;
            }
        }
        // else: mic already open from listening mode — just switch to recording

        StartMaxDurationTimer(audioOptions.MaxRecordingSeconds);

        _logger.LogInformation("Recording started (Device: {Device}, SampleRate: {SampleRate}Hz, MaxDuration: {MaxSec}s)",
            audioOptions.DeviceIndex, audioOptions.SampleRate, audioOptions.MaxRecordingSeconds);

        return Task.CompletedTask;
    }

    public Task<byte[]> StopRecordingAsync()
    {
        if (!_isRecording)
            throw new InvalidOperationException("Not recording.");

        StopMaxDurationTimer();
        _isRecording = false;
        _isListening = false;

        byte[] audioData;
        try
        {
            _waveIn!.StopRecording();

            // Dispose the writer first to finalize the WAV header (RIFF/data chunk sizes).
            // WaveFileWriter.Dispose() seeks back and patches the header before closing.
            // We keep the MemoryStream reference to read the finalized data.
            lock (_recordingLock)
            {
                var writer = _waveFileWriter;
                _waveFileWriter = null;
                writer?.Dispose();
                audioData = _memoryStream!.ToArray();
                _memoryStream?.Dispose();
                _memoryStream = null;
            }
        }
        finally
        {
            CleanupRecordingResources();
            ClearPreBuffer();
        }

        _logger.LogInformation("Recording stopped ({Size} bytes)", audioData.Length);

        return Task.FromResult(audioData);
    }

    public Task StartListeningAsync()
    {
        if (_isListening)
            throw new InvalidOperationException("Already listening.");
        if (_vadService is null)
            throw new InvalidOperationException("Voice activity detection is not available.");

        var audioOptions = _optionsMonitor.CurrentValue.Audio;
        var vadOptions = audioOptions.VoiceActivity;
        _waveFormat = new WaveFormat(audioOptions.SampleRate, 16, 1);

        // Initialize circular pre-buffer
        var preBufferSeconds = vadOptions.PreBufferSeconds;
        _preBufferCapacity = (int)(audioOptions.SampleRate * 2 * preBufferSeconds); // 16-bit = 2 bytes/sample
        _preBuffer = new byte[_preBufferCapacity];
        _preBufferWritePos = 0;
        _preBufferLength = 0;

        _vadService.EnsureModelLoaded();
        _vadService.Reset();

        _isListening = true;

        _waveIn = new WaveInEvent
        {
            WaveFormat = _waveFormat,
            BufferMilliseconds = AudioBufferMilliseconds,
            DeviceNumber = audioOptions.DeviceIndex
        };

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;

        try
        {
            _waveIn.StartRecording();
        }
        catch
        {
            _isListening = false;
            CleanupRecordingResources();
            throw;
        }

        _logger.LogInformation("Listening started (Device: {Device}, PreBuffer: {PreBuf}s, SilenceDuration: {Silence}s)",
            audioOptions.DeviceIndex, preBufferSeconds, vadOptions.SilenceDurationSeconds);

        return Task.CompletedTask;
    }

    public void StopListening()
    {
        if (!_isListening) return;

        _isListening = false;
        _isRecording = false;
        StopMaxDurationTimer();

        if (_waveIn is not null)
        {
            try { _waveIn.StopRecording(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Best-effort StopRecording during StopListening"); }
        }

        CleanupRecordingResources();
        ClearPreBuffer();

        _logger.LogInformation("Listening stopped");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        bool wasRecording = _isRecording;

        // Pre-buffer: write raw bytes before VAD processing so the current chunk
        // is included when the pre-buffer is flushed on speech detection
        if (_isListening && !_isRecording)
        {
            WriteToPreBuffer(e.Buffer, e.BytesRecorded);
        }

        // Feed audio to VAD for speech/silence detection
        if (_vadService is not null && (_isListening || _isRecording))
        {
            var samples = ConvertBytesToFloats(e.Buffer, e.BytesRecorded);
            _vadService.ProcessAudioChunk(samples);
            // SpeechStarted/SilenceDetected events may have fired synchronously,
            // potentially transitioning _isRecording from false to true
        }

        // Write to WAV file (only if we were already recording before VAD processing,
        // to avoid double-writing the chunk that triggered the transition)
        if (wasRecording)
        {
            lock (_recordingLock)
            {
                _waveFileWriter?.Write(e.Buffer, 0, e.BytesRecorded);
            }
        }

        // Calculate RMS audio level for visualization
        float sum = 0;
        int sampleCount = e.BytesRecorded / 2; // 16-bit = 2 bytes per sample
        for (int i = 0; i < e.BytesRecorded; i += 2)
        {
            short sample = BitConverter.ToInt16(e.Buffer, i);
            float normalized = sample / 32768f;
            sum += normalized * normalized;
        }

        float rms = MathF.Sqrt(sum / sampleCount);
        AudioLevelChanged?.Invoke(this, rms);
    }

    private void OnVadSpeechStarted(object? sender, EventArgs e)
    {
        if (!_isListening || _isRecording) return;

        TransitionToRecording();
    }

    private void OnVadSilenceDetected(object? sender, EventArgs e)
    {
        if (!_isRecording) return;

        _logger.LogDebug("Silence detected during recording, notifying listeners");
        SilenceDetected?.Invoke(this, EventArgs.Empty);
    }

    private void TransitionToRecording()
    {
        var audioOptions = _optionsMonitor.CurrentValue.Audio;

        lock (_recordingLock)
        {
            _memoryStream = new MemoryStream();
            _waveFileWriter = new WaveFileWriter(_memoryStream, _waveFormat!);

            // Flush pre-buffer into the WAV writer (captures audio before speech onset)
            var preBufferData = FlushPreBuffer();
            if (preBufferData.Length > 0)
            {
                _waveFileWriter.Write(preBufferData, 0, preBufferData.Length);
            }

            _isRecording = true;
        }

        StartMaxDurationTimer(audioOptions.MaxRecordingSeconds);

        _logger.LogInformation("VAD: Transitioned from listening to recording (pre-buffer: {PreBuf} bytes)",
            _preBufferLength);

        SpeechStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            _logger.LogError(e.Exception, "Recording error");
            _isRecording = false;
            _isListening = false;
            CleanupRecordingResources();
            RecordingError?.Invoke(this, e.Exception);
        }
    }

    private void CleanupRecordingResources()
    {
        lock (_recordingLock)
        {
            if (_waveIn is null) return;

            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;

            try { _waveIn.Dispose(); } catch (Exception ex) { _logger.LogDebug(ex, "Best-effort WaveIn disposal"); }
            _waveIn = null;

            try { _waveFileWriter?.Dispose(); } catch (Exception ex) { _logger.LogDebug(ex, "Best-effort WaveFileWriter disposal"); }
            _waveFileWriter = null;
            try { _memoryStream?.Dispose(); } catch (Exception ex) { _logger.LogDebug(ex, "Best-effort MemoryStream disposal"); }
            _memoryStream = null;
        }
    }

    // --- Circular pre-buffer ---

    private void WriteToPreBuffer(byte[] data, int count)
    {
        if (_preBuffer is null) return;

        for (int i = 0; i < count; i++)
        {
            _preBuffer[_preBufferWritePos] = data[i];
            _preBufferWritePos = (_preBufferWritePos + 1) % _preBufferCapacity;
            if (_preBufferLength < _preBufferCapacity) _preBufferLength++;
        }
    }

    private byte[] FlushPreBuffer()
    {
        if (_preBuffer is null || _preBufferLength == 0)
            return [];

        var result = new byte[_preBufferLength];
        int readStart = (_preBufferWritePos - _preBufferLength + _preBufferCapacity) % _preBufferCapacity;
        for (int i = 0; i < _preBufferLength; i++)
            result[i] = _preBuffer[(readStart + i) % _preBufferCapacity];

        _preBufferLength = 0;
        _preBufferWritePos = 0;
        return result;
    }

    private void ClearPreBuffer()
    {
        _preBuffer = null;
        _preBufferWritePos = 0;
        _preBufferLength = 0;
        _preBufferCapacity = 0;
    }

    // --- Helpers ---

    internal static float[] ConvertBytesToFloats(byte[] buffer, int bytesRecorded)
    {
        int sampleCount = bytesRecorded / 2; // 16-bit = 2 bytes per sample
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(buffer, i * 2);
            samples[i] = sample / 32768f;
        }
        return samples;
    }

    private void StartMaxDurationTimer(int maxRecordingSeconds)
    {
        StopMaxDurationTimer();
        _maxDurationTimer = new System.Timers.Timer(maxRecordingSeconds * 1000);
        _maxDurationTimer.AutoReset = false;
        _maxDurationTimer.Elapsed += (_, _) =>
        {
            _logger.LogInformation("Max recording duration reached ({MaxSec}s)", maxRecordingSeconds);
            MaxDurationReached?.Invoke(this, EventArgs.Empty);
        };
        _maxDurationTimer.Start();
    }

    private void StopMaxDurationTimer()
    {
        _maxDurationTimer?.Stop();
        _maxDurationTimer?.Dispose();
        _maxDurationTimer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_vadService is not null)
        {
            _vadService.SpeechStarted -= OnVadSpeechStarted;
            _vadService.SilenceDetected -= OnVadSilenceDetected;
        }

        StopMaxDurationTimer();

        if (_waveIn is not null)
            try { _waveIn.StopRecording(); } catch (Exception ex) { _logger.LogDebug(ex, "Best-effort StopRecording during disposal"); }

        CleanupRecordingResources();
        ClearPreBuffer();
    }
}
