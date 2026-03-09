using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Whisper.net.Ggml;
using WriteSpeech.App.ViewModels.Settings;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Configuration;
using WriteSpeech.Core.Services.ModelManagement;

namespace WriteSpeech.App.ViewModels;

/// <summary>
/// Represents the four sequential steps of the first-run setup wizard.
/// </summary>
public enum SetupStep
{
    /// <summary>Welcome screen where the user selects their preferred language.</summary>
    Welcome,

    /// <summary>Transcription provider selection (Cloud/Local/Parakeet) with API key or model download.</summary>
    Transcription,

    /// <summary>AI text correction provider selection (Off/OpenAI/Anthropic/Google/Groq/Custom/Local).</summary>
    Correction,

    /// <summary>Microphone device selection with live audio level testing.</summary>
    Microphone
}

/// <summary>
/// ViewModel for the first-run setup wizard that guides new users through initial configuration.
/// Walks through four steps: language selection, transcription provider setup (including model
/// downloads for local providers), AI text correction provider setup, and microphone selection
/// with live audio level testing. Validates API keys before allowing navigation forward and
/// persists all settings to appsettings.json on completion.
/// </summary>
public partial class SetupWizardViewModel : ObservableObject
{
    private readonly ISettingsPersistenceService _persistenceService;
    private readonly ILogger<SetupWizardViewModel> _logger;
    private readonly IDispatcherService _dispatcher;
    private readonly IModelManager _modelManager;
    private readonly IParakeetModelManager _parakeetModelManager;
    private readonly ICorrectionModelManager _correctionModelManager;
    private readonly IModelPreloadService _preloadService;

    // --- Step navigation ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStepIndex))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    private SetupStep _currentStep = SetupStep.Welcome;

    /// <summary>Zero-based index of the current wizard step (for progress indicator binding).</summary>
    public int CurrentStepIndex => (int)CurrentStep;

    /// <summary>Whether the Back button should be enabled (disabled on the first step).</summary>
    public bool CanGoBack => CurrentStep != SetupStep.Welcome;

    /// <summary>Whether the current step is the final one (Microphone), showing "Finish" instead of "Next".</summary>
    public bool IsLastStep => CurrentStep == SetupStep.Microphone;

    [ObservableProperty] private bool _canGoNext = true;

    // --- Step 1: Language ---
    [ObservableProperty] private string? _selectedLanguageCode;
    [ObservableProperty] private bool _isAutoDetectLanguage = true;

    /// <summary>All supported languages for speech recognition, displayed in the language picker.</summary>
    public ObservableCollection<LanguageInfo> AvailableLanguages { get; } =
        new(SupportedLanguages.All.Select(l => new LanguageInfo(l.Code, l.Name, l.Flag)));

    // --- Step 2: Transcription Provider ---
    [ObservableProperty] private TranscriptionProvider _provider = TranscriptionProvider.OpenAI;
    [ObservableProperty] private string _cloudTranscriptionProvider = "OpenAI";
    [ObservableProperty] private string _openAiApiKey = "";
    [ObservableProperty] private string _openAiTranscriptionModel = "whisper-1";
    [ObservableProperty] private string _groqTranscriptionApiKey = "";
    [ObservableProperty] private string _groqTranscriptionModel = "whisper-large-v3-turbo";
    [ObservableProperty] private bool _localGpuAcceleration = true;
    [ObservableProperty] private bool _parakeetGpuAcceleration = true;
    [ObservableProperty] private string _localModelName = "ggml-small.bin";
    [ObservableProperty] private string _parakeetModelName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";

    /// <summary>Available Whisper GGML models for local transcription (download/select in wizard).</summary>
    public ObservableCollection<ModelItemViewModel> WhisperModels { get; } = [];

    /// <summary>Available Parakeet ONNX models for local transcription (English-only, download/select in wizard).</summary>
    public ObservableCollection<ParakeetModelItemViewModel> ParakeetModels { get; } = [];

