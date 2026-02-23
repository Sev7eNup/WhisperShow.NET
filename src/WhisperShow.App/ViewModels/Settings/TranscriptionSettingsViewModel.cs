using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WhisperShow.Core.Models;
using WhisperShow.Core.Services.ModelManagement;

namespace WhisperShow.App.ViewModels.Settings;

public partial class TranscriptionSettingsViewModel : ObservableObject
{
    private readonly IModelPreloadService _preloadService;
    private readonly ILogger _logger;
    private readonly Action _scheduleSave;

    // --- Child Sub-VM ---
    public ModelManagementViewModel Models { get; }

    // --- Transcription: Provider ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCloudUsageHint))]
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
    [ObservableProperty] private string _transcriptionModel = "whisper-1";
    [ObservableProperty] private bool _isEditingModel;
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

    // --- Cloud usage hint ---
    public bool ShowCloudUsageHint =>
        Provider == TranscriptionProvider.Local && CorrectionProvider == TextCorrectionProvider.Cloud;

    public TranscriptionSettingsViewModel(
        IModelManager modelManager,
        ICorrectionModelManager correctionModelManager,
        IModelPreloadService preloadService,
        ILogger logger,
        Action scheduleSave,
        TranscriptionProvider provider,
        string openAiEndpoint,
        string openAiApiKey,
        string openAiModelName,
        string localModelName,
        bool gpuAcceleration,
        TextCorrectionProvider correctionProvider,
        string correctionCloudModel,
        bool correctionGpuAcceleration,
        string correctionLocalModelName,
        bool useCombinedAudioModel,
        string combinedAudioModel)
    {
        _preloadService = preloadService;
        _logger = logger;
        _scheduleSave = scheduleSave;

        _provider = provider;
        _openAiEndpoint = openAiEndpoint;
        _openAiApiKey = openAiApiKey;
        _openAiModelName = openAiModelName;
        _localModelName = localModelName;
        _transcriptionModel = provider == TranscriptionProvider.OpenAI ? openAiModelName : localModelName;
        _gpuAcceleration = gpuAcceleration;
        _correctionProvider = correctionProvider;
        _correctionCloudModel = correctionCloudModel;
        _correctionGpuAcceleration = correctionGpuAcceleration;
        _correctionLocalModelName = correctionLocalModelName;
        _useCombinedAudioModel = useCombinedAudioModel;
        _combinedAudioModel = combinedAudioModel;

        UpdateApiKeyDisplay();

        Models = new ModelManagementViewModel(
            modelManager, correctionModelManager, preloadService, logger, scheduleSave,
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
        section["OpenAI"]!["ApiKey"] = OpenAiApiKey;
        section["OpenAI"]!["Model"] = _openAiModelName;
        section["OpenAI"]!["Endpoint"] = string.IsNullOrWhiteSpace(OpenAiEndpoint) ? null : OpenAiEndpoint;
        section["Local"]!["ModelName"] = _localModelName;
        section["Local"]!["GpuAcceleration"] = GpuAcceleration;
        section["TextCorrection"]!["Provider"] = CorrectionProvider.ToString();
        section["TextCorrection"]!["Model"] = CorrectionCloudModel;
        section["TextCorrection"]!["LocalModelName"] = CorrectionLocalModelName;
        section["TextCorrection"]!["LocalGpuAcceleration"] = CorrectionGpuAcceleration;
        section["TextCorrection"]!["UseCombinedAudioModel"] = UseCombinedAudioModel;
        section["TextCorrection"]!["CombinedAudioModel"] = CombinedAudioModel;
    }
}
