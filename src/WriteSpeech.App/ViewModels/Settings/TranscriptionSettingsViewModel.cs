using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.ModelManagement;

namespace WriteSpeech.App.ViewModels.Settings;

public record CloudModelOption(string Id, string DisplayName, string Description);

public partial class TranscriptionSettingsViewModel : ObservableObject
{
    public static IReadOnlyList<CloudModelOption> CloudTranscriptionModels { get; } =
    [
        new("gpt-4o-mini-transcribe", "GPT-4o Mini Transcribe", "Fast and accurate transcription"),
        new("gpt-4o-transcribe", "GPT-4o Transcribe", "Most accurate transcription"),
        new("whisper-1", "Whisper", "Original Whisper model"),
    ];

    public static IReadOnlyList<CloudModelOption> CloudCorrectionModels { get; } =
    [
        new("gpt-5.2", "GPT-5.2", "Latest flagship reasoning model"),
        new("gpt-5-mini", "GPT-5 Mini", "Fast and cost-efficient"),
        new("gpt-5-nano", "GPT-5 Nano", "Ultra-fast, low latency"),
        new("gpt-4.1", "GPT-4.1", "Strong baseline, 1M context"),
        new("gpt-4.1-mini", "GPT-4.1 Mini", "Smaller GPT-4.1 model"),
        new("gpt-4.1-nano", "GPT-4.1 Nano", "Lowest latency GPT-4.1"),
    ];

    public static IReadOnlyList<CloudModelOption> AnthropicCorrectionModels { get; } =
    [
        new("claude-sonnet-4-6", "Claude Sonnet 4.6", "Fast and capable"),
        new("claude-opus-4-6", "Claude Opus 4.6", "Most capable"),
        new("claude-haiku-4-5-20251001", "Claude Haiku 4.5", "Fastest"),
    ];

    public static IReadOnlyList<CloudModelOption> GoogleCorrectionModels { get; } =
    [
        new("gemini-2.5-flash", "Gemini 2.5 Flash", "Fast and efficient"),
        new("gemini-2.5-pro", "Gemini 2.5 Pro", "Most capable"),
    ];

    public static IReadOnlyList<CloudModelOption> GroqCorrectionModels { get; } =
    [
        new("llama-3.3-70b-versatile", "Llama 3.3 70B", "Most capable"),
        new("llama-3.1-8b-instant", "Llama 3.1 8B", "Ultra-fast"),
        new("mixtral-8x7b-32768", "Mixtral 8x7B", "Balanced"),
    ];

    private readonly IModelPreloadService _preloadService;
    private readonly ILogger _logger;
    private readonly Action _scheduleSave;

    // --- Child Sub-VM ---
    public ModelManagementViewModel Models { get; }

    // --- Transcription: Provider ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCloudUsageHint))]
    [NotifyPropertyChangedFor(nameof(IsCustomCloudModel))]
    private TranscriptionProvider _provider = TranscriptionProvider.OpenAI;
    [ObservableProperty] private bool _isEditingProvider;

    // --- Transcription: Endpoint ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomEndpoint))]
    [NotifyPropertyChangedFor(nameof(IsCustomCloudModel))]
    private string _openAiEndpoint = "";
    [ObservableProperty] private bool _isEditingEndpoint;

    // --- Transcription: API Key ---
    [ObservableProperty] private string _openAiApiKey = "";
    [ObservableProperty] private bool _isEditingApiKey;
    [ObservableProperty] private string _openAiApiKeyDisplay = "";

    // --- Transcription: Model ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomCloudModel))]
    private string _transcriptionModel = "whisper-1";
    [ObservableProperty] private bool _isEditingModel;
    [ObservableProperty] private bool _isEditingCustomCloudModel;
    private string _openAiModelName = "whisper-1";
    private string _localModelName = "ggml-small.bin";
    private string _parakeetModelName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";

    // --- Transcription: GPU ---
    [ObservableProperty] private bool _gpuAcceleration = true;

    // --- Parakeet ---
    [ObservableProperty] private int _parakeetNumThreads = 4;

