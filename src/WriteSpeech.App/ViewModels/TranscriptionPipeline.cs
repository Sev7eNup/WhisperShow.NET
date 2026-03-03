using System.Text;
using Microsoft.Extensions.Logging;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.IDE;
using WriteSpeech.Core.Services.Modes;
using WriteSpeech.Core.Services.Snippets;
using WriteSpeech.Core.Services.TextCorrection;
using WriteSpeech.Core.Services.Transcription;

namespace WriteSpeech.App.ViewModels;

/// <summary>
/// Result of a transcription pipeline run.
/// </summary>
public record PipelineResult(string Text, string CorrectionProvider);

/// <summary>
/// Encapsulates the transcription + correction pipeline: provider routing, streaming,
/// text correction, command mode, IDE context preparation, and snippet application.
/// </summary>
public class TranscriptionPipeline : IDisposable
{
    private readonly TranscriptionProviderFactory _providerFactory;
    private readonly TextCorrectionProviderFactory _correctionFactory;
    private readonly ICombinedTranscriptionCorrectionService _combinedService;
    private readonly ISnippetService _snippetService;
    private readonly IModeService _modeService;
    private readonly IIDEDetectionService _ideDetectionService;
    private readonly IIDEContextService _ideContextService;
    private readonly ILogger<TranscriptionPipeline> _logger;

    private CancellationTokenSource? _transcriptionCts;

    public TranscriptionPipeline(
        TranscriptionProviderFactory providerFactory,
        TextCorrectionProviderFactory correctionFactory,
        ICombinedTranscriptionCorrectionService combinedService,
        ISnippetService snippetService,
        IModeService modeService,
        IIDEDetectionService ideDetectionService,
        IIDEContextService ideContextService,
        ILogger<TranscriptionPipeline> logger)
    {
        _providerFactory = providerFactory;
        _correctionFactory = correctionFactory;
        _combinedService = combinedService;
        _snippetService = snippetService;
        _modeService = modeService;
        _ideDetectionService = ideDetectionService;
        _ideContextService = ideContextService;
        _logger = logger;
    }

    /// <summary>Whether the combined audio model is available.</summary>
    public bool IsCombinedModelAvailable => _combinedService.IsAvailable;

    /// <summary>Reports status changes ("Transcribing...", "Correcting text...", etc.).</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Reports progressive streaming text updates (null to clear).</summary>
    public event Action<string?>? StreamingTextChanged;

    /// <summary>
    /// Runs the full transcription pipeline: transcribe + optionally correct + apply snippets.
    /// Returns null if the result is empty/whitespace.
    /// </summary>
    public async Task<PipelineResult?> TranscribeAsync(
        byte[] audioData,
        WriteSpeechOptions options,
        string? activeProcessName,
        string? selectedText,
        bool isCommandMode,
        CancellationToken cancellationToken = default)
    {
        _transcriptionCts?.Cancel();
        _transcriptionCts?.Dispose();
        _transcriptionCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            _transcriptionCts.Token, cancellationToken);
        var ct = linkedCts.Token;

        string text;
        string correctionProvider;

        // Fast path: combined audio model (transcription + correction in one API call)
        if (options.TextCorrection.UseCombinedAudioModel && _combinedService.IsAvailable)
        {
            _logger.LogInformation("Using combined transcription+correction pipeline");
            StatusChanged?.Invoke(isCommandMode ? "Processing command..." : "Transcribing & correcting...");
            try
            {
                string? combinedPrompt;
                if (isCommandMode && !string.IsNullOrWhiteSpace(selectedText))
                {
                    combinedPrompt = TextCorrectionDefaults.VoiceCommandCombinedSystemPrompt
                        + $"\n\nSelected text:\n{selectedText}";
                }
                else
                {
                    combinedPrompt = _modeService.ResolveCombinedSystemPrompt(activeProcessName);
                }

                var combinedTargetLang = _modeService.ResolveTargetLanguage(activeProcessName);
                text = await _combinedService.TranscribeAndCorrectAsync(
                    audioData, options.Language, combinedPrompt, combinedTargetLang, ct);
                correctionProvider = "Combined";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Combined audio model failed, falling back to standard pipeline");
                text = await StandardTranscribeAsync(audioData, options, activeProcessName, ct);
                correctionProvider = options.TextCorrection.Provider.ToString();
            }
        }
        else if (isCommandMode && !string.IsNullOrWhiteSpace(selectedText))
        {
            // Command mode via standard pipeline
            _logger.LogInformation("Using standard transcription pipeline in command mode");
            var provider = _providerFactory.GetProvider(options.Provider);
            StatusChanged?.Invoke(provider.IsModelLoaded ? "Transcribing..." : "Loading transcription model...");
            var result = await provider.TranscribeAsync(audioData, options.Language, ct);
            text = await TransformTextAsync(result.Text, selectedText, options, ct);
            correctionProvider = "VoiceCommand";
        }
        else
        {
            // Standard pipeline
            _logger.LogInformation("Using standard transcription pipeline (Provider: {Provider})", options.Provider);
            text = await StandardTranscribeAsync(audioData, options, activeProcessName, ct);
            correctionProvider = options.TextCorrection.Provider.ToString();
        }

