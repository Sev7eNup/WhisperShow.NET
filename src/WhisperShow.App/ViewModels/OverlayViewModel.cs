using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services.Audio;
using WhisperShow.Core.Services.TextInsertion;
using WhisperShow.Core.Services.Transcription;

namespace WhisperShow.App.ViewModels;

public partial class OverlayViewModel : ObservableObject
{
    private readonly IAudioRecordingService _audioService;
    private readonly TranscriptionProviderFactory _providerFactory;
    private readonly ITextInsertionService _textInsertionService;
    private readonly ILogger<OverlayViewModel> _logger;
    private readonly WhisperShowOptions _options;
    private IntPtr _previousForegroundWindow;

    [ObservableProperty]
    private RecordingState _state = RecordingState.Idle;

    [ObservableProperty]
    private string? _transcribedText;

    [ObservableProperty]
    private float _audioLevel;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _currentProviderName = string.Empty;

    public OverlayViewModel(
        IAudioRecordingService audioService,
        TranscriptionProviderFactory providerFactory,
        ITextInsertionService textInsertionService,
        ILogger<OverlayViewModel> logger,
        IOptions<WhisperShowOptions> options)
    {
        _audioService = audioService;
        _providerFactory = providerFactory;
        _textInsertionService = textInsertionService;
        _logger = logger;
        _options = options.Value;

        _audioService.AudioLevelChanged += (_, level) =>
            Application.Current.Dispatcher.Invoke(() => AudioLevel = level);

        UpdateProviderName();
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

    private async Task StartRecordingAsync()
    {
        try
        {
            _previousForegroundWindow = NativeMethods.GetForegroundWindow();
            State = RecordingState.Recording;
            await _audioService.StartRecordingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start recording");
            ErrorMessage = $"Recording failed: {ex.Message}";
            State = RecordingState.Error;
        }
    }

    private async Task StopAndTranscribeAsync()
    {
        try
        {
            State = RecordingState.Transcribing;
            var audioData = await _audioService.StopRecordingAsync();

            if (audioData.Length < 1000)
            {
                ErrorMessage = "Recording too short. Please try again.";
                State = RecordingState.Error;
                return;
            }

            var provider = _providerFactory.GetProvider(_options.Provider);
            var result = await provider.TranscribeAsync(audioData, _options.Language);

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                ErrorMessage = "No speech detected. Please try again.";
                State = RecordingState.Error;
                return;
            }

            TranscribedText = result.Text;
            // Auto-insert into the previously focused window
            await InsertTextAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription failed");
            ErrorMessage = $"Transcription failed: {ex.Message}";
            State = RecordingState.Error;
        }
    }

    [RelayCommand]
    private async Task InsertTextAsync()
    {
        if (string.IsNullOrEmpty(TranscribedText)) return;

        // Restore focus to previously active window
        if (_previousForegroundWindow != IntPtr.Zero)
        {
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

        DismissResult();
    }

    [RelayCommand]
    private void CopyText()
    {
        if (string.IsNullOrEmpty(TranscribedText)) return;
        Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(TranscribedText));
        DismissResult();
    }

    [RelayCommand]
    private void DismissResult()
    {
        TranscribedText = null;
        ErrorMessage = null;
        State = RecordingState.Idle;
    }

    public void UpdateProviderName()
    {
        var provider = _providerFactory.GetProvider(_options.Provider);
        CurrentProviderName = provider.ProviderName;
    }
}
