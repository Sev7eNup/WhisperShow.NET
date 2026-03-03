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

public record RecentFileItem(string FilePath, string FileName, string TimeAgo, string FileInfo);

public partial class FileTranscriptionViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    internal static readonly HashSet<string> AudioExtensions =
        [".mp3", ".wav", ".m4a", ".flac", ".ogg", ".mp4"];

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
    private bool _isSelectingFile = true;

    [ObservableProperty]
    private bool _isDragOver;

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

    [ObservableProperty]
    private List<RecentFileItem> _recentFiles = [];

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

    internal static bool IsAudioFile(string path)
        => AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public void LoadRecentFiles()
    {
        var entries = _historyService.GetEntries();
        var recentFiles = entries
            .Where(e => e.Provider.StartsWith("File", StringComparison.Ordinal)
                     && !string.IsNullOrEmpty(e.SourceFilePath)
                     && File.Exists(e.SourceFilePath))
            .GroupBy(e => e.SourceFilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(5)
            .Select(e =>
            {
                var ext = Path.GetExtension(e.SourceFilePath!).TrimStart('.').ToUpperInvariant();
                string fileInfo;
                try
                {
                    var fi = new FileInfo(e.SourceFilePath!);
                    fileInfo = $"{ext}, {FormatFileSize(fi.Length)}";
                }
                catch (IOException)
                {
                    fileInfo = ext;
                }
                return new RecentFileItem(e.SourceFilePath!, Path.GetFileName(e.SourceFilePath!), e.TimeAgo, fileInfo);
            })
            .ToList();

        RecentFiles = recentFiles;
    }

    [RelayCommand]
    private void SelectFile(string path)
    {
        if (!IsAudioFile(path))
        {
            ErrorMessage = "Unsupported file format. Supported: MP3, WAV, M4A, FLAC, OGG, MP4.";
            return;
        }

        if (!File.Exists(path))
        {
            ErrorMessage = "File not found.";
            return;
        }

        ErrorMessage = null;
        SetFile(path);
        _ = TranscribeCommand.ExecuteAsync(null);
    }

    public void ResetToSelection()
    {
        _cts?.Cancel();
        IsSelectingFile = true;
        FilePath = null;
        FileName = null;
        FileInfo = null;
        ResultText = null;
        ErrorMessage = null;
        StatusText = "";
        IsCopied = false;
        IsTranscribing = false;
        IsDragOver = false;
        LoadRecentFiles();
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
        IsSelectingFile = false;
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

            // Save to history (include source file path for recent files)
            _historyService.AddEntry(text, $"File ({provider.ProviderName})", result.Duration?.TotalSeconds ?? 0, FilePath);

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    internal static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Format(CultureInfo.InvariantCulture, "{0:F1} KB", bytes / 1024.0),
        _ => string.Format(CultureInfo.InvariantCulture, "{0:F1} MB", bytes / (1024.0 * 1024.0))
    };
}