    // --- Text Correction ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCloudUsageHint))]
    private TextCorrectionProvider _correctionProvider = TextCorrectionProvider.Off;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomCorrectionModel))]
    private string _correctionCloudModel = "gpt-4.1-mini";
    [ObservableProperty] private bool _isEditingCustomCorrectionModel;
    [ObservableProperty] private bool _correctionGpuAcceleration = true;

    // --- Correction local model name ---
    [ObservableProperty] private string _correctionLocalModelName = "";

    // --- Anthropic ---
    [ObservableProperty] private string _anthropicApiKey = "";
    [ObservableProperty] private string _anthropicApiKeyDisplay = "";
    [ObservableProperty] private bool _isEditingAnthropicApiKey;
    [ObservableProperty] private string _anthropicModel = "claude-sonnet-4-6";

    // --- Google ---
    [ObservableProperty] private string _googleApiKey = "";
    [ObservableProperty] private string _googleApiKeyDisplay = "";
    [ObservableProperty] private bool _isEditingGoogleApiKey;
    [ObservableProperty] private string _googleModel = "gemini-2.5-flash";

    // --- Groq ---
    [ObservableProperty] private string _groqApiKey = "";
    [ObservableProperty] private string _groqApiKeyDisplay = "";
    [ObservableProperty] private bool _isEditingGroqApiKey;
    [ObservableProperty] private string _groqModel = "llama-3.3-70b-versatile";

    // --- Combined Audio Model ---
    [ObservableProperty] private bool _useCombinedAudioModel;
    [ObservableProperty] private string _combinedAudioModel = "gpt-4o-mini-audio-preview";
    [ObservableProperty] private bool _isEditingCombinedAudioModel;

    // --- Custom endpoint ---
    public bool HasCustomEndpoint => !string.IsNullOrWhiteSpace(OpenAiEndpoint);

    // --- Custom cloud model ---
    public bool IsCustomCloudModel =>
        Provider == TranscriptionProvider.OpenAI &&
        (HasCustomEndpoint || CloudTranscriptionModels.All(m => m.Id != TranscriptionModel));

    // --- Custom correction model ---
    public bool IsCustomCorrectionModel =>
        CloudCorrectionModels.All(m => m.Id != CorrectionCloudModel);

    // --- Cloud usage hint ---
    public bool ShowCloudUsageHint =>
        Provider is TranscriptionProvider.Local or TranscriptionProvider.Parakeet &&
        CorrectionProvider is TextCorrectionProvider.Cloud or TextCorrectionProvider.OpenAI;

    public TranscriptionSettingsViewModel(
        IModelManager modelManager,
        ICorrectionModelManager correctionModelManager,
        IParakeetModelManager parakeetModelManager,
        IModelPreloadService preloadService,
        ILogger logger,
        IDispatcherService dispatcher,
        Action scheduleSave,
        WriteSpeechOptions options)
    {
        _preloadService = preloadService;
        _logger = logger;
        _scheduleSave = scheduleSave;

        _provider = options.Provider;
        _openAiEndpoint = options.OpenAI.Endpoint ?? "";
        _openAiApiKey = options.OpenAI.ApiKey ?? "";
        _openAiModelName = options.OpenAI.Model;
        _localModelName = options.Local.ModelName;
        _parakeetModelName = options.Parakeet.ModelName;
        _parakeetNumThreads = options.Parakeet.NumThreads;
        _transcriptionModel = options.Provider switch
        {
            TranscriptionProvider.OpenAI => options.OpenAI.Model,
            TranscriptionProvider.Parakeet => options.Parakeet.ModelName,
            _ => options.Local.ModelName,
        };
        _gpuAcceleration = options.Local.GpuAcceleration;
        _correctionProvider = options.TextCorrection.Provider;
        _correctionCloudModel = options.TextCorrection.Model;
        _correctionGpuAcceleration = options.TextCorrection.LocalGpuAcceleration;
        _correctionLocalModelName = options.TextCorrection.LocalModelName;
        _anthropicApiKey = options.TextCorrection.Anthropic.ApiKey ?? "";
        _anthropicModel = options.TextCorrection.Anthropic.Model;
        _googleApiKey = options.TextCorrection.Google.ApiKey ?? "";
        _googleModel = options.TextCorrection.Google.Model;
        _groqApiKey = options.TextCorrection.Groq.ApiKey ?? "";
        _groqModel = options.TextCorrection.Groq.Model;
        _useCombinedAudioModel = options.TextCorrection.UseCombinedAudioModel;
        _combinedAudioModel = options.TextCorrection.CombinedAudioModel;

        UpdateApiKeyDisplay();
        UpdateProviderApiKeyDisplay(AnthropicApiKey, v => AnthropicApiKeyDisplay = v);
        UpdateProviderApiKeyDisplay(GoogleApiKey, v => GoogleApiKeyDisplay = v);
        UpdateProviderApiKeyDisplay(GroqApiKey, v => GroqApiKeyDisplay = v);

        Models = new ModelManagementViewModel(
            modelManager, correctionModelManager, parakeetModelManager,
            preloadService, logger, dispatcher, scheduleSave,
            () => TranscriptionModel,
            name => { TranscriptionModel = name; _localModelName = name; },
            () => CorrectionLocalModelName,
            name => CorrectionLocalModelName = name,
            () => _parakeetModelName,
            name => { _parakeetModelName = name; },
            provider => { Provider = provider; },
            () => Provider);
    }

    private void UpdateApiKeyDisplay()
    {
        OpenAiApiKeyDisplay = string.IsNullOrEmpty(OpenAiApiKey)
            ? "Not configured"
            : OpenAiApiKey.Length >= 4 ? $"sk-...{OpenAiApiKey[^4..]}" : "sk-...****";
    }

    private static void UpdateProviderApiKeyDisplay(string key, Action<string> setter)
    {
        setter(string.IsNullOrEmpty(key)
            ? "Not configured"
            : key.Length >= 4 ? $"...{key[^4..]}" : "...****");
    }

    // --- Transcription ---

    [RelayCommand]
    private void StartEditingProvider() => IsEditingProvider = true;

    public void ApplyProvider(TranscriptionProvider provider)
    {
        if (Provider == TranscriptionProvider.OpenAI) _openAiModelName = TranscriptionModel;
        else if (Provider == TranscriptionProvider.Local) _localModelName = TranscriptionModel;

        Provider = provider;
        IsEditingProvider = false;
        TranscriptionModel = provider switch
        {
            TranscriptionProvider.OpenAI => _openAiModelName,
            TranscriptionProvider.Parakeet => _parakeetModelName,
            _ => _localModelName,
        };
        _scheduleSave();

        if (provider == TranscriptionProvider.Local)
        {
            _preloadService.UnloadParakeetModel();
            _preloadService.PreloadTranscriptionModel(_localModelName);
        }
        else if (provider == TranscriptionProvider.Parakeet)
        {
            _preloadService.UnloadTranscriptionModel();
            _preloadService.PreloadParakeetModel();
        }
    }

    [RelayCommand]
    private void SelectProvider(string providerName)
    {
        if (providerName == "Local" && Provider is TranscriptionProvider.Local or TranscriptionProvider.Parakeet)
            return; // Already showing the local section — don't reset provider

        if (providerName == "Local")
        {
            // Default to Local (Whisper) when switching from Cloud
            ApplyProvider(TranscriptionProvider.Local);
            return;
        }

        if (Enum.TryParse<TranscriptionProvider>(providerName, out var provider))
            ApplyProvider(provider);
    }

    [RelayCommand]
    private void StartEditingEndpoint() => IsEditingEndpoint = true;

    public void ApplyEndpoint(string endpoint)
    {
        OpenAiEndpoint = endpoint;
        IsEditingEndpoint = false;
        _scheduleSave();
    }

    [RelayCommand]
    private void StartEditingApiKey() => IsEditingApiKey = true;

    public void ApplyApiKey(string key)
    {
        OpenAiApiKey = key;
        IsEditingApiKey = false;
        UpdateApiKeyDisplay();
        _scheduleSave();
    }

    [RelayCommand]
    private void StartEditingModel() => IsEditingModel = true;

    [RelayCommand]
    private void SelectCloudModel(string modelId) => ApplyModel(modelId);

    public void ApplyModel(string model)
    {
        TranscriptionModel = model;
        if (Provider == TranscriptionProvider.OpenAI) _openAiModelName = model;
        else if (Provider == TranscriptionProvider.Local) _localModelName = model;
        IsEditingModel = false;
        _scheduleSave();
    }

    [RelayCommand]
    private void ToggleGpuAcceleration()
    {
        GpuAcceleration = !GpuAcceleration;
        _scheduleSave();
    }

    // --- Text Correction ---

    [RelayCommand]
    private void SelectCorrectionProvider(string providerName)
    {
        if (Enum.TryParse<TextCorrectionProvider>(providerName, out var provider))
        {
            CorrectionProvider = provider;
            _logger.LogInformation("Text correction provider changed to: {Provider}", provider);
            _scheduleSave();

            if (provider == TextCorrectionProvider.Local)
                _preloadService.PreloadCorrectionModel(CorrectionLocalModelName);
            else
                _preloadService.UnloadCorrectionModel();
        }
    }

    [RelayCommand]
    private void SelectCorrectionCloudModel(string modelId) => ApplyCorrectionModel(modelId);

    public void ApplyCorrectionModel(string model)
    {
        CorrectionCloudModel = model;
        IsEditingCustomCorrectionModel = false;
        _scheduleSave();
    }

    [RelayCommand]
    private void ToggleCorrectionGpuAcceleration()
    {
        CorrectionGpuAcceleration = !CorrectionGpuAcceleration;
        _logger.LogInformation("Correction GPU acceleration: {Enabled}", CorrectionGpuAcceleration);
        _scheduleSave();
    }

    // --- Per-provider API key + model ---

    public void ApplyAnthropicApiKey(string key)
    {
        AnthropicApiKey = key;
        IsEditingAnthropicApiKey = false;
        UpdateProviderApiKeyDisplay(key, v => AnthropicApiKeyDisplay = v);
        _scheduleSave();
    }

    [RelayCommand]
    private void SelectAnthropicModel(string modelId)
    {
        AnthropicModel = modelId;
        _scheduleSave();
    }

    public void ApplyGoogleApiKey(string key)
    {
        GoogleApiKey = key;
        IsEditingGoogleApiKey = false;
        UpdateProviderApiKeyDisplay(key, v => GoogleApiKeyDisplay = v);
        _scheduleSave();
    }

    [RelayCommand]
    private void SelectGoogleModel(string modelId)
    {
        GoogleModel = modelId;
        _scheduleSave();
    }

    public void ApplyGroqApiKey(string key)
    {
        GroqApiKey = key;
        IsEditingGroqApiKey = false;
        UpdateProviderApiKeyDisplay(key, v => GroqApiKeyDisplay = v);
        _scheduleSave();
    }

    [RelayCommand]
    private void SelectGroqModel(string modelId)
    {
        GroqModel = modelId;
        _scheduleSave();
    }

    [RelayCommand]
    private void ToggleCombinedAudioModel() => _scheduleSave();

    [RelayCommand]
    private void StartEditingCombinedAudioModel() => IsEditingCombinedAudioModel = true;

    public void ApplyCombinedAudioModel(string model)
    {
        CombinedAudioModel = model;
        IsEditingCombinedAudioModel = false;
        _scheduleSave();
    }

    public void RefreshModels()
    {
        Models.RefreshModels();
        Models.RefreshCorrectionModels();
        Models.RefreshParakeetModels();
    }

    // --- Persistence ---

    public void WriteSettings(JsonNode section)
    {
        section["Provider"] = Provider.ToString();

        var openAi = SettingsViewModel.EnsureObject(section, "OpenAI");
        openAi["ApiKey"] = OpenAiApiKey;
        openAi["Model"] = _openAiModelName;
        openAi["Endpoint"] = string.IsNullOrWhiteSpace(OpenAiEndpoint) ? null : OpenAiEndpoint;

        var local = SettingsViewModel.EnsureObject(section, "Local");
        local["ModelName"] = _localModelName;
        local["GpuAcceleration"] = GpuAcceleration;

        var parakeet = SettingsViewModel.EnsureObject(section, "Parakeet");
        parakeet["ModelName"] = _parakeetModelName;
        parakeet["GpuAcceleration"] = GpuAcceleration;
        parakeet["NumThreads"] = ParakeetNumThreads;

        var correction = SettingsViewModel.EnsureObject(section, "TextCorrection");
        correction["Provider"] = CorrectionProvider.ToString();
        correction["Model"] = CorrectionCloudModel;
        correction["LocalModelName"] = CorrectionLocalModelName;
        correction["LocalGpuAcceleration"] = CorrectionGpuAcceleration;
        correction["UseCombinedAudioModel"] = UseCombinedAudioModel;
        correction["CombinedAudioModel"] = CombinedAudioModel;

        var anthropic = SettingsViewModel.EnsureObject(correction, "Anthropic");
        anthropic["ApiKey"] = AnthropicApiKey;
        anthropic["Model"] = AnthropicModel;

        var google = SettingsViewModel.EnsureObject(correction, "Google");
        google["ApiKey"] = GoogleApiKey;
        google["Model"] = GoogleModel;

        var groq = SettingsViewModel.EnsureObject(correction, "Groq");
        groq["ApiKey"] = GroqApiKey;
        groq["Model"] = GroqModel;
    }
}