    // --- Step 3: Text Correction ---
    [ObservableProperty] private TextCorrectionProvider _correctionProvider = TextCorrectionProvider.Off;
    [ObservableProperty] private string _correctionModel = "gpt-4.1-mini";
    [ObservableProperty] private string _anthropicApiKey = "";
    [ObservableProperty] private string _anthropicModel = "claude-sonnet-4-6";
    [ObservableProperty] private string _googleApiKey = "";
    [ObservableProperty] private string _googleModel = "gemini-3-flash-preview";
    [ObservableProperty] private string _groqCorrectionApiKey = "";
    [ObservableProperty] private string _groqCorrectionModel = "qwen/qwen3-32b";
    [ObservableProperty] private string _customCorrectionEndpoint = "";
    [ObservableProperty] private string _customCorrectionApiKey = "";
    [ObservableProperty] private string _customCorrectionModel = "";
    [ObservableProperty] private bool _correctionGpuAcceleration = true;
    [ObservableProperty] private string _correctionLocalModelName = "";

    /// <summary>Available GGUF models for local AI text correction (download/select in wizard).</summary>
    public ObservableCollection<CorrectionModelItemViewModel> CorrectionModels { get; } = [];

    /// <summary>Whether the OpenAI API key entered for transcription can be reused for correction.</summary>
    public bool IsReusableOpenAiKey =>
        CorrectionProvider is TextCorrectionProvider.OpenAI or TextCorrectionProvider.Cloud
        && Provider == TranscriptionProvider.OpenAI
        && CloudTranscriptionProvider == "OpenAI"
        && !string.IsNullOrWhiteSpace(OpenAiApiKey);

    // --- Step 4: Microphone ---
    [ObservableProperty] private int _selectedMicrophoneIndex;

    /// <summary>Detected audio input devices available for selection.</summary>
    public ObservableCollection<MicrophoneInfo> AvailableMicrophones { get; } = [];

    // --- Mic test ---
    [ObservableProperty] private bool _isMicTesting;
    [ObservableProperty] private float _micTestLevel;
    private MicTestHelper? _micTestHelper;

    /// <summary>Whether the wizard has been completed and settings have been persisted.</summary>
    public bool IsCompleted { get; private set; }

    public SetupWizardViewModel(
        ISettingsPersistenceService persistenceService,
        IDispatcherService dispatcher,
        ILogger<SetupWizardViewModel> logger,
        IModelManager modelManager,
        IParakeetModelManager parakeetModelManager,
        ICorrectionModelManager correctionModelManager,
        IModelPreloadService preloadService,
        WriteSpeechOptions? existingOptions = null)
    {
        _persistenceService = persistenceService;
        _dispatcher = dispatcher;
        _logger = logger;
        _modelManager = modelManager;
        _parakeetModelManager = parakeetModelManager;
        _correctionModelManager = correctionModelManager;
        _preloadService = preloadService;

        if (existingOptions is not null)
        {
            // Transcription
            _provider = existingOptions.Provider;
            _cloudTranscriptionProvider = existingOptions.CloudTranscriptionProvider ?? "OpenAI";
            _openAiApiKey = existingOptions.OpenAI.ApiKey ?? "";
            _openAiTranscriptionModel = existingOptions.OpenAI.Model ?? "whisper-1";
            _groqTranscriptionApiKey = existingOptions.GroqTranscription.ApiKey ?? "";
            _groqTranscriptionModel = existingOptions.GroqTranscription.Model ?? "whisper-large-v3-turbo";
            _localGpuAcceleration = existingOptions.Local.GpuAcceleration;
            _parakeetGpuAcceleration = existingOptions.Parakeet.GpuAcceleration;
            _localModelName = existingOptions.Local.ModelName ?? "ggml-small.bin";
            _parakeetModelName = existingOptions.Parakeet.ModelName ?? "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";

            // Correction
            _correctionProvider = existingOptions.TextCorrection.Provider;
            _correctionModel = existingOptions.TextCorrection.Model ?? "gpt-4.1-mini";
            _anthropicApiKey = existingOptions.TextCorrection.Anthropic.ApiKey ?? "";
            _anthropicModel = existingOptions.TextCorrection.Anthropic.Model ?? "claude-sonnet-4-6";
            _googleApiKey = existingOptions.TextCorrection.Google.ApiKey ?? "";
            _googleModel = existingOptions.TextCorrection.Google.Model ?? "gemini-3-flash-preview";
            _groqCorrectionApiKey = existingOptions.TextCorrection.Groq.ApiKey ?? "";
            _groqCorrectionModel = existingOptions.TextCorrection.Groq.Model ?? "qwen/qwen3-32b";
            _customCorrectionEndpoint = existingOptions.TextCorrection.Custom.Endpoint ?? "";
            _customCorrectionApiKey = existingOptions.TextCorrection.Custom.ApiKey ?? "";
            _customCorrectionModel = existingOptions.TextCorrection.Custom.Model;
            _correctionGpuAcceleration = existingOptions.TextCorrection.LocalGpuAcceleration;
            _correctionLocalModelName = existingOptions.TextCorrection.LocalModelName;

            // Language & Mic
            _selectedLanguageCode = existingOptions.Language;
            _isAutoDetectLanguage = existingOptions.Language == null;
            _selectedMicrophoneIndex = existingOptions.Audio.DeviceIndex;
        }

        LoadMicrophones();
        LoadWhisperModels();
        LoadParakeetModels();
        LoadCorrectionModels();
    }