        // Apply snippet expansions only in normal dictation mode
        if (!isCommandMode)
        {
            text = _snippetService.ApplySnippets(text);
        }

        if (string.IsNullOrWhiteSpace(text))
            return null;

        return new PipelineResult(text, correctionProvider);
    }

    /// <summary>
    /// Prepares IDE context (fire-and-forget). Must be called before TranscribeAsync
    /// to inject identifiers into correction prompts.
    /// </summary>
    public void PrepareIDEContext(IntPtr foregroundWindow, WriteSpeechOptions options,
        Func<IntPtr, string?> getProcessName)
    {
        var integration = options.Integration;
        if (!integration.VariableRecognition && !integration.FileTagging)
        {
            _ideContextService.Clear();
            return;
        }

        try
        {
            var ideInfo = _ideDetectionService.DetectIDE(foregroundWindow);
            if (ideInfo?.WorkspacePath is not null)
            {
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

    /// <summary>Clears cached IDE context.</summary>
    public void ClearIDEContext() => _ideContextService.Clear();

    /// <summary>Cancels any in-progress transcription.</summary>
    public void Cancel()
    {
        _transcriptionCts?.Cancel();
    }

    /// <summary>Gets the provider display name for a given provider setting.</summary>
    public string GetProviderName(TranscriptionProvider provider)
    {
        var p = _providerFactory.GetProvider(provider);
        return p.ProviderName;
    }

    private async Task<string> StandardTranscribeAsync(
        byte[] audioData, WriteSpeechOptions options, string? activeProcessName, CancellationToken ct)
    {
        var provider = _providerFactory.GetProvider(options.Provider);
        StatusChanged?.Invoke(provider.IsModelLoaded ? "Transcribing..." : "Loading transcription model...");

        string text;

        // Use streaming when available (local Whisper yields segments progressively)
        if (provider is IStreamingTranscriptionService streamer)
        {
            var sb = new StringBuilder();
            await foreach (var segment in streamer.TranscribeStreamingAsync(audioData, options.Language, ct))
            {
                sb.Append(segment);
                StreamingTextChanged?.Invoke(sb.ToString().Trim());
            }
            text = sb.ToString().Trim();
        }
        else
        {
            var result = await provider.TranscribeAsync(audioData, options.Language, ct);
            text = result.Text;
        }

        // Clear streaming preview before correction phase
        StreamingTextChanged?.Invoke(null);

        var corrector = _correctionFactory.GetProvider(options.TextCorrection.Provider);
        _logger.LogInformation("Text correction: {Provider}", options.TextCorrection.Provider);
        if (corrector is not null && !string.IsNullOrWhiteSpace(text))
        {
            StatusChanged?.Invoke(corrector.IsModelLoaded ? "Correcting text..." : "Loading correction model...");
            var modePrompt = _modeService.ResolveSystemPrompt(activeProcessName);
            var targetLanguage = _modeService.ResolveTargetLanguage(activeProcessName);
            text = await corrector.CorrectAsync(text, options.Language, modePrompt, targetLanguage, ct);
        }

        return text;
    }

    private async Task<string> TransformTextAsync(
        string voiceCommand, string selectedText, WriteSpeechOptions options, CancellationToken ct)
    {
        _logger.LogInformation("Command mode: transforming text ({SelectedLength} chars) with command ({CommandLength} chars)",
            selectedText.Length, voiceCommand.Length);
        StatusChanged?.Invoke("Transforming text...");

        var corrector = _correctionFactory.GetProvider(options.TextCorrection.Provider);
        if (corrector is null)
        {
            _logger.LogWarning("Voice command mode requires text correction to be enabled — returning raw transcription");
            return voiceCommand;
        }

        // Sanitize selected text to prevent prompt injection via document content
        var sanitizedSelected = selectedText
            .Replace("<selected_text>", "&lt;selected_text&gt;")
            .Replace("</selected_text>", "&lt;/selected_text&gt;");
        var userMessage = $"<selected_text>{sanitizedSelected}</selected_text>\n\nVoice command: {voiceCommand}";
        return await corrector.CorrectAsync(userMessage, options.Language,
            TextCorrectionDefaults.VoiceCommandSystemPrompt, targetLanguage: null, ct);
    }

    public void Dispose()
    {
        _transcriptionCts?.Cancel();
        _transcriptionCts?.Dispose();
        _transcriptionCts = null;
    }
}
