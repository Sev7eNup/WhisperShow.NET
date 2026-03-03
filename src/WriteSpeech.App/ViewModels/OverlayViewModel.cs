using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Audio;
using WriteSpeech.Core.Services.Configuration;
using WriteSpeech.Core.Services.History;
using WriteSpeech.Core.Services.IDE;
using WriteSpeech.Core.Services.Modes;
using WriteSpeech.Core.Services.Snippets;
using WriteSpeech.Core.Services.Statistics;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Core.Services.TextInsertion;
using WriteSpeech.Core.Services.Transcription;


namespace WriteSpeech.App.ViewModels;

public partial class OverlayViewModel : ObservableObject, IDisposable
{
    private readonly RecordingController _recordingController;
    private readonly TranscriptionPipeline _transcriptionPipeline;
    private readonly ITextInsertionService _textInsertionService;
    private readonly IWindowFocusService _windowFocusService;
    private readonly ISelectedTextService _selectedTextService;
    private readonly IUsageStatsService _statsService;
    private readonly ITranscriptionHistoryService _historyService;
    private readonly ISettingsPersistenceService _persistenceService;
    private readonly IDispatcherService _dispatcher;
    private readonly ILogger<OverlayViewModel> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly IDisposable? _optionsChangeRegistration;

    private IntPtr _previousForegroundWindow;
    private string? _activeProcessName;
    private string? _selectedText;
    private bool _isCommandMode;
    private bool _isTransitioning;
    private bool _isVadListeningLoop;

    private WriteSpeechOptions Options => _optionsMonitor.CurrentValue;

    public bool MuteWhileDictating => Options.Audio.MuteWhileDictating;
    public bool IsOverlayAlwaysVisible => Options.Overlay.AlwaysVisible;
    public string PushToTalkHotkeyText =>
        $"Click \"{Settings.GeneralSettingsViewModel.FormatKeys(Options.Hotkey.PushToTalk.Modifiers, Options.Hotkey.PushToTalk.Key, Options.Hotkey.PushToTalk.MouseButton)}\" to start dictating";

    [ObservableProperty]
    private RecordingState _state = RecordingState.Idle;

    [ObservableProperty]
    private string? _transcribedText;

    [ObservableProperty]
    private float _audioLevel;

    private readonly float[] _waveformLevels = new float[20];
    private readonly Lock _waveformLock = new();
    public event EventHandler? WaveformUpdated;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _currentProviderName = string.Empty;

    [ObservableProperty]
    private string _recordingTimerText = "0:00";

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _streamingText;

    [ObservableProperty]
    private bool _isCommandModeActive;

    // --- Overlay position ---
    public double PositionX { get; private set; }
    public double PositionY { get; private set; }

    public OverlayViewModel(
        RecordingController recordingController,
        TranscriptionPipeline transcriptionPipeline,
        ITextInsertionService textInsertionService,
        IWindowFocusService windowFocusService,
        ISelectedTextService selectedTextService,
        IUsageStatsService statsService,
        ITranscriptionHistoryService historyService,
        ISettingsPersistenceService persistenceService,
        IDispatcherService dispatcher,
        ILogger<OverlayViewModel> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        _recordingController = recordingController;
        _transcriptionPipeline = transcriptionPipeline;
        _textInsertionService = textInsertionService;
        _windowFocusService = windowFocusService;
        _selectedTextService = selectedTextService;
        _statsService = statsService;
        _historyService = historyService;
        _persistenceService = persistenceService;
        _dispatcher = dispatcher;
        _logger = logger;
        _optionsMonitor = optionsMonitor;

        PositionX = optionsMonitor.CurrentValue.Overlay.PositionX;
        PositionY = optionsMonitor.CurrentValue.Overlay.PositionY;

        // Wire RecordingController events
        _recordingController.AudioLevelChanged += OnControllerAudioLevelChanged;
        _recordingController.RecordingError += OnRecordingError;
        _recordingController.MaxDurationReached += OnMaxDurationReached;
        _recordingController.SpeechStarted += OnSpeechStarted;
        _recordingController.SilenceDetected += OnSilenceDetected;
        _recordingController.RecordingTimerTick += OnRecordingTimerTick;
        _recordingController.AutoDismissExpired += OnAutoDismissExpired;

        // Wire TranscriptionPipeline events
        _transcriptionPipeline.StatusChanged += OnPipelineStatusChanged;
        _transcriptionPipeline.StreamingTextChanged += OnPipelineStreamingTextChanged;

        _optionsChangeRegistration = _optionsMonitor.OnChange(OnOptionsChanged);

        UpdateProviderName();
    }

