using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using WriteSpeech.App.ViewModels.Settings;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Configuration;

namespace WriteSpeech.App.ViewModels;

public enum SetupStep { Welcome, Transcription, Correction, Microphone }

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly ISettingsPersistenceService _persistenceService;
    private readonly ILogger<SetupWizardViewModel> _logger;
    private readonly IDispatcherService _dispatcher;

    // --- Step navigation ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStepIndex))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    private SetupStep _currentStep = SetupStep.Welcome;

    public int CurrentStepIndex => (int)CurrentStep;
    public bool CanGoBack => CurrentStep != SetupStep.Welcome;
    public bool IsLastStep => CurrentStep == SetupStep.Microphone;

    [ObservableProperty] private bool _canGoNext = true;

    // --- Step 1: Language ---
    [ObservableProperty] private string? _selectedLanguageCode;
    [ObservableProperty] private bool _isAutoDetectLanguage = true;

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

    // --- Step 3: Text Correction ---
    [ObservableProperty] private TextCorrectionProvider _correctionProvider = TextCorrectionProvider.Off;
    [ObservableProperty] private string _correctionModel = "gpt-4.1-mini";
    [ObservableProperty] private string _anthropicApiKey = "";
    [ObservableProperty] private string _anthropicModel = "claude-sonnet-4-6";
    [ObservableProperty] private string _googleApiKey = "";
    [ObservableProperty] private string _googleModel = "gemini-3-flash-preview";
    [ObservableProperty] private string _groqCorrectionApiKey = "";
    [ObservableProperty] private string _groqCorrectionModel = "qwen/qwen3-32b";

    public bool IsReusableOpenAiKey =>
        CorrectionProvider is TextCorrectionProvider.OpenAI or TextCorrectionProvider.Cloud
        && !string.IsNullOrWhiteSpace(OpenAiApiKey);

    // --- Step 4: Microphone ---
    [ObservableProperty] private int _selectedMicrophoneIndex;
    public ObservableCollection<MicrophoneInfo> AvailableMicrophones { get; } = [];

    // --- Mic test ---
    [ObservableProperty] private bool _isMicTesting;
    [ObservableProperty] private float _micTestLevel;
    private WaveInEvent? _micTestWaveIn;

    // --- Result ---
    public bool IsCompleted { get; private set; }

    public SetupWizardViewModel(
        ISettingsPersistenceService persistenceService,
        IDispatcherService dispatcher,
        ILogger<SetupWizardViewModel> logger,
        WriteSpeechOptions? existingOptions = null)
    {
        _persistenceService = persistenceService;
        _dispatcher = dispatcher;
        _logger = logger;

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

            // Correction
            _correctionProvider = existingOptions.TextCorrection.Provider;
            _correctionModel = existingOptions.TextCorrection.Model ?? "gpt-4.1-mini";
            _anthropicApiKey = existingOptions.TextCorrection.Anthropic.ApiKey ?? "";
            _anthropicModel = existingOptions.TextCorrection.Anthropic.Model ?? "claude-sonnet-4-6";
            _googleApiKey = existingOptions.TextCorrection.Google.ApiKey ?? "";
            _googleModel = existingOptions.TextCorrection.Google.Model ?? "gemini-3-flash-preview";
            _groqCorrectionApiKey = existingOptions.TextCorrection.Groq.ApiKey ?? "";
            _groqCorrectionModel = existingOptions.TextCorrection.Groq.Model ?? "qwen/qwen3-32b";

            // Language & Mic
            _selectedLanguageCode = existingOptions.Language;
            _isAutoDetectLanguage = existingOptions.Language == null;
            _selectedMicrophoneIndex = existingOptions.Audio.DeviceIndex;
        }

        LoadMicrophones();
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

    internal void SetAnthropicApiKey(string key) => AnthropicApiKey = key.Trim();
    internal void SetGoogleApiKey(string key) => GoogleApiKey = key.Trim();
    internal void SetGroqCorrectionApiKey(string key) => GroqCorrectionApiKey = key.Trim();

    // --- Mic test ---

    internal void StartMicTest()
    {
        try
        {
            _micTestWaveIn = new WaveInEvent
            {
                DeviceNumber = SelectedMicrophoneIndex,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50
            };
            _micTestWaveIn.DataAvailable += OnMicTestDataAvailable;
            _micTestWaveIn.StartRecording();
            IsMicTesting = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start mic test");
            StopMicTest();
        }
    }

    internal void StopMicTest()
    {
        if (_micTestWaveIn is not null)
        {
            _micTestWaveIn.DataAvailable -= OnMicTestDataAvailable;
            try { _micTestWaveIn.StopRecording(); } catch { /* device may already be gone */ }
            _micTestWaveIn.Dispose();
            _micTestWaveIn = null;
        }
        IsMicTesting = false;
        MicTestLevel = 0;
    }

    private void OnMicTestDataAvailable(object? sender, WaveInEventArgs e)
    {
        double sumOfSquares = 0;
        int sampleCount = e.BytesRecorded / 2;
        for (int i = 0; i < e.BytesRecorded; i += 2)
        {
            short sample = BitConverter.ToInt16(e.Buffer, i);
            double normalized = sample / 32768.0;
            sumOfSquares += normalized * normalized;
        }

        float rms = sampleCount > 0 ? (float)Math.Sqrt(sumOfSquares / sampleCount) : 0;
        float level = Math.Min(rms * 3.5f, 1.0f);

        _dispatcher.Invoke(() => MicTestLevel = level);
    }

    // --- Validation ---

    internal void UpdateCanGoNext()
    {
        CanGoNext = CurrentStep switch
        {
            SetupStep.Transcription => Provider != TranscriptionProvider.OpenAI
                || CloudTranscriptionProvider switch
                {
                    "Groq" => !string.IsNullOrWhiteSpace(GroqTranscriptionApiKey),
                    _ => !string.IsNullOrWhiteSpace(OpenAiApiKey)
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
                SettingsViewModel.EnsureObject(section, "Local")["GpuAcceleration"] = LocalGpuAcceleration;
            }
            else if (Provider == TranscriptionProvider.Parakeet)
            {
                SettingsViewModel.EnsureObject(section, "Parakeet")["GpuAcceleration"] = ParakeetGpuAcceleration;
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

            // Microphone
            SettingsViewModel.EnsureObject(section, "Audio")["DeviceIndex"] = SelectedMicrophoneIndex;

            // Mark setup as completed
            SettingsViewModel.EnsureObject(section, "App")["SetupCompleted"] = true;
        });

        IsCompleted = true;
        OnPropertyChanged(nameof(IsCompleted));
    }

    // --- Microphone loading ---

    private void LoadMicrophones()
    {
        AvailableMicrophones.Clear();

        var deviceCount = WaveInEvent.DeviceCount;
        for (int i = 0; i < deviceCount; i++)
        {
            try
            {
                var caps = WaveInEvent.GetCapabilities(i);
                AvailableMicrophones.Add(new MicrophoneInfo(i, caps.ProductName));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get capabilities for audio device {Index}", i);
            }
        }

        if (AvailableMicrophones.Count == 0)
            AvailableMicrophones.Add(new MicrophoneInfo(0, "No devices found"));
    }
}