    // --- Navigation ---

    [RelayCommand]
    internal void NavigateNext()
    {
        if (CurrentStep == SetupStep.Microphone)
        {
            FinishSetup();
            return;
        }

        CurrentStep = CurrentStep switch
        {
            SetupStep.Welcome => SetupStep.Transcription,
            SetupStep.Transcription => SetupStep.Correction,
            SetupStep.Correction => SetupStep.Microphone,
            _ => CurrentStep
        };

        if (CurrentStep == SetupStep.Microphone)
            StartMicTest();

        UpdateCanGoNext();
    }

    [RelayCommand]
    internal void NavigateBack()
    {
        if (CurrentStep == SetupStep.Microphone)
            StopMicTest();

        CurrentStep = CurrentStep switch
        {
            SetupStep.Transcription => SetupStep.Welcome,
            SetupStep.Correction => SetupStep.Transcription,
            SetupStep.Microphone => SetupStep.Correction,
            _ => CurrentStep
        };
        UpdateCanGoNext();
    }

    // --- Provider selection ---

    [RelayCommand]
    internal void SelectProvider(string providerName)
    {
        if (Enum.TryParse<TranscriptionProvider>(providerName, out var provider))
        {
            Provider = provider;
            UpdateCanGoNext();
            OnPropertyChanged(nameof(IsReusableOpenAiKey));
        }
    }

    [RelayCommand]
    internal void SelectCorrectionProvider(string providerName)
    {
        if (Enum.TryParse<TextCorrectionProvider>(providerName, out var provider))
        {
            CorrectionProvider = provider;
            UpdateCanGoNext();
            OnPropertyChanged(nameof(IsReusableOpenAiKey));
        }
    }

    [RelayCommand]
    internal void SelectCloudTranscriptionProvider(string provider)
    {
        CloudTranscriptionProvider = provider;
        UpdateCanGoNext();
        OnPropertyChanged(nameof(IsReusableOpenAiKey));
    }

    [RelayCommand]
    internal void SelectTranscriptionModel(string modelId)
    {
        if (CloudTranscriptionProvider == "Groq")
            GroqTranscriptionModel = modelId;
        else
            OpenAiTranscriptionModel = modelId;
    }

    [RelayCommand]
    internal void SelectCorrectionModel(string modelId)
    {
        switch (CorrectionProvider)
        {
            case TextCorrectionProvider.OpenAI or TextCorrectionProvider.Cloud:
                CorrectionModel = modelId; break;
            case TextCorrectionProvider.Anthropic:
                AnthropicModel = modelId; break;
            case TextCorrectionProvider.Google:
                GoogleModel = modelId; break;
            case TextCorrectionProvider.Groq:
                GroqCorrectionModel = modelId; break;
        }
    }

