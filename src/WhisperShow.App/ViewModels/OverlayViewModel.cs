using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WhisperShow.App.Services;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services.Audio;
using WhisperShow.Core.Services.Snippets;
using WhisperShow.Core.Services.TextCorrection;
using WhisperShow.Core.Services.TextInsertion;
using WhisperShow.Core.Services.History;
using WhisperShow.Core.Services.Statistics;
using WhisperShow.Core.Services.Transcription;


namespace WhisperShow.App.ViewModels;

public partial class OverlayViewModel : ObservableObject
{
    private readonly IAudioRecordingService _audioService;
    private readonly IAudioMutingService _mutingService;
    private readonly TranscriptionProviderFactory _providerFactory;
    private readonly ITextInsertionService _textInsertionService;
    private readonly TextCorrectionProviderFactory _correctionFactory;
    private readonly ICombinedTranscriptionCorrectionService _combinedService;
    private readonly SoundEffectService _soundEffects;
    private readonly ISnippetService _snippetService;
    private readonly IUsageStatsService _statsService;
    private readonly ITranscriptionHistoryService _historyService;
    private readonly ILogger<OverlayViewModel> _logger;
    private readonly SettingsViewModel _settings;
    private IntPtr _previousForegroundWindow;
    private CancellationTokenSource? _autoDismissCts;
    private DateTime _recordingStartTime;
    private System.Timers.Timer? _recordingTimer;

    public bool MuteWhileDictating { get; set; }
    public bool IsOverlayAlwaysVisible { get; set; }

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

    public OverlayViewModel(
        IAudioRecordingService audioService,
        IAudioMutingService mutingService,
        TranscriptionProviderFactory providerFactory,
        ITextInsertionService textInsertionService,
        TextCorrectionProviderFactory correctionFactory,
        ICombinedTranscriptionCorrectionService combinedService,
        ISnippetService snippetService,
        SoundEffectService soundEffects,
        IUsageStatsService statsService,
        ITranscriptionHistoryService historyService,
        ILogger<OverlayViewModel> logger,
        SettingsViewModel settingsViewModel)
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
        _logger = logger;
        _settings = settingsViewModel;

        MuteWhileDictating = _settings.MuteWhileDictating;
        IsOverlayAlwaysVisible = _settings.OverlayAlwaysVisible;

        _audioService.AudioLevelChanged += (_, level) =>
            Application.Current.Dispatcher.Invoke(() => AudioLevel = level);

        // Subscribe to live settings changes from SettingsViewModel
        settingsViewModel.PropertyChanged += OnSettingsChanged;

        UpdateProviderName();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SettingsViewModel settings) return;

        switch (e.PropertyName)
        {
            case nameof(SettingsViewModel.MuteWhileDictating):
                MuteWhileDictating = settings.MuteWhileDictating;
                _logger.LogDebug("MuteWhileDictating updated to {Value}", MuteWhileDictating);
                break;
            case nameof(SettingsViewModel.OverlayAlwaysVisible):
                IsOverlayAlwaysVisible = settings.OverlayAlwaysVisible;
                _logger.LogDebug("IsOverlayAlwaysVisible updated to {Value}", IsOverlayAlwaysVisible);
                break;
            case nameof(SettingsViewModel.SoundEffectsEnabled):
                _soundEffects.Enabled = settings.SoundEffectsEnabled;
                _logger.LogDebug("SoundEffects.Enabled updated to {Value}", _soundEffects.Enabled);
                break;
            case nameof(SettingsViewModel.Provider):
                UpdateProviderName();
                _logger.LogInformation("Transcription provider changed to {Provider}", settings.Provider);
                break;
        }
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
            _previousForegroundWindow = NativeMethods.GetForegroundWindow();
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
            if (_settings.UseCombinedAudioModel && _combinedService.IsAvailable)
            {
                _logger.LogInformation("Using combined transcription+correction pipeline");
                try
                {
                    text = await _combinedService.TranscribeAndCorrectAsync(audioData, _settings.SelectedLanguageCode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Combined audio model failed, falling back to standard pipeline");
                    text = await StandardTranscribeAsync(audioData);
                }
            }
            else
            {
                _logger.LogInformation("Using standard transcription pipeline (Provider: {Provider})", _settings.Provider);
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
            _statsService.RecordTranscription(duration, audioData.Length, _settings.Provider.ToString());
            _historyService.AddEntry(text, _settings.Provider.ToString(), duration);

            // Auto-insert into the previously focused window
            await InsertTextAsync();
            // Show result panel with transcribed text, auto-dismiss after configured timeout
            State = RecordingState.Result;
            _logger.LogInformation("State: Transcribing -> Result");
            StartAutoDismissTimer();
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
        var provider = _providerFactory.GetProvider(_settings.Provider);
        var result = await provider.TranscribeAsync(audioData, _settings.SelectedLanguageCode);

        var text = result.Text;

        var corrector = _correctionFactory.GetProvider(_settings.CorrectionProvider);
        _logger.LogInformation("Text correction: {Provider}", _settings.CorrectionProvider);
        if (corrector is not null && !string.IsNullOrWhiteSpace(text))
        {
            text = await corrector.CorrectAsync(text, _settings.SelectedLanguageCode);
        }

        return text;
    }

    [RelayCommand]
    private async Task InsertTextAsync()
    {
        if (string.IsNullOrEmpty(TranscribedText)) return;

        _logger.LogInformation("Inserting transcribed text ({Length} chars)", TranscribedText.Length);

        // Restore focus to previously active window
        if (_previousForegroundWindow != IntPtr.Zero)
        {
            _logger.LogDebug("Restoring focus to window 0x{Handle:X}", _previousForegroundWindow.ToInt64());
            var foregroundThread = NativeMethods.GetWindowThreadProcessId(
                NativeMethods.GetForegroundWindow(), out _);
            var currentThread = NativeMethods.GetCurrentThreadId();

            if (foregroundThread != currentThread)
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

            NativeMethods.SetForegroundWindow(_previousForegroundWindow);

            if (foregroundThread != currentThread)
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);

            await Task.Delay(150);
        }

        await _textInsertionService.InsertTextAsync(TranscribedText);
    }

    [RelayCommand]
    private void CopyText()
    {
        if (string.IsNullOrEmpty(TranscribedText)) return;
        Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(TranscribedText));
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
            Application.Current?.Dispatcher.Invoke(() =>
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

    private async void StartAutoDismissTimer()
    {
        _autoDismissCts?.Cancel();
        _autoDismissCts = new CancellationTokenSource();
        var token = _autoDismissCts.Token;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_settings.AutoDismissSeconds), token);
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

    public void UpdateProviderName()
    {
        var provider = _providerFactory.GetProvider(_settings.Provider);
        CurrentProviderName = provider.ProviderName;
    }
}
