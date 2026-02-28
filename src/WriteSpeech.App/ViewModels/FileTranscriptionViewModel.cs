using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Audio;
using WriteSpeech.Core.Services.History;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Core.Services.Transcription;

namespace WriteSpeech.App.ViewModels;

public partial class FileTranscriptionViewModel : ObservableObject
{
    private readonly TranscriptionProviderFactory _providerFactory;
    private readonly TextCorrectionProviderFactory _correctionFactory;
    private readonly IAudioFileReader _audioFileReader;
    private readonly ITranscriptionHistoryService _historyService;
    private readonly IDispatcherService _dispatcher;
    private readonly ILogger<FileTranscriptionViewModel> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private CancellationTokenSource? _cts;

    private WriteSpeechOptions Options => _optionsMonitor.CurrentValue;

    [ObservableProperty]
    private string? _fileName;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private string? _fileInfo;

    [ObservableProperty]
    private bool _isTranscribing;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string? _resultText;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isCopied;

    public FileTranscriptionViewModel(
        TranscriptionProviderFactory providerFactory,
        TextCorrectionProviderFactory correctionFactory,
        IAudioFileReader audioFileReader,
        ITranscriptionHistoryService historyService,
        IDispatcherService dispatcher,
        ILogger<FileTranscriptionViewModel> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor)
    {
        _providerFactory = providerFactory;
        _correctionFactory = correctionFactory;
        _audioFileReader = audioFileReader;
        _historyService = historyService;
        _dispatcher = dispatcher;
        _logger = logger;
        _optionsMonitor = optionsMonitor;
    }

    public void SetFile(string path)
    {
        FilePath = path;
        FileName = Path.GetFileName(path);
        var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        try
        {
            var fi = new FileInfo(path);
            FileInfo = $"{ext}, {FormatFileSize(fi.Length)}";
        }
        catch (IOException)
        {
            FileInfo = ext;
        }
        ResultText = null;
        ErrorMessage = null;
        IsCopied = false;
    }

    [RelayCommand]
    private async Task TranscribeAsync()
    {
        if (string.IsNullOrEmpty(FilePath)) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsTranscribing = true;
        ResultText = null;
        ErrorMessage = null;
        IsCopied = false;

        try
        {
            var provider = _providerFactory.GetProvider(Options.Provider);
            StatusText = "Reading file...";

            // Always convert to WAV (16kHz/16bit/Mono) — ensures correct format for both
            // local Whisper (requires WAV) and cloud OpenAI (filename = "recording.wav")
            var audioData = await _audioFileReader.ReadAsWavAsync(FilePath, ct);

            ct.ThrowIfCancellationRequested();

            // Transcribe
            StatusText = provider.IsModelLoaded ? "Transcribing..." : "Loading model...";
            var result = await provider.TranscribeAsync(audioData, Options.Language, ct);
            var text = result.Text;

            // Optional: apply text correction
            var corrector = _correctionFactory.GetProvider(Options.TextCorrection.Provider);
            if (corrector is not null && !string.IsNullOrWhiteSpace(text))
            {
                StatusText = "Correcting text...";
                text = await corrector.CorrectAsync(text, Options.Language, ct: ct);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                ErrorMessage = "No speech detected in the file.";
                return;
            }

            ResultText = text;

            // Auto-copy to clipboard
            _dispatcher.Invoke(() => System.Windows.Clipboard.SetText(text));
            IsCopied = true;

            // Save to history
            _historyService.AddEntry(text, $"File ({provider.ProviderName})", result.Duration?.TotalSeconds ?? 0);

            _logger.LogInformation("File transcription complete: {FileName} → {Length} chars", FileName, text.Length);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("File transcription cancelled");
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File transcription failed: {FileName}", FileName);
            ErrorMessage = $"Transcription failed: {OverlayViewModel.SanitizeErrorMessage(ex)}";
        }
        finally
        {
            IsTranscribing = false;
            if (string.IsNullOrEmpty(ErrorMessage) && ResultText is not null)
                StatusText = "Done — copied to clipboard.";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void CopyResult()
    {
        if (string.IsNullOrEmpty(ResultText)) return;
        _dispatcher.Invoke(() => System.Windows.Clipboard.SetText(ResultText));
        IsCopied = true;
    }

    internal static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Format(CultureInfo.InvariantCulture, "{0:F1} KB", bytes / 1024.0),
        _ => string.Format(CultureInfo.InvariantCulture, "{0:F1} MB", bytes / (1024.0 * 1024.0))
    };
}