    // --- Language ---

    [RelayCommand]
    internal void SelectLanguage(string code)
    {
        SelectedLanguageCode = code;
        IsAutoDetectLanguage = false;
    }

    [RelayCommand]
    internal void ToggleAutoDetect()
    {
        IsAutoDetectLanguage = !IsAutoDetectLanguage;
        if (IsAutoDetectLanguage)
            SelectedLanguageCode = null;
    }

    // --- Microphone ---

    internal void SelectMicrophone(int deviceIndex)
    {
        SelectedMicrophoneIndex = deviceIndex;
        StopMicTest();
        StartMicTest();
    }

    // --- API key ---

    internal void SetOpenAiApiKey(string key)
    {
        OpenAiApiKey = key.Trim();
        UpdateCanGoNext();
        OnPropertyChanged(nameof(IsReusableOpenAiKey));
    }

    internal void SetGroqTranscriptionApiKey(string key)
    {
        GroqTranscriptionApiKey = key.Trim();
        UpdateCanGoNext();
    }

    internal void SetAnthropicApiKey(string key)
    {
        AnthropicApiKey = key.Trim();
        UpdateCanGoNext();
    }

    internal void SetGoogleApiKey(string key)
    {
        GoogleApiKey = key.Trim();
        UpdateCanGoNext();
    }

    internal void SetGroqCorrectionApiKey(string key)
    {
        GroqCorrectionApiKey = key.Trim();
        UpdateCanGoNext();
    }

    internal void SetCustomCorrectionEndpoint(string endpoint)
    {
        CustomCorrectionEndpoint = endpoint.Trim();
        UpdateCanGoNext();
    }

    internal void SetCustomCorrectionApiKey(string key)
    {
        CustomCorrectionApiKey = key.Trim();
        UpdateCanGoNext();
    }

    internal void SetCustomCorrectionModel(string model)
    {
        CustomCorrectionModel = model.Trim();
    }

    // --- Mic test ---

    internal void StartMicTest()
    {
        _micTestHelper ??= new MicTestHelper(_dispatcher, _logger, level =>
        {
            MicTestLevel = level;
            IsMicTesting = _micTestHelper?.IsTesting ?? false;
        });
        _micTestHelper.Start(SelectedMicrophoneIndex);
        IsMicTesting = _micTestHelper.IsTesting;
    }

    internal void StopMicTest()
    {
        _micTestHelper?.Stop();
        IsMicTesting = false;
        MicTestLevel = 0;
    }

    // --- Validation ---

    internal void UpdateCanGoNext()
    {
        CanGoNext = CurrentStep switch
        {
            SetupStep.Transcription => Provider switch
            {
                TranscriptionProvider.OpenAI => CloudTranscriptionProvider switch
                {
                    "Groq" => !string.IsNullOrWhiteSpace(GroqTranscriptionApiKey),
                    _ => !string.IsNullOrWhiteSpace(OpenAiApiKey)
                },
                TranscriptionProvider.Local => WhisperModels.Any(m => m.IsActive),
                TranscriptionProvider.Parakeet => ParakeetModels.Any(m => m.IsActive),
                _ => true
            },
            SetupStep.Correction => CorrectionProvider switch
            {
                TextCorrectionProvider.OpenAI or TextCorrectionProvider.Cloud
                    => !string.IsNullOrWhiteSpace(OpenAiApiKey),
                TextCorrectionProvider.Anthropic => !string.IsNullOrWhiteSpace(AnthropicApiKey),
                TextCorrectionProvider.Google => !string.IsNullOrWhiteSpace(GoogleApiKey),
                TextCorrectionProvider.Groq => !string.IsNullOrWhiteSpace(GroqCorrectionApiKey),
                TextCorrectionProvider.Custom => !string.IsNullOrWhiteSpace(CustomCorrectionEndpoint)
                                                 && !string.IsNullOrWhiteSpace(CustomCorrectionApiKey),
                TextCorrectionProvider.Local => CorrectionModels.Any(m => m.IsActive),
                _ => true // Off
            },
            _ => true
        };
    }

