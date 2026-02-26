using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.IDE;

namespace WriteSpeech.Core.Services.TextCorrection;

public class LocalTextCorrectionService : ITextCorrectionService, IDisposable
{
    private readonly ILogger<LocalTextCorrectionService> _logger;
    private readonly IOptionsMonitor<WriteSpeechOptions> _optionsMonitor;
    private readonly IDictionaryService _dictionaryService;
    private readonly IIDEContextService _ideContextService;
    private readonly Lock _loadLock = new();
    private LLamaWeights? _model;
    private string? _loadedModelPath;
    private bool _disposed;

    public TextCorrectionProvider ProviderType => TextCorrectionProvider.Local;
    public bool IsModelLoaded => _model is not null;

    public LocalTextCorrectionService(
        ILogger<LocalTextCorrectionService> logger,
        IOptionsMonitor<WriteSpeechOptions> optionsMonitor,
        IDictionaryService dictionaryService,
        IIDEContextService ideContextService)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _dictionaryService = dictionaryService;
        _ideContextService = ideContextService;
    }

    public async Task<string> CorrectAsync(string rawText, string? language, string? systemPromptOverride = null, string? targetLanguage = null, CancellationToken ct = default)
    {
        try
        {
            var options = _optionsMonitor.CurrentValue;
            var correctionOpts = options.TextCorrection;

            var modelPath = GetModelPath(correctionOpts);
            if (modelPath is null)
            {
                _logger.LogWarning("No local correction model found, returning raw text");
                return rawText;
            }

            EnsureModelLoaded(modelPath, correctionOpts.LocalGpuAcceleration);

            var systemPrompt = systemPromptOverride ?? correctionOpts.SystemPrompt ?? TextCorrectionDefaults.CorrectionSystemPrompt;
            systemPrompt += _dictionaryService.BuildPromptFragment();
            if (options.Integration.IncludeForLocalModels)
                systemPrompt += _ideContextService.BuildPromptFragment();

            if (correctionOpts.AutoAddToDictionary)
                systemPrompt += TextCorrectionDefaults.VocabExtractionInstruction;

            string userMessage;
            if (!string.IsNullOrEmpty(targetLanguage))
            {
                userMessage = $"[Translate to: {targetLanguage}]\n{rawText}";
            }
            else if (systemPromptOverride is not null)
            {
                userMessage = rawText;
            }
            else
            {
                var languageHint = string.IsNullOrEmpty(language)
                    ? "Keep the SAME language as the input — do NOT translate"
                    : $"Output language MUST be: {language}";
                userMessage = $"[{languageHint}]\n{rawText}";
            }

            _logger.LogInformation("Running local text correction ({Length} chars, model: {Model})",
                rawText.Length, correctionOpts.LocalModelName);

            var modelParams = new ModelParams(_loadedModelPath!)
            {
                ContextSize = 2048,
                GpuLayerCount = correctionOpts.LocalGpuAcceleration ? -1 : 0,
                Threads = Math.Max(1, Environment.ProcessorCount / 2),
            };

            var executor = new StatelessExecutor(_model!, modelParams)
            {
                ApplyTemplate = true,
                SystemMessage = systemPrompt,
            };

            var inferenceParams = new InferenceParams
            {
                MaxTokens = Math.Max(256, rawText.Length * 2),
                AntiPrompts = ["User:", "\nUser", "<|end|>", "<|im_end|>"],
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0f },
            };

            var result = new StringBuilder();
            await foreach (var token in executor.InferAsync(userMessage, inferenceParams, ct))
            {
                result.Append(token);
            }

            var corrected = StripMetaCommentary(result.ToString().Trim());

            _logger.LogInformation("Local text correction completed: {OrigLength} → {CorrLength} chars",
                rawText.Length, corrected.Length);

            if (string.IsNullOrWhiteSpace(corrected))
                return rawText;

            if (correctionOpts.AutoAddToDictionary)
            {
                var (cleanText, vocab) = VocabResponseParser.Parse(corrected);
                VocabResponseParser.AddExtractedVocabulary(vocab, _dictionaryService, _logger);
                return string.IsNullOrWhiteSpace(cleanText) ? rawText : cleanText;
            }

            return corrected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local text correction failed, returning raw text");
            return rawText;
        }
    }

    public void Preload()
    {
        var correctionOpts = _optionsMonitor.CurrentValue.TextCorrection;
        var modelPath = GetModelPath(correctionOpts);
        if (modelPath is not null)
            EnsureModelLoaded(modelPath, correctionOpts.LocalGpuAcceleration);
    }

    public void Preload(string modelName)
    {
        var correctionOpts = _optionsMonitor.CurrentValue.TextCorrection;
        var dir = correctionOpts.GetLocalModelDirectory();
        var path = Path.Combine(dir, Path.GetFileName(modelName));
        if (File.Exists(path))
            EnsureModelLoaded(path, correctionOpts.LocalGpuAcceleration);
    }

    internal static string StripMetaCommentary(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var lines = text.Split('\n');
        var cleaned = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("Here is", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Here's", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Translation:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Corrected text:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("The corrected", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Note:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Output:", StringComparison.OrdinalIgnoreCase))
                break;
            cleaned.Add(line);
        }
        return string.Join("\n", cleaned).Trim();
    }

    private static string? GetModelPath(TextCorrectionOptions correctionOpts)
    {
        var dir = correctionOpts.GetLocalModelDirectory();
        var name = correctionOpts.LocalModelName;
        if (string.IsNullOrEmpty(name)) return null;
        var path = Path.Combine(dir, name);
        return File.Exists(path) ? path : null;
    }

    private void EnsureModelLoaded(string modelPath, bool gpuAcceleration)
    {
        lock (_loadLock)
        {
            if (_model is not null && _loadedModelPath == modelPath)
                return;

            _model?.Dispose();

            _logger.LogInformation("Loading correction model from {Path} (GPU: {Gpu})",
                modelPath, gpuAcceleration);

            var loadParams = new ModelParams(modelPath)
            {
                GpuLayerCount = gpuAcceleration ? -1 : 0,
                Threads = Math.Max(1, Environment.ProcessorCount / 2),
            };

            _model = LLamaWeights.LoadFromFile(loadParams);
            _loadedModelPath = modelPath;
        }
    }

    public void UnloadModel()
    {
        lock (_loadLock)
        {
            _model?.Dispose();
            _model = null;
            _loadedModelPath = null;
            _logger.LogInformation("Correction model unloaded");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _model?.Dispose();
    }
}
