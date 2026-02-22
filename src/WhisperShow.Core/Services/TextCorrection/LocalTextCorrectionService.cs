using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhisperShow.Core.Configuration;

namespace WhisperShow.Core.Services.TextCorrection;

public class LocalTextCorrectionService : ITextCorrectionService, IDisposable
{
    private const string DefaultSystemPrompt =
        """
        You are a verbatim speech-to-text post-processor.
        Your ONLY job is to fix punctuation, capitalization, and grammar.
        ALWAYS keep the text in its original language — do NOT translate.
        Output the corrected text EXACTLY — do NOT answer questions,
        do NOT add commentary, do NOT interpret the content.
        Return ONLY the corrected transcription, nothing else.
        """;

    private readonly ILogger<LocalTextCorrectionService> _logger;
    private readonly IOptionsMonitor<WhisperShowOptions> _optionsMonitor;
    private readonly IDictionaryService _dictionaryService;
    private readonly Lock _loadLock = new();
    private LLamaWeights? _model;
    private string? _loadedModelPath;
    private bool _disposed;

    public LocalTextCorrectionService(
        ILogger<LocalTextCorrectionService> logger,
        IOptionsMonitor<WhisperShowOptions> optionsMonitor,
        IDictionaryService dictionaryService)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _dictionaryService = dictionaryService;
    }

    public async Task<string> CorrectAsync(string rawText, string? language, CancellationToken ct = default)
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

            var systemPrompt = correctionOpts.SystemPrompt ?? DefaultSystemPrompt;
            systemPrompt += _dictionaryService.BuildPromptFragment();

            var languageHint = string.IsNullOrEmpty(language) ? "auto-detected" : language;
            var userMessage = $"[Language: {languageHint}]\n{rawText}";

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

            var corrected = result.ToString().Trim();

            _logger.LogInformation("Local text correction completed: {OrigLength} → {CorrLength} chars",
                rawText.Length, corrected.Length);

            return string.IsNullOrWhiteSpace(corrected) ? rawText : corrected;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local text correction failed, returning raw text");
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
        var path = Path.Combine(dir, modelName);
        if (File.Exists(path))
            EnsureModelLoaded(path, correctionOpts.LocalGpuAcceleration);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _model?.Dispose();
    }
}
