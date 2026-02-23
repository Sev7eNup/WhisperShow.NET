using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using WhisperShow.Core.Configuration;

namespace WhisperShow.Core.Services.Audio;

public class AudioRecordingService : IAudioRecordingService
{
    private readonly ILogger<AudioRecordingService> _logger;
    private readonly IOptionsMonitor<WhisperShowOptions> _optionsMonitor;
    private WaveInEvent? _waveIn;
    private MemoryStream? _memoryStream;
    private WaveFileWriter? _waveFileWriter;
    private bool _disposed;

    public event EventHandler<float>? AudioLevelChanged;
    public bool IsRecording => _waveIn is not null;

    public AudioRecordingService(
        ILogger<AudioRecordingService> logger,
        IOptionsMonitor<WhisperShowOptions> optionsMonitor)
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
            BufferMilliseconds = 50,
            DeviceNumber = audioOptions.DeviceIndex
        };

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;
        _waveIn.StartRecording();

        _logger.LogInformation("Recording started (Device: {Device}, SampleRate: {SampleRate}Hz)",
            audioOptions.DeviceIndex, audioOptions.SampleRate);

        return Task.CompletedTask;
    }

    public Task<byte[]> StopRecordingAsync()
    {
        if (!IsRecording)
            throw new InvalidOperationException("Not recording.");

        _waveIn!.StopRecording();
        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.RecordingStopped -= OnRecordingStopped;
        _waveIn.Dispose();
        _waveIn = null;

        _waveFileWriter!.Flush();
        var audioData = _memoryStream!.ToArray();

        _waveFileWriter.Dispose();
        _waveFileWriter = null;
        _memoryStream.Dispose();
        _memoryStream = null;

        _logger.LogInformation("Recording stopped ({Size} bytes)", audioData.Length);

        return Task.FromResult(audioData);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _waveFileWriter?.Write(e.Buffer, 0, e.BytesRecorded);

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
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (IsRecording)
        {
            _waveIn!.StopRecording();
            _waveIn.Dispose();
        }
        _waveFileWriter?.Dispose();
        _memoryStream?.Dispose();
    }
}