    /// <summary>
    /// Creates an OverlayViewModel from the original 18 dependencies. Used by tests
    /// to preserve the existing CreateViewModel helper pattern.
    /// </summary>
    internal static OverlayViewModel CreateForTests(
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
        IIDEDetectionService ideDetectionService,
        IIDEContextService ideContextService,
        IModeService modeService,
        ISelectedTextService selectedTextService,
        IDispatcherService dispatcher,
        ISettingsPersistenceService persistenceService,
        ILogger<OverlayViewModel> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        var recordingController = new RecordingController(
            audioService, mutingService, soundEffects, dispatcher,
            NullLoggerFactory.Instance.CreateLogger<RecordingController>());

        var transcriptionPipeline = new TranscriptionPipeline(
            providerFactory, correctionFactory, combinedService,
            snippetService, modeService, ideDetectionService, ideContextService,
            NullLoggerFactory.Instance.CreateLogger<TranscriptionPipeline>());

        return new OverlayViewModel(
            recordingController, transcriptionPipeline,
            textInsertionService, windowFocusService, selectedTextService,
            statsService, historyService, persistenceService,
            dispatcher, logger, optionsMonitor);
    }

    // --- RecordingController event handlers ---

    private void OnControllerAudioLevelChanged(object? sender, float level)
        => _dispatcher.Invoke(() => AudioLevel = level);

    private void OnRecordingError(object? sender, Exception ex)
    {
        _dispatcher.Invoke(() =>
        {
            if (State is not (RecordingState.Recording or RecordingState.Listening)) return;

            _isVadListeningLoop = false;
            _recordingController.StopRecordingTimer();
            if (MuteWhileDictating)
                _recordingController.UnmuteAll();

            _logger.LogError(ex, "Recording device error during active recording");
            ErrorMessage = $"Recording device error: {SanitizeErrorMessage(ex)}";
            State = RecordingState.Error;
            _recordingController.PlayError();
            _recordingController.StartAutoDismissTimer(Options.Overlay.AutoDismissSeconds);
        });
    }

