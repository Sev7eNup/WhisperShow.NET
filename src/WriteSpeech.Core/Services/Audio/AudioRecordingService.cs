using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using WriteSpeech.Core.Configuration;

namespace WriteSpeech.Core.Services.Audio;

public class AudioRecordingService : IAudioRecordingService
{
    private readonly ILogger<AudioRecordingService> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private const int AudioBufferMilliseconds = 50;

    private readonly Lock _recordingLock = new();
    private WaveInEvent? _waveIn;
    private MemoryStream? _memoryStream;
    private WaveFileWriter? _waveFileWriter;
    private System.Timers.Timer? _maxDurationTimer;
    private bool _disposed;

    public event EventHandler<float>? AudioLevelChanged;
    public event EventHandler<Exception>? RecordingError;
    public event EventHandler? MaxDurationReached;
    public bool IsRecording => _waveIn is not null;

    public AudioRecordingService(
        ILogger<AudioRecordingService> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    public Task StartRecordingAsync()
    {
        if (IsRecording)
            throw new InvalidOperationException("Already recording.");

        var audioOptions = _optionsMonitor.CurrentValue.Audio;
        var waveFormat = new WaveFormat(audioOptions.SampleRate, 16, 1);

        _memoryStream = new MemoryStream();
        _waveFileWriter = new WaveFileWriter(_memoryStream, waveFormat);

        _waveIn = new WaveInEvent
        {
            WaveFormat = waveFormat,
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
            CleanupRecordingResources();
            throw;
        }

        StartMaxDurationTimer(audioOptions.MaxRecordingSeconds);

        _logger.LogInformation("Recording started (Device: {Device}, SampleRate: {SampleRate}Hz, MaxDuration: {MaxSec}s)",
            audioOptions.DeviceIndex, audioOptions.SampleRate, audioOptions.MaxRecordingSeconds);

        return Task.CompletedTask;
    }

    public Task<byte[]> StopRecordingAsync()
    {
        if (!IsRecording)
            throw new InvalidOperationException("Not recording.");

        StopMaxDurationTimer();

        byte[] audioData;
        try
        {
            _waveIn!.StopRecording();
            // Dispose the writer first to finalize the WAV header (RIFF/data chunk sizes).
            // WaveFileWriter.Dispose() seeks back and patches the header before closing.
            // We keep the MemoryStream reference to read the finalized data.
            var writer = _waveFileWriter;
            _waveFileWriter = null;
            writer?.Dispose();
            audioData = _memoryStream!.ToArray();
        }
        finally
        {
            CleanupRecordingResources();
        }

        _logger.LogInformation("Recording stopped ({Size} bytes)", audioData.Length);

        return Task.FromResult(audioData);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_recordingLock)
        {
            _waveFileWriter?.Write(e.Buffer, 0, e.BytesRecorded);
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

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            _logger.LogError(e.Exception, "Recording error");
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

        StopMaxDurationTimer();

        if (IsRecording)
            try { _waveIn!.StopRecording(); } catch (Exception ex) { _logger.LogDebug(ex, "Best-effort StopRecording during disposal"); }

        CleanupRecordingResources();
    }
}
