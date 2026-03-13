using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Voxwright.Core.Services;

namespace Voxwright.App.ViewModels;

/// <summary>
/// Shared microphone test logic used by both SetupWizardViewModel and GeneralSettingsViewModel.
/// Handles WaveInEvent lifecycle, RMS level computation, and dispatcher marshalling.
/// </summary>
public sealed class MicTestHelper : IDisposable
{
    private readonly IDispatcherService _dispatcher;
    private readonly ILogger _logger;
    private readonly Action<float> _onLevelChanged;
    private WaveInEvent? _waveIn;

    /// <summary>Whether a microphone test is currently active and capturing audio.</summary>
    public bool IsTesting { get; private set; }

    public MicTestHelper(IDispatcherService dispatcher, ILogger logger, Action<float> onLevelChanged)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(onLevelChanged);
        _dispatcher = dispatcher;
        _logger = logger;
        _onLevelChanged = onLevelChanged;
    }

    /// <summary>
    /// Opens the specified microphone device and begins capturing audio.
    /// RMS audio levels are computed from each buffer and dispatched to the callback on the UI thread.
    /// Any previously active test is stopped first.
    /// </summary>
    public void Start(int deviceIndex)
    {
        Stop();

        try
        {
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceIndex,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            IsTesting = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start mic test");
            Stop();
        }
    }

    /// <summary>Stops the active microphone test, releases the audio device, and resets the level to zero.</summary>
    public void Stop()
    {
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            try { _waveIn.StopRecording(); } catch (Exception ex) { _logger.LogDebug(ex, "Best-effort StopRecording during mic test cleanup"); }
            _waveIn.Dispose();
            _waveIn = null;
        }
        IsTesting = false;
        _onLevelChanged(0);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        double sumOfSquares = 0;
        int sampleCount = e.BytesRecorded / 2;
        for (int i = 0; i < e.BytesRecorded; i += 2)
        {
            short sample = BitConverter.ToInt16(e.Buffer, i);
            double normalized = sample / 32768.0;
            sumOfSquares += normalized * normalized;
        }

        float rms = sampleCount > 0 ? (float)Math.Sqrt(sumOfSquares / sampleCount) : 0;
        float level = Math.Min(rms * 3.5f, 1.0f);

        _dispatcher.Invoke(() => _onLevelChanged(level));
    }

    /// <summary>Stops any active test and releases the microphone device.</summary>
    public void Dispose() => Stop();
}
