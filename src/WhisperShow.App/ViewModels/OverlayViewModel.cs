using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services;
using WhisperShow.Core.Services.Audio;
using WhisperShow.Core.Services.Snippets;
using WhisperShow.Core.Services.Configuration;
using WhisperShow.Core.Services.TextCorrection;
using WhisperShow.Core.Services.TextInsertion;
using WhisperShow.Core.Services.History;
using WhisperShow.Core.Services.Statistics;
using WhisperShow.Core.Services.Transcription;


namespace WhisperShow.App.ViewModels;

public partial class OverlayViewModel : ObservableObject, IDisposable
{
    private readonly IAudioRecordingService _audioService;
    private readonly IAudioMutingService _mutingService;
    private readonly TranscriptionProviderFactory _providerFactory;
    private readonly ITextInsertionService _textInsertionService;
    private readonly TextCorrectionProviderFactory _correctionFactory;
    private readonly ICombinedTranscriptionCorrectionService _combinedService;
    private readonly ISoundEffectService _soundEffects;
    private readonly ISnippetService _snippetService;
    private readonly IUsageStatsService _statsService;
    private readonly ITranscriptionHistoryService _historyService;
    private readonly IWindowFocusService _windowFocusService;
    private readonly IDispatcherService _dispatcher;
    private readonly ISettingsPersistenceService _persistenceService;
    private readonly ILogger<OverlayViewModel> _logger;
    private readonly IOptionsMonitor<WhisperShowOptions> _optionsMonitor;
    private readonly IDisposable? _optionsChangeRegistration;
    private IntPtr _previousForegroundWindow;
    private CancellationTokenSource? _autoDismissCts;
    private DateTime _recordingStartTime;
    private System.Timers.Timer? _recordingTimer;

    private WhisperShowOptions Options => _optionsMonitor.CurrentValue;

    public bool MuteWhileDictating => Options.Audio.MuteWhileDictating;
    public bool IsOverlayAlwaysVisible => Options.Overlay.AlwaysVisible;
    public string PushToTalkHotkeyText =>
        $"Click or hold {Options.Hotkey.PushToTalk.Modifiers.Replace(",", " +").Replace("Control", "Ctrl")} + {Options.Hotkey.PushToTalk.Key} to start dictating";

    [ObservableProperty]
    private RecordingState _state = RecordingState.Idle;

    [ObservableProperty]
    private string? _transcribedText;

    [ObservableProperty]
    private float _audioLevel;

    private readonly float[] _waveformLevels = new float[20];
    public event EventHandler? WaveformUpdated;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _currentProviderName = string.Empty;

    [ObservableProperty]
    private string _recordingTimerText = "0:00";

    [ObservableProperty]
    private string _statusText = string.Empty;

    // --- Overlay position ---
    public double PositionX { get; private set; }
    public double PositionY { get; private set; }

    public OverlayViewModel(
        IAudioRecordingService audioService,
        IAudioMutingService mutingService,
        TranscriptionProviderFactory providerFactory,
        ITextInsertionService textInsertionService,
        TextCorrectionProviderFactory correctionFactory,
        ICombinedTranscriptionCorrectionService combinedService,
        ISnippetService snippetService,
        ISoundEffectService soundEffects,
        IUsageStatsService statsService,
        ITranscriptionHistoryService historyService,
        IWindowFocusService windowFocusService,
        IDispatcherService dispatcher,
        ISettingsPersistenceService persistenceService,
        ILogger<OverlayViewModel> logger,
        IOptionsMonitor<WhisperShowOptions> optionsMonitor)
    {
        _audioService = audioService;
        _mutingService = mutingService;
        _providerFactory = providerFactory;
        _textInsertionService = textInsertionService;
        _correctionFactory = correctionFactory;
        _combinedService = combinedService;
        _snippetService = snippetService;
        _soundEffects = soundEffects;
        _statsService = statsService;
        _historyService = historyService;
        _windowFocusService = windowFocusService;
        _dispatcher = dispatcher;
        _persistenceService = persistenceService;
        _logger = logger;
        _optionsMonitor = optionsMonitor;

        PositionX = optionsMonitor.CurrentValue.Overlay.PositionX;
        PositionY = optionsMonitor.CurrentValue.Overlay.PositionY;

        _audioService.AudioLevelChanged += (_, level) =>
            _dispatcher.Invoke(() => AudioLevel = level);

        _optionsChangeRegistration = _optionsMonitor.OnChange(OnOptionsChanged);

        UpdateProviderName();
    }

