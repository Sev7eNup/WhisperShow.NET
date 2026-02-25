using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Audio;
using WriteSpeech.Core.Services.Snippets;
using WriteSpeech.Core.Services.Configuration;
using WriteSpeech.Core.Services.IDE;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Core.Services.TextInsertion;
using WriteSpeech.Core.Services.History;
using WriteSpeech.Core.Services.Statistics;
using WriteSpeech.Core.Services.Modes;
using WriteSpeech.Core.Services.Transcription;


namespace WriteSpeech.App.ViewModels;

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
    private readonly IIDEDetectionService _ideDetectionService;
    private readonly IIDEContextService _ideContextService;
    private readonly IDispatcherService _dispatcher;
    private readonly IModeService _modeService;
    private readonly ISelectedTextService _selectedTextService;
    private readonly ISettingsPersistenceService _persistenceService;
    private readonly ILogger<OverlayViewModel> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly IDisposable? _optionsChangeRegistration;
    private IntPtr _previousForegroundWindow;
    private string? _activeProcessName;
    private string? _selectedText;
    private bool _isCommandMode;
    private CancellationTokenSource? _autoDismissCts;
    private CancellationTokenSource? _transcriptionCts;
    private DateTime _recordingStartTime;
    private System.Timers.Timer? _recordingTimer;

    private WriteSpeechOptions Options => _optionsMonitor.CurrentValue;

    public bool MuteWhileDictating => Options.Audio.MuteWhileDictating;
    public bool IsOverlayAlwaysVisible => Options.Overlay.AlwaysVisible;
    public string PushToTalkHotkeyText =>
        $"Click or hold {Settings.GeneralSettingsViewModel.FormatKeys(Options.Hotkey.PushToTalk.Modifiers, Options.Hotkey.PushToTalk.Key, Options.Hotkey.PushToTalk.MouseButton)} to start dictating";

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
    private bool _isCommandModeActive;

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
        IIDEDetectionService ideDetectionService,
        IIDEContextService ideContextService,
        IModeService modeService,
        ISelectedTextService selectedTextService,
        IDispatcherService dispatcher,
        ISettingsPersistenceService persistenceService,
        ILogger<OverlayViewModel> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
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
        _ideDetectionService = ideDetectionService;
        _ideContextService = ideContextService;
        _modeService = modeService;
        _selectedTextService = selectedTextService;
        _dispatcher = dispatcher;
        _persistenceService = persistenceService;
        _logger = logger;
        _optionsMonitor = optionsMonitor;

        PositionX = optionsMonitor.CurrentValue.Overlay.PositionX;
        PositionY = optionsMonitor.CurrentValue.Overlay.PositionY;

        _audioService.AudioLevelChanged += OnAudioLevelChanged;
        _audioService.RecordingError += OnRecordingError;
        _audioService.MaxDurationReached += OnMaxDurationReached;

        _optionsChangeRegistration = _optionsMonitor.OnChange(OnOptionsChanged);

        UpdateProviderName();
    }

    private void OnAudioLevelChanged(object? sender, float level)
        => _dispatcher.Invoke(() => AudioLevel = level);

    private void OnRecordingError(object? sender, Exception ex)
    {
        _dispatcher.Invoke(() =>
        {
            if (State != RecordingState.Recording) return;

            StopRecordingTimer();
            if (MuteWhileDictating)
                _mutingService.UnmuteAll();

            _logger.LogError(ex, "Recording device error during active recording");
            ErrorMessage = $"Recording device error: {ex.Message}";
            State = RecordingState.Error;
            _soundEffects.PlayError();
            StartAutoDismissTimer();
        });
    }

    private void OnMaxDurationReached(object? sender, EventArgs e)
    {
        _dispatcher.Invoke(async () =>
        {
            if (State == RecordingState.Recording)
            {
                _logger.LogInformation("Max recording duration reached, auto-stopping");
                await StopAndTranscribeAsync();
            }
        });
    }

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
            _activeProcessName = _windowFocusService.GetProcessName(_previousForegroundWindow);

            // Capture selected text BEFORE recording starts (focus is still on the previous window)
            _selectedText = await _selectedTextService.ReadSelectedTextAsync();
            _isCommandMode = !string.IsNullOrWhiteSpace(_selectedText);
            IsCommandModeActive = _isCommandMode;

            _logger.LogInformation(
                "Starting recording (ForegroundWindow: 0x{Handle:X}, Process: {Process}, CommandMode: {CommandMode})",
                _previousForegroundWindow.ToInt64(), _activeProcessName ?? "unknown", _isCommandMode);

            // Prepare IDE context while user records (non-blocking)
            PrepareIDEContext();

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

        _transcriptionCts?.Cancel();
        _transcriptionCts?.Dispose();
        _transcriptionCts = new CancellationTokenSource();
        var ct = _transcriptionCts.Token;

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
            string correctionProvider;

            // Fast path: combined audio model (transcription + correction in one API call)
            if (Options.TextCorrection.UseCombinedAudioModel && _combinedService.IsAvailable)
            {
                _logger.LogInformation("Using combined transcription+correction pipeline");
                StatusText = _isCommandMode ? "Processing command..." : "Transcribing & correcting...";
                try
                {
                    string? combinedPrompt;
                    if (_isCommandMode && !string.IsNullOrWhiteSpace(_selectedText))
                    {
                        combinedPrompt = TextCorrectionDefaults.VoiceCommandCombinedSystemPrompt
                            + $"\n\nSelected text:\n{_selectedText}";
                    }
                    else
                    {
                        combinedPrompt = _modeService.ResolveCombinedSystemPrompt(_activeProcessName);
                    }

                    text = await _combinedService.TranscribeAndCorrectAsync(audioData, Options.Language, combinedPrompt, ct);
                    correctionProvider = "Combined";
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Combined audio model failed, falling back to standard pipeline");
                    text = await StandardTranscribeAsync(audioData, ct);
                    correctionProvider = Options.TextCorrection.Provider.ToString();
                }
            }
            else if (_isCommandMode && !string.IsNullOrWhiteSpace(_selectedText))
            {
                // Command mode via standard pipeline: transcribe only (skip correction),
                // then transform the selected text using the voice command
                _logger.LogInformation("Using standard transcription pipeline in command mode");
                var provider = _providerFactory.GetProvider(Options.Provider);
                StatusText = provider.IsModelLoaded ? "Transcribing..." : "Loading transcription model...";
                var result = await provider.TranscribeAsync(audioData, Options.Language, ct);
                text = await TransformTextAsync(result.Text, _selectedText, ct);
                correctionProvider = "VoiceCommand";
            }
            else
            {
                _logger.LogInformation("Using standard transcription pipeline (Provider: {Provider})", Options.Provider);
                text = await StandardTranscribeAsync(audioData, ct);
                correctionProvider = Options.TextCorrection.Provider.ToString();
            }

            // Apply snippet expansions only in normal dictation mode
            if (!_isCommandMode)
            {
                text = _snippetService.ApplySnippets(text);
            }

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
            var wordCount = text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries).Length;
            _statsService.RecordTranscription(duration, audioData.Length, Options.Provider.ToString(), wordCount, correctionProvider);
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
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Transcription cancelled");
            State = RecordingState.Idle;
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

    private async Task<string> StandardTranscribeAsync(byte[] audioData, CancellationToken ct = default)
    {
        var provider = _providerFactory.GetProvider(Options.Provider);
        StatusText = provider.IsModelLoaded ? "Transcribing..." : "Loading transcription model...";
        var result = await provider.TranscribeAsync(audioData, Options.Language, ct);

        var text = result.Text;

        var corrector = _correctionFactory.GetProvider(Options.TextCorrection.Provider);
        _logger.LogInformation("Text correction: {Provider}", Options.TextCorrection.Provider);
        if (corrector is not null && !string.IsNullOrWhiteSpace(text))
        {
            StatusText = corrector.IsModelLoaded ? "Correcting text..." : "Loading correction model...";
            var modePrompt = _modeService.ResolveSystemPrompt(_activeProcessName);
            text = await corrector.CorrectAsync(text, Options.Language, modePrompt, ct);
        }

        return text;
    }

    private async Task<string> TransformTextAsync(string voiceCommand, string selectedText, CancellationToken ct)
    {
        _logger.LogInformation("Command mode: transforming text ({SelectedLength} chars) with command: {Command}",
            selectedText.Length, voiceCommand);
        StatusText = "Transforming text...";

        var corrector = _correctionFactory.GetProvider(Options.TextCorrection.Provider);
        if (corrector is null)
        {
            _logger.LogWarning("Voice command mode requires text correction to be enabled — returning raw transcription");
            return voiceCommand;
        }

        var userMessage = $"Selected text:\n{selectedText}\n\nVoice command: {voiceCommand}";
        return await corrector.CorrectAsync(userMessage, Options.Language, TextCorrectionDefaults.VoiceCommandSystemPrompt, ct);
    }

    [RelayCommand]
    private async Task InsertTextAsync()
    {
        if (string.IsNullOrEmpty(TranscribedText)) return;

        _logger.LogInformation("Inserting transcribed text ({Length} chars)", TranscribedText.Length);

        // Restore focus to previously active window
        await _windowFocusService.RestoreFocusAsync(_previousForegroundWindow);

        await _textInsertionService.InsertTextAsync(TranscribedText);
        _ideContextService.Clear();
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
        _selectedText = null;
        _isCommandMode = false;
        IsCommandModeActive = false;
        State = RecordingState.Idle;
        _logger.LogDebug("Result dismissed (was {PreviousState})", previousState);
    }

    private void PrepareIDEContext()
    {
        var integration = Options.Integration;
        if (!integration.VariableRecognition && !integration.FileTagging)
        {
            _ideContextService.Clear();
            return;
        }

        try
        {
            var ideInfo = _ideDetectionService.DetectIDE(_previousForegroundWindow);
            if (ideInfo?.WorkspacePath is not null)
            {
                // Fire-and-forget — context will be ready by the time correction runs
                _ = _ideContextService.PrepareContextAsync(
                    ideInfo.WorkspacePath,
                    integration.VariableRecognition,
                    integration.FileTagging)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            _logger.LogWarning(t.Exception?.InnerException, "IDE context preparation failed");
                    }, TaskContinuationOptions.OnlyOnFaulted);
            }
            else
            {
                _ideContextService.Clear();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IDE context preparation skipped");
            _ideContextService.Clear();
        }
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
        _autoDismissCts?.Dispose();
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
        _autoDismissCts?.Dispose();
        _autoDismissCts = null;
    }

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
        var provider = _providerFactory.GetProvider(Options.Provider);
        CurrentProviderName = provider.ProviderName;
    }

    public void Dispose()
    {
        _audioService.AudioLevelChanged -= OnAudioLevelChanged;
        _audioService.RecordingError -= OnRecordingError;
        _audioService.MaxDurationReached -= OnMaxDurationReached;
        _transcriptionCts?.Cancel();
        _transcriptionCts?.Dispose();
        _transcriptionCts = null;
        _autoDismissCts?.Cancel();
        _autoDismissCts?.Dispose();
        _autoDismissCts = null;
        _recordingTimer?.Stop();
        _recordingTimer?.Dispose();
        _recordingTimer = null;
        _optionsChangeRegistration?.Dispose();
    }
}