    private async void OnMaxDurationReached(object? sender, EventArgs e)
    {
        try
        {
            await _dispatcher.InvokeAsync(async () =>
            {
                if (State == RecordingState.Recording)
                {
                    _logger.LogInformation("Max recording duration reached, auto-stopping");
                    await StopAndTranscribeAsync();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Max duration handler failed");
        }
    }

    private void OnSpeechStarted(object? sender, EventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            if (State != RecordingState.Listening) return;

            _recordingController.PlayStartRecording();
            State = RecordingState.Recording;
            RecordingTimerText = "0:00";
            _recordingController.StartRecordingTimer();
            _logger.LogInformation("State: Listening -> Recording (VAD speech detected)");
        });
    }

    private async void OnSilenceDetected(object? sender, EventArgs e)
    {
        try
        {
            await _dispatcher.InvokeAsync(async () =>
            {
                if (State != RecordingState.Recording) return;

                var elapsed = _recordingController.GetElapsedSeconds();
                var minDuration = Options.Audio.VoiceActivity.MinRecordingSeconds;
                if (elapsed < minDuration)
                {
                    _logger.LogDebug("Silence detected but recording too short ({Elapsed:F1}s < {Min}s), ignoring",
                        elapsed, minDuration);
                    return;
                }

                _logger.LogInformation("VAD: Auto-stopping recording after silence");
                await StopAndTranscribeAsync();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Silence detection handler failed");
        }
    }

    private void OnRecordingTimerTick(string text)
        => _dispatcher.Invoke(() => RecordingTimerText = text);

    private void OnAutoDismissExpired(object? sender, EventArgs e)
    {
        if (State is RecordingState.Error or RecordingState.Result)
            DismissResult();
    }

    // --- TranscriptionPipeline event handlers ---

    private void OnPipelineStatusChanged(string text)
        => _dispatcher.Invoke(() => StatusText = text);

    private void OnPipelineStreamingTextChanged(string? text)
        => _dispatcher.Invoke(() => StreamingText = text);

    // --- Options change ---

    private void OnOptionsChanged(WriteSpeechOptions options, string? name)
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

    // --- Commands & State Machine ---

    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        if (_isTransitioning) return;

        switch (State)
        {
            case RecordingState.Idle when Options.Audio.VoiceActivity.Enabled:
                await StartListeningModeAsync();
                break;
            case RecordingState.Idle:
                await StartRecordingAsync();
                break;
            case RecordingState.Listening:
                StopListeningMode();
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
        if (_isTransitioning) return;
        if (State == RecordingState.Idle)
            await StartRecordingAsync();
    }

    /// <summary>
    /// Called by push-to-talk hotkey release. Only stops if recording.
    /// </summary>
    public async Task HotkeyStopRecordingAsync()
    {
        _logger.LogDebug("HotkeyStopRecordingAsync called (current state: {State})", State);
        if (_isTransitioning) return;
        if (State == RecordingState.Recording)
            await StopAndTranscribeAsync();
    }

    private async Task StartRecordingAsync()
    {
        _recordingController.CancelAutoDismissTimer();
        _isTransitioning = true;
        try
        {
            _previousForegroundWindow = GetTargetForegroundWindow();
            _activeProcessName = _windowFocusService.GetProcessName(_previousForegroundWindow);

            // Capture selected text BEFORE recording starts (focus is still on the previous window)
            _selectedText = await _selectedTextService.ReadSelectedTextAsync();
            var hasCorrectionCapability = Options.TextCorrection.Provider != TextCorrectionProvider.Off
                || (Options.TextCorrection.UseCombinedAudioModel && _transcriptionPipeline.IsCombinedModelAvailable);
            _isCommandMode = !string.IsNullOrWhiteSpace(_selectedText) && hasCorrectionCapability;
            IsCommandModeActive = _isCommandMode;

            _logger.LogInformation(
                "Starting recording (ForegroundWindow: 0x{Handle:X}, Process: {Process}, CommandMode: {CommandMode})",
                _previousForegroundWindow.ToInt64(), _activeProcessName ?? "unknown", _isCommandMode);

            // Prepare IDE context while user records (non-blocking)
            _transcriptionPipeline.PrepareIDEContext(_previousForegroundWindow, Options,
                h => _windowFocusService.GetProcessName(h));

            State = RecordingState.Recording;
            RecordingTimerText = "0:00";
            _recordingController.StartRecordingTimer();
            _logger.LogInformation("State: Idle -> Recording");
            await _recordingController.StartRecordingAsync(MuteWhileDictating);
        }
        catch (Exception ex)
        {
            _recordingController.StopRecordingTimer();
            _recordingController.UnmuteAll();
            _logger.LogError(ex, "Failed to start recording");
            ErrorMessage = $"Recording failed: {SanitizeErrorMessage(ex)}";
            State = RecordingState.Error;
            _recordingController.PlayError();
            _recordingController.StartAutoDismissTimer(Options.Overlay.AutoDismissSeconds);
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    private async Task StartListeningModeAsync()
    {
        _recordingController.CancelAutoDismissTimer();
        _isTransitioning = true;
        try
        {
            _previousForegroundWindow = GetTargetForegroundWindow();
            _activeProcessName = _windowFocusService.GetProcessName(_previousForegroundWindow);

            _transcriptionPipeline.PrepareIDEContext(_previousForegroundWindow, Options,
                h => _windowFocusService.GetProcessName(h));

            _isVadListeningLoop = true;
            State = RecordingState.Listening;
            await _recordingController.StartListeningAsync(MuteWhileDictating);

            _logger.LogInformation("State: Idle -> Listening (VAD mode)");
        }
        catch (Exception ex)
        {
            _isVadListeningLoop = false;
            _recordingController.UnmuteAll();
            _logger.LogError(ex, "Failed to start listening");
            ErrorMessage = $"Listening failed: {SanitizeErrorMessage(ex)}";
            State = RecordingState.Error;
            _recordingController.PlayError();
            _recordingController.StartAutoDismissTimer(Options.Overlay.AutoDismissSeconds);
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    private void StopListeningMode()
    {
        _isVadListeningLoop = false;
        _recordingController.StopListening(muteWhileDictating: false);
        _recordingController.StopRecordingTimer();
        if (MuteWhileDictating)
            _recordingController.UnmuteAll();
        State = RecordingState.Idle;
        _logger.LogInformation("State: Listening -> Idle (manual stop)");
    }

    private async Task StopAndTranscribeAsync()
    {
        _isTransitioning = true;
        _recordingController.StopRecordingTimer();
        _logger.LogInformation("State: Recording -> Transcribing");
        if (MuteWhileDictating)
            _recordingController.UnmuteAll();
        _recordingController.PlayStopRecording();

        try
        {
            StreamingText = null;
            State = RecordingState.Transcribing;
            var audioData = await _recordingController.StopRecordingAsync();

            if (audioData.Length < 1000)
            {
                _logger.LogWarning("Recording too short ({Size} bytes), discarding", audioData.Length);
                if (_isVadListeningLoop)
                {
                    await RestartListeningOrFallbackAsync("Failed to restart listening after short recording");
                    return;
                }
                ErrorMessage = "Recording too short. Please try again.";
                State = RecordingState.Error;
                _recordingController.StartAutoDismissTimer(Options.Overlay.AutoDismissSeconds);
                return;
            }

            var result = await _transcriptionPipeline.TranscribeAsync(
                audioData, Options, _activeProcessName, _selectedText, _isCommandMode);

            if (result is null)
            {
                _logger.LogWarning("Transcription returned empty text");
                if (_isVadListeningLoop)
                {
                    await RestartListeningOrFallbackAsync("Failed to restart listening after empty result");
                    return;
                }
                ErrorMessage = "No speech detected. Please try again.";
                State = RecordingState.Error;
                _recordingController.StartAutoDismissTimer(Options.Overlay.AutoDismissSeconds);
                return;
            }

            _logger.LogInformation("Transcription result: {Length} chars", result.Text.Length);
            TranscribedText = result.Text;

            // Record stats and history
            var duration = _recordingController.GetElapsedSeconds();
            var wordCount = result.Text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries).Length;
            _statsService.RecordTranscription(duration, audioData.Length, Options.Provider.ToString(), wordCount, result.CorrectionProvider);
            _historyService.AddEntry(result.Text, Options.Provider.ToString(), duration);

            // Auto-insert into the previously focused window
            await InsertTextAsync();

            if (_isVadListeningLoop)
            {
                // VAD listening loop: restart listening for next utterance
                try
                {
                    TranscribedText = null;
                    StreamingText = null;
                    await _recordingController.StartListeningAsync(muteWhileDictating: false);
                    State = RecordingState.Listening;
                    _logger.LogInformation("State: Transcribing -> Listening (VAD loop)");
                }
                catch (Exception listenEx)
                {
                    _logger.LogWarning(listenEx, "Failed to restart listening, falling back to Idle");
                    _isVadListeningLoop = false;
                    if (MuteWhileDictating)
                        _recordingController.UnmuteAll();
                    State = RecordingState.Idle;
                }
            }
            else if (Options.Overlay.ShowResultOverlay)
            {
                State = RecordingState.Result;
                _logger.LogInformation("State: Transcribing -> Result");
                _recordingController.StartAutoDismissTimer(Options.Overlay.AutoDismissSeconds);
            }
            else
            {
                State = RecordingState.Idle;
                _logger.LogInformation("State: Transcribing -> Idle (result overlay disabled)");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Transcription cancelled");
            if (_isVadListeningLoop)
                _isVadListeningLoop = false;
            if (MuteWhileDictating)
                _recordingController.UnmuteAll();
            State = RecordingState.Idle;
        }
        catch (Exception ex)
        {
            if (_isVadListeningLoop)
                _isVadListeningLoop = false;
            _statsService.RecordError();
            _logger.LogError(ex, "Transcription failed");
            ErrorMessage = $"Transcription failed: {SanitizeErrorMessage(ex)}";
            State = RecordingState.Error;
            _recordingController.PlayError();
            _recordingController.StartAutoDismissTimer(Options.Overlay.AutoDismissSeconds);
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    private async Task RestartListeningOrFallbackAsync(string failureMessage)
    {
        try
        {
            await _recordingController.StartListeningAsync(muteWhileDictating: false);
            State = RecordingState.Listening;
            _logger.LogInformation("State: Transcribing -> Listening (VAD loop restart)");
        }
        catch (Exception listenEx)
        {
            _logger.LogWarning(listenEx, failureMessage);
            _isVadListeningLoop = false;
            if (MuteWhileDictating) _recordingController.UnmuteAll();
            State = RecordingState.Idle;
        }
    }

    [RelayCommand]
    private async Task InsertTextAsync()
    {
        if (string.IsNullOrEmpty(TranscribedText)) return;

        _logger.LogInformation("Inserting transcribed text ({Length} chars)", TranscribedText.Length);

        // Restore focus to previously active window
        var focusRestored = await _windowFocusService.RestoreFocusAsync(_previousForegroundWindow);
        if (!focusRestored)
            _logger.LogWarning("Could not restore focus to target window — paste may go to wrong window");

        await _textInsertionService.InsertTextAsync(TranscribedText);
        _transcriptionPipeline.ClearIDEContext();
    }

    private IntPtr GetTargetForegroundWindow()
    {
        var hwnd = _windowFocusService.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId)
        {
            _logger.LogDebug("Foreground window 0x{Handle:X} belongs to own process, ignoring",
                hwnd.ToInt64());
            return IntPtr.Zero;
        }
        return hwnd;
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
        _recordingController.CancelAutoDismissTimer();
        var previousState = State;
        TranscribedText = null;
        StreamingText = null;
        ErrorMessage = null;
        _selectedText = null;
        _isCommandMode = false;
        IsCommandModeActive = false;

        if (_isVadListeningLoop)
        {
            _isVadListeningLoop = false;
            _recordingController.StopListening(muteWhileDictating: false);
        }
        if (MuteWhileDictating && previousState is RecordingState.Listening or RecordingState.Recording)
            _recordingController.UnmuteAll();

        State = RecordingState.Idle;
        _logger.LogDebug("Result dismissed (was {PreviousState})", previousState);
    }

    // --- Waveform ---

    partial void OnAudioLevelChanged(float value)
    {
        lock (_waveformLock)
        {
            Array.Copy(_waveformLevels, 1, _waveformLevels, 0, _waveformLevels.Length - 1);
            _waveformLevels[^1] = value;
        }
        WaveformUpdated?.Invoke(this, EventArgs.Empty);
    }

    public float[] GetWaveformLevels()
    {
        lock (_waveformLock)
        {
            return (float[])_waveformLevels.Clone();
        }
    }

    public void ClearWaveform()
    {
        lock (_waveformLock)
        {
            Array.Clear(_waveformLevels);
        }
    }

    // --- Position persistence ---

    public void UpdatePosition(double x, double y)
    {
        PositionX = x;
        PositionY = y;
        _persistenceService.ScheduleUpdate(section =>
        {
            var overlay = SettingsViewModel.EnsureObject(section, "Overlay");
            overlay["PositionX"] = x;
            overlay["PositionY"] = y;
        });
    }

    public void UpdateProviderName()
    {
        CurrentProviderName = _transcriptionPipeline.GetProviderName(Options.Provider);
    }

    public void Dispose()
    {
        _recordingController.AudioLevelChanged -= OnControllerAudioLevelChanged;
        _recordingController.RecordingError -= OnRecordingError;
        _recordingController.MaxDurationReached -= OnMaxDurationReached;
        _recordingController.SpeechStarted -= OnSpeechStarted;
        _recordingController.SilenceDetected -= OnSilenceDetected;
        _recordingController.RecordingTimerTick -= OnRecordingTimerTick;
        _recordingController.AutoDismissExpired -= OnAutoDismissExpired;
        _transcriptionPipeline.StatusChanged -= OnPipelineStatusChanged;
        _transcriptionPipeline.StreamingTextChanged -= OnPipelineStreamingTextChanged;

        _transcriptionPipeline.Cancel();
        _recordingController.Dispose();
        _transcriptionPipeline.Dispose();
        _optionsChangeRegistration?.Dispose();

        // Ensure other apps are unmuted if we're disposed during recording
        try { _recordingController.UnmuteAll(); } catch (Exception ex) { _logger.LogDebug(ex, "Best-effort UnmuteAll during disposal"); }
    }

    internal static string SanitizeErrorMessage(Exception ex)
        => ErrorMessageHelper.SanitizeErrorMessage(ex);
}