    private void OnOptionsChanged(WhisperShowOptions options, string? name)
    {
        UpdateProviderName();
        OnPropertyChanged(nameof(MuteWhileDictating));
        OnPropertyChanged(nameof(IsOverlayAlwaysVisible));
    }

    partial void OnStateChanged(RecordingState value)
    {
        if (value != RecordingState.Transcribing)
            StatusText = string.Empty;
    }

    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        switch (State)
        {
            case RecordingState.Idle:
                await StartRecordingAsync();
                break;
            case RecordingState.Recording:
                await StopAndTranscribeAsync();
                break;
            case RecordingState.Result:
            case RecordingState.Error:
                DismissResult();
                break;
        }
    }

    /// <summary>
    /// Called by push-to-talk hotkey press. Only starts if idle.
    /// </summary>
    public async Task HotkeyStartRecordingAsync()
    {
        _logger.LogDebug("HotkeyStartRecordingAsync called (current state: {State})", State);
        if (State == RecordingState.Idle)
            await StartRecordingAsync();
    }

    /// <summary>
    /// Called by push-to-talk hotkey release. Only stops if recording.
    /// </summary>
    public async Task HotkeyStopRecordingAsync()
    {
        _logger.LogDebug("HotkeyStopRecordingAsync called (current state: {State})", State);
        if (State == RecordingState.Recording)
            await StopAndTranscribeAsync();
    }

    private async Task StartRecordingAsync()
    {
        CancelAutoDismissTimer();
        try
        {
            _previousForegroundWindow = _windowFocusService.GetForegroundWindow();
            _logger.LogInformation("Starting recording (ForegroundWindow: 0x{Handle:X})",
                _previousForegroundWindow.ToInt64());
            if (MuteWhileDictating)
                _mutingService.MuteOtherApplications();
            _soundEffects.PlayStartRecording();
            State = RecordingState.Recording;
            StartRecordingTimer();
            _logger.LogInformation("State: Idle -> Recording");
            await _audioService.StartRecordingAsync();
        }
        catch (Exception ex)
        {
            StopRecordingTimer();
            _logger.LogError(ex, "Failed to start recording");
            ErrorMessage = $"Recording failed: {ex.Message}";
            State = RecordingState.Error;
            _soundEffects.PlayError();
            StartAutoDismissTimer();
        }
    }

    private async Task StopAndTranscribeAsync()
    {
        StopRecordingTimer();
        _logger.LogInformation("State: Recording -> Transcribing");
        if (MuteWhileDictating)
            _mutingService.UnmuteAll();
        _soundEffects.PlayStopRecording();
        try
        {
            State = RecordingState.Transcribing;
            var audioData = await _audioService.StopRecordingAsync();

            if (audioData.Length < 1000)
            {
                _logger.LogWarning("Recording too short ({Size} bytes), discarding", audioData.Length);
                ErrorMessage = "Recording too short. Please try again.";
                State = RecordingState.Error;
                StartAutoDismissTimer();
                return;
            }

            string text;

            // Fast path: combined audio model (transcription + correction in one API call)
            if (Options.TextCorrection.UseCombinedAudioModel && _combinedService.IsAvailable)
            {
                _logger.LogInformation("Using combined transcription+correction pipeline");
                StatusText = "Transcribing & correcting...";
                try
                {
                    text = await _combinedService.TranscribeAndCorrectAsync(audioData, Options.Language);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Combined audio model failed, falling back to standard pipeline");
                    text = await StandardTranscribeAsync(audioData);
                }
            }
            else
            {
                _logger.LogInformation("Using standard transcription pipeline (Provider: {Provider})", Options.Provider);
                text = await StandardTranscribeAsync(audioData);
            }

            // Apply snippet expansions after transcription + correction
            text = _snippetService.ApplySnippets(text);

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Transcription returned empty text");
                ErrorMessage = "No speech detected. Please try again.";
                State = RecordingState.Error;
                StartAutoDismissTimer();
                return;
            }

            _logger.LogInformation("Transcription result: {Length} chars", text.Length);
            TranscribedText = text;

            // Record stats and history
            var duration = (DateTime.UtcNow - _recordingStartTime).TotalSeconds;
            _statsService.RecordTranscription(duration, audioData.Length, Options.Provider.ToString());
            _historyService.AddEntry(text, Options.Provider.ToString(), duration);

            // Auto-insert into the previously focused window
            await InsertTextAsync();

            if (Options.Overlay.ShowResultOverlay)
            {
                // Show result panel with transcribed text, auto-dismiss after configured timeout
                State = RecordingState.Result;
                _logger.LogInformation("State: Transcribing -> Result");
                StartAutoDismissTimer();
            }
            else
            {
                State = RecordingState.Idle;
                _logger.LogInformation("State: Transcribing -> Idle (result overlay disabled)");
            }
        }
        catch (Exception ex)
        {
            _statsService.RecordError();
            _logger.LogError(ex, "Transcription failed");
            ErrorMessage = $"Transcription failed: {ex.Message}";
            State = RecordingState.Error;
            _soundEffects.PlayError();
            StartAutoDismissTimer();
        }
    }

    private async Task<string> StandardTranscribeAsync(byte[] audioData)
    {
        var provider = _providerFactory.GetProvider(Options.Provider);
        StatusText = provider.IsModelLoaded ? "Transcribing..." : "Loading transcription model...";
        var result = await provider.TranscribeAsync(audioData, Options.Language);

        var text = result.Text;

        var corrector = _correctionFactory.GetProvider(Options.TextCorrection.Provider);
        _logger.LogInformation("Text correction: {Provider}", Options.TextCorrection.Provider);
        if (corrector is not null && !string.IsNullOrWhiteSpace(text))
        {
            StatusText = corrector.IsModelLoaded ? "Correcting text..." : "Loading correction model...";
            text = await corrector.CorrectAsync(text, Options.Language);
        }

        return text;
    }

    [RelayCommand]
    private async Task InsertTextAsync()
    {
        if (string.IsNullOrEmpty(TranscribedText)) return;

        _logger.LogInformation("Inserting transcribed text ({Length} chars)", TranscribedText.Length);

        // Restore focus to previously active window
        await _windowFocusService.RestoreFocusAsync(_previousForegroundWindow);

        await _textInsertionService.InsertTextAsync(TranscribedText);
    }

    [RelayCommand]
    private void CopyText()
    {
        if (string.IsNullOrEmpty(TranscribedText)) return;
        _dispatcher.Invoke(() => System.Windows.Clipboard.SetText(TranscribedText));
    }

    [RelayCommand]
    private void DismissResult()
    {
        CancelAutoDismissTimer();
        var previousState = State;
        TranscribedText = null;
        ErrorMessage = null;
        State = RecordingState.Idle;
        _logger.LogDebug("Result dismissed (was {PreviousState})", previousState);
    }

    private void StartRecordingTimer()
    {
        _recordingStartTime = DateTime.UtcNow;
        RecordingTimerText = "0:00";
        _recordingTimer = new System.Timers.Timer(1000);
        _recordingTimer.Elapsed += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - _recordingStartTime;
            _dispatcher.Invoke(() =>
                RecordingTimerText = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}");
        };
        _recordingTimer.Start();
    }

    private void StopRecordingTimer()
    {
        _recordingTimer?.Stop();
        _recordingTimer?.Dispose();
        _recordingTimer = null;
    }

    private void StartAutoDismissTimer() => _ = StartAutoDismissTimerAsync();

    private async Task StartAutoDismissTimerAsync()
    {
        _autoDismissCts?.Cancel();
        _autoDismissCts = new CancellationTokenSource();
        var token = _autoDismissCts.Token;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Options.Overlay.AutoDismissSeconds), token);
            if (State is RecordingState.Error or RecordingState.Result)
                DismissResult();
        }
        catch (TaskCanceledException) { }
    }

    private void CancelAutoDismissTimer()
    {
        _autoDismissCts?.Cancel();
        _autoDismissCts = null;
    }

    partial void OnAudioLevelChanged(float value)
    {
        Array.Copy(_waveformLevels, 1, _waveformLevels, 0, _waveformLevels.Length - 1);
        _waveformLevels[^1] = value;
        WaveformUpdated?.Invoke(this, EventArgs.Empty);
    }

    public float[] GetWaveformLevels() => _waveformLevels;

    public void ClearWaveform()
    {
        Array.Clear(_waveformLevels);
    }

    public void UpdatePosition(double x, double y)
    {
        PositionX = x;
        PositionY = y;
        _persistenceService.ScheduleUpdate(section =>
        {
            section["Overlay"]!["PositionX"] = x;
            section["Overlay"]!["PositionY"] = y;
        });
    }

    public void UpdateProviderName()
    {
        var provider = _providerFactory.GetProvider(Options.Provider);
        CurrentProviderName = provider.ProviderName;
    }

    public void Dispose()
    {
        _autoDismissCts?.Cancel();
        _autoDismissCts?.Dispose();
        _autoDismissCts = null;
        _recordingTimer?.Stop();
        _recordingTimer?.Dispose();
        _recordingTimer = null;
        _optionsChangeRegistration?.Dispose();
    }
}