    // --- Finish & Persist ---

    internal void FinishSetup()
    {
        StopMicTest();

        _persistenceService.ScheduleUpdate(section =>
        {
            // Language
            section["Language"] = IsAutoDetectLanguage ? null : SelectedLanguageCode;

            // Transcription provider
            section["Provider"] = Provider.ToString();
            section["CloudTranscriptionProvider"] = CloudTranscriptionProvider;

            if (Provider == TranscriptionProvider.OpenAI)
            {
                if (CloudTranscriptionProvider == "Groq")
                {
                    var groqTx = SettingsViewModel.EnsureObject(section, "GroqTranscription");
                    if (!string.IsNullOrWhiteSpace(GroqTranscriptionApiKey))
                        groqTx["ApiKey"] = GroqTranscriptionApiKey;
                    groqTx["Model"] = GroqTranscriptionModel;
                }
                else
                {
                    var openAi = SettingsViewModel.EnsureObject(section, "OpenAI");
                    if (!string.IsNullOrWhiteSpace(OpenAiApiKey))
                        openAi["ApiKey"] = OpenAiApiKey;
                    openAi["Model"] = OpenAiTranscriptionModel;
                }
            }
            else if (Provider == TranscriptionProvider.Local)
            {
                var local = SettingsViewModel.EnsureObject(section, "Local");
                local["GpuAcceleration"] = LocalGpuAcceleration;
                local["ModelName"] = LocalModelName;
            }
            else if (Provider == TranscriptionProvider.Parakeet)
            {
                var parakeet = SettingsViewModel.EnsureObject(section, "Parakeet");
                parakeet["GpuAcceleration"] = ParakeetGpuAcceleration;
                parakeet["ModelName"] = ParakeetModelName;
            }

            // Text correction
            var correction = SettingsViewModel.EnsureObject(section, "TextCorrection");
            correction["Provider"] = CorrectionProvider.ToString();

            if (CorrectionProvider is TextCorrectionProvider.OpenAI or TextCorrectionProvider.Cloud)
            {
                correction["Model"] = CorrectionModel;
                if (!string.IsNullOrWhiteSpace(OpenAiApiKey))
                    SettingsViewModel.EnsureObject(section, "OpenAI")["ApiKey"] = OpenAiApiKey;
            }
            else if (CorrectionProvider == TextCorrectionProvider.Anthropic)
            {
                var anthropic = SettingsViewModel.EnsureObject(correction, "Anthropic");
                if (!string.IsNullOrWhiteSpace(AnthropicApiKey))
                    anthropic["ApiKey"] = AnthropicApiKey;
                anthropic["Model"] = AnthropicModel;
            }
            else if (CorrectionProvider == TextCorrectionProvider.Google)
            {
                var google = SettingsViewModel.EnsureObject(correction, "Google");
                if (!string.IsNullOrWhiteSpace(GoogleApiKey))
                    google["ApiKey"] = GoogleApiKey;
                google["Model"] = GoogleModel;
            }
            else if (CorrectionProvider == TextCorrectionProvider.Groq)
            {
                var groq = SettingsViewModel.EnsureObject(correction, "Groq");
                if (!string.IsNullOrWhiteSpace(GroqCorrectionApiKey))
                    groq["ApiKey"] = GroqCorrectionApiKey;
                groq["Model"] = GroqCorrectionModel;
            }
            else if (CorrectionProvider == TextCorrectionProvider.Custom)
            {
                var custom = SettingsViewModel.EnsureObject(correction, "Custom");
                if (!string.IsNullOrWhiteSpace(CustomCorrectionEndpoint))
                    custom["Endpoint"] = CustomCorrectionEndpoint;
                if (!string.IsNullOrWhiteSpace(CustomCorrectionApiKey))
                    custom["ApiKey"] = CustomCorrectionApiKey;
                if (!string.IsNullOrWhiteSpace(CustomCorrectionModel))
                    custom["Model"] = CustomCorrectionModel;
            }
            else if (CorrectionProvider == TextCorrectionProvider.Local)
            {
                correction["LocalModelName"] = CorrectionLocalModelName;
                correction["LocalGpuAcceleration"] = CorrectionGpuAcceleration;
            }

            // Microphone
            SettingsViewModel.EnsureObject(section, "Audio")["DeviceIndex"] = SelectedMicrophoneIndex;

            // Mark setup as completed
            SettingsViewModel.EnsureObject(section, "App")["SetupCompleted"] = true;
        });

        IsCompleted = true;
        OnPropertyChanged(nameof(IsCompleted));
    }

