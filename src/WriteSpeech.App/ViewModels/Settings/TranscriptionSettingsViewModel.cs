using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.ModelManagement;

namespace WriteSpeech.App.ViewModels.Settings;

public record CloudTranscriptionModel(string Id, string DisplayName, string Description);

public partial class TranscriptionSettingsViewModel : ObservableObject
{
    public static IReadOnlyList<CloudTranscriptionModel> CloudTranscriptionModels { get; } =
    [
        new("gpt-4o-mini-transcribe", "GPT-4o Mini Transcribe", "Fast and accurate transcription"),
        new("gpt-4o-transcribe", "GPT-4o Transcribe", "Most accurate transcription"),
        new("whisper-1", "Whisper", "Original Whisper model"),
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
    [ObservableProperty] private string _openAiEndpoint = "";
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

    // --- Transcription: GPU ---
    [ObservableProperty] private bool _gpuAcceleration = true;

    // --- Text Correction ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCloudUsageHint))]
    private TextCorrectionProvider _correctionProvider = TextCorrectionProvider.Off;
    [ObservableProperty] private string _correctionCloudModel = "gpt-4o-mini";
    [ObservableProperty] private bool _isEditingCorrectionModel;
    [ObservableProperty] private bool _correctionGpuAcceleration = true;

    // --- Correction local model name ---
    [ObservableProperty] private string _correctionLocalModelName = "";

    // --- Combined Audio Model ---
    [ObservableProperty] private bool _useCombinedAudioModel;
    [ObservableProperty] private string _combinedAudioModel = "gpt-4o-mini-audio-preview";
    [ObservableProperty] private bool _isEditingCombinedAudioModel;

    // --- Custom cloud model ---
    public bool IsCustomCloudModel =>
        Provider == TranscriptionProvider.OpenAI &&
        CloudTranscriptionModels.All(m => m.Id != TranscriptionModel);

    // --- Cloud usage hint ---
    public bool ShowCloudUsageHint =>
        Provider == TranscriptionProvider.Local && CorrectionProvider == TextCorrectionProvider.Cloud;

    public TranscriptionSettingsViewModel(
        IModelManager modelManager,
        ICorrectionModelManager correctionModelManager,
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
        _transcriptionModel = options.Provider == TranscriptionProvider.OpenAI ? options.OpenAI.Model : options.Local.ModelName;
        _gpuAcceleration = options.Local.GpuAcceleration;
        _correctionProvider = options.TextCorrection.Provider;
        _correctionCloudModel = options.TextCorrection.Model;
        _correctionGpuAcceleration = options.TextCorrection.LocalGpuAcceleration;
        _correctionLocalModelName = options.TextCorrection.LocalModelName;
        _useCombinedAudioModel = options.TextCorrection.UseCombinedAudioModel;
        _combinedAudioModel = options.TextCorrection.CombinedAudioModel;

        UpdateApiKeyDisplay();

        Models = new ModelManagementViewModel(
            modelManager, correctionModelManager, preloadService, logger, dispatcher, scheduleSave,
            () => TranscriptionModel,
            name => { TranscriptionModel = name; _localModelName = name; },
            () => CorrectionLocalModelName,
            name => CorrectionLocalModelName = name);
    }

    private void UpdateApiKeyDisplay()
    {
        OpenAiApiKeyDisplay = string.IsNullOrEmpty(OpenAiApiKey)
            ? "Not configured"
            : $"sk-...{OpenAiApiKey[^4..]}";
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
        TranscriptionModel = provider == TranscriptionProvider.OpenAI ? _openAiModelName : _localModelName;
        _scheduleSave();

        if (provider == TranscriptionProvider.Local)
            _preloadService.PreloadTranscriptionModel(_localModelName);
    }

    [RelayCommand]
    private void SelectProvider(string providerName)
    {
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
    private void StartEditingCorrectionModel() => IsEditingCorrectionModel = true;

    public void ApplyCorrectionModel(string model)
    {
        CorrectionCloudModel = model;
        IsEditingCorrectionModel = false;
        _scheduleSave();
    }

    [RelayCommand]
    private void ToggleCorrectionGpuAcceleration()
    {
        CorrectionGpuAcceleration = !CorrectionGpuAcceleration;
        _logger.LogInformation("Correction GPU acceleration: {Enabled}", CorrectionGpuAcceleration);
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

        var correction = SettingsViewModel.EnsureObject(section, "TextCorrection");
        correction["Provider"] = CorrectionProvider.ToString();
        correction["Model"] = CorrectionCloudModel;
        correction["LocalModelName"] = CorrectionLocalModelName;
        correction["LocalGpuAcceleration"] = CorrectionGpuAcceleration;
        correction["UseCombinedAudioModel"] = UseCombinedAudioModel;
        correction["CombinedAudioModel"] = CombinedAudioModel;
    }
}