    // --- Local model download ---

    [RelayCommand]
    internal async Task DownloadWhisperModel(ModelItemViewModel item)
    {
        if (item.IsDownloading) return;

        item.IsDownloading = true;
        item.StatusText = "Downloading...";
        item.DownloadProgress = 0;

        try
        {
            var progress = new Progress<float>(p =>
            {
                _dispatcher.Invoke(() =>
                {
                    item.DownloadProgress = p;
                    item.StatusText = $"Downloading... {p * 100:F0}%";
                });
            });

            await _modelManager.DownloadModelAsync(item.GgmlType, progress);

            item.IsDownloaded = true;
            item.StatusText = "Downloaded";
            _logger.LogInformation("Whisper model {Name} downloaded in wizard", item.Name);

            if (!WhisperModels.Any(m => m.IsActive))
                SelectWhisperModel(item);
        }
        catch (Exception ex)
        {
            item.StatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to download Whisper model {Name}", item.Name);
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    [RelayCommand]
    internal void SelectWhisperModel(ModelItemViewModel item)
    {
        if (!item.IsDownloaded) return;

        foreach (var m in WhisperModels)
        {
            m.IsActive = false;
            if (m.IsDownloaded) m.StatusText = "Downloaded";
        }
        foreach (var m in ParakeetModels)
        {
            m.IsActive = false;
            if (m.IsDownloaded) m.StatusText = "Downloaded";
        }

        item.IsActive = true;
        item.StatusText = "Active";
        LocalModelName = item.FileName;
        _preloadService.UnloadParakeetModel();
        _preloadService.PreloadTranscriptionModel(item.FileName);
        UpdateCanGoNext();
    }

    [RelayCommand]
    internal async Task DownloadParakeetModel(ParakeetModelItemViewModel item)
    {
        if (item.IsDownloading) return;

        item.IsDownloading = true;
        item.StatusText = "Downloading...";
        item.DownloadProgress = 0;

        try
        {
            var progress = new Progress<float>(p =>
            {
                _dispatcher.Invoke(() =>
                {
                    item.DownloadProgress = p;
                    item.StatusText = $"Downloading... {p * 100:F0}%";
                });
            });

            await _parakeetModelManager.DownloadModelAsync(item.DirectoryName, progress);

            item.IsDownloaded = true;
            item.StatusText = "Downloaded";
            _logger.LogInformation("Parakeet model {Name} downloaded in wizard", item.Name);

            if (!ParakeetModels.Any(m => m.IsActive))
                SelectParakeetModel(item);
        }
        catch (Exception ex)
        {
            item.StatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to download Parakeet model {Name}", item.Name);
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    [RelayCommand]
    internal void SelectParakeetModel(ParakeetModelItemViewModel item)
    {
        if (!item.IsDownloaded) return;

        foreach (var m in ParakeetModels)
        {
            m.IsActive = false;
            if (m.IsDownloaded) m.StatusText = "Downloaded";
        }
        foreach (var m in WhisperModels)
        {
            m.IsActive = false;
            if (m.IsDownloaded) m.StatusText = "Downloaded";
        }

        item.IsActive = true;
        item.StatusText = "Active";
        ParakeetModelName = item.DirectoryName;
        _preloadService.UnloadTranscriptionModel();
        _preloadService.PreloadParakeetModel();
        UpdateCanGoNext();
    }

    [RelayCommand]
    internal async Task DownloadCorrectionModel(CorrectionModelItemViewModel item)
    {
        if (item.IsDownloading) return;

        item.IsDownloading = true;
        item.StatusText = "Downloading...";
        item.DownloadProgress = 0;

        try
        {
            var progress = new Progress<float>(p =>
            {
                _dispatcher.Invoke(() =>
                {
                    item.DownloadProgress = p;
                    item.StatusText = $"Downloading... {p * 100:F0}%";
                });
            });

            await _correctionModelManager.DownloadModelAsync(item.FileName, progress);

            item.IsDownloaded = true;
            item.StatusText = "Downloaded";
            _logger.LogInformation("Correction model {Name} downloaded in wizard", item.Name);

            if (!CorrectionModels.Any(m => m.IsActive))
                SelectCorrectionLocalModel(item);
        }
        catch (Exception ex)
        {
            item.StatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to download correction model {Name}", item.Name);
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    [RelayCommand]
    internal void SelectCorrectionLocalModel(CorrectionModelItemViewModel item)
    {
        if (!item.IsDownloaded) return;

        foreach (var m in CorrectionModels)
        {
            m.IsActive = false;
            if (m.IsDownloaded) m.StatusText = "Downloaded";
        }

        item.IsActive = true;
        item.StatusText = "Active";
        CorrectionLocalModelName = item.FileName;
        _preloadService.PreloadCorrectionModel(item.FileName);
        UpdateCanGoNext();
    }

    private void LoadCorrectionModels()
    {
        CorrectionModels.Clear();
        foreach (var model in _correctionModelManager.GetAllModels())
        {
            var item = new CorrectionModelItemViewModel(model);
            if (CorrectionProvider == TextCorrectionProvider.Local
                && model.FileName == CorrectionLocalModelName
                && model.IsDownloaded)
            {
                item.IsActive = true;
                item.StatusText = "Active";
            }
            CorrectionModels.Add(item);
        }
    }

    private void LoadWhisperModels()
    {
        WhisperModels.Clear();
        foreach (var model in _modelManager.GetAllModels())
        {
            var ggmlType = FileNameToGgmlType(model.FileName);
            var item = new ModelItemViewModel(model, ggmlType);
            if (Provider == TranscriptionProvider.Local && model.FileName == LocalModelName && model.IsDownloaded)
            {
                item.IsActive = true;
                item.StatusText = "Active";
            }
            WhisperModels.Add(item);
        }
    }

    private void LoadParakeetModels()
    {
        ParakeetModels.Clear();
        foreach (var model in _parakeetModelManager.GetAllModels())
        {
            var item = new ParakeetModelItemViewModel(model);
            if (Provider == TranscriptionProvider.Parakeet && model.DirectoryName == ParakeetModelName && model.IsDirectoryComplete)
            {
                item.IsActive = true;
                item.StatusText = "Active";
            }
            ParakeetModels.Add(item);
        }
    }

    /// <summary>Maps a Whisper GGML model filename (e.g. "ggml-small.bin") to its corresponding <see cref="GgmlType"/> enum value.</summary>
    internal static GgmlType FileNameToGgmlType(string fileName) => fileName switch
    {
        "ggml-tiny.bin" => GgmlType.Tiny,
        "ggml-base.bin" => GgmlType.Base,
        "ggml-small.bin" => GgmlType.Small,
        "ggml-medium.bin" => GgmlType.Medium,
        "ggml-large-v3.bin" => GgmlType.LargeV3,
        "ggml-large-v3-turbo.bin" => GgmlType.LargeV3Turbo,
        _ => throw new ArgumentException($"Unknown model: {fileName}")
    };

    // --- Microphone loading ---

    private void LoadMicrophones()
    {
        AvailableMicrophones.Clear();
        foreach (var mic in GeneralSettingsViewModel.EnumerateMicrophones())
            AvailableMicrophones.Add(mic);
    }
}
