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
    [ObservableProperty] private string _openAiApiKey = "";

    // --- Step 3: Text Correction ---
    [ObservableProperty] private TextCorrectionProvider _correctionProvider = TextCorrectionProvider.Off;
    [ObservableProperty] private string _correctionApiKey = "";

    public bool IsReusableOpenAiKey =>
        CorrectionProvider is TextCorrectionProvider.OpenAI or TextCorrectionProvider.Cloud
        && !string.IsNullOrWhiteSpace(OpenAiApiKey);

    public bool NeedsCorrectionApiKey =>
        CorrectionProvider is TextCorrectionProvider.Anthropic or TextCorrectionProvider.Google or TextCorrectionProvider.Groq
        || (CorrectionProvider is TextCorrectionProvider.OpenAI or TextCorrectionProvider.Cloud
            && string.IsNullOrWhiteSpace(OpenAiApiKey));

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
        ILogger<SetupWizardViewModel> logger)
    {
        _persistenceService = persistenceService;
        _dispatcher = dispatcher;
        _logger = logger;
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
            OnPropertyChanged(nameof(NeedsCorrectionApiKey));
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
            OnPropertyChanged(nameof(NeedsCorrectionApiKey));
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
        OnPropertyChanged(nameof(NeedsCorrectionApiKey));
    }

    internal void SetCorrectionApiKey(string key)
    {
        CorrectionApiKey = key.Trim();
        UpdateCanGoNext();
    }

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
                || !string.IsNullOrWhiteSpace(OpenAiApiKey),
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

            if (Provider == TranscriptionProvider.OpenAI && !string.IsNullOrWhiteSpace(OpenAiApiKey))
            {
                var openAi = SettingsViewModel.EnsureObject(section, "OpenAI");
                openAi["ApiKey"] = OpenAiApiKey;
            }

            // Text correction
            var correction = SettingsViewModel.EnsureObject(section, "TextCorrection");
            correction["Provider"] = CorrectionProvider.ToString();

            if (CorrectionProvider is TextCorrectionProvider.OpenAI or TextCorrectionProvider.Cloud)
            {
                // Reuse OpenAI API key if available, otherwise use correction key
                if (!string.IsNullOrWhiteSpace(OpenAiApiKey))
                {
                    var openAi = SettingsViewModel.EnsureObject(section, "OpenAI");
                    openAi["ApiKey"] = OpenAiApiKey;
                }
                else if (!string.IsNullOrWhiteSpace(CorrectionApiKey))
                {
                    var openAi = SettingsViewModel.EnsureObject(section, "OpenAI");
                    openAi["ApiKey"] = CorrectionApiKey;
                }
            }
            else if (CorrectionProvider == TextCorrectionProvider.Anthropic
                     && !string.IsNullOrWhiteSpace(CorrectionApiKey))
            {
                var anthropic = SettingsViewModel.EnsureObject(correction, "Anthropic");
                anthropic["ApiKey"] = CorrectionApiKey;
            }
            else if (CorrectionProvider == TextCorrectionProvider.Google
                     && !string.IsNullOrWhiteSpace(CorrectionApiKey))
            {
                var google = SettingsViewModel.EnsureObject(correction, "Google");
                google["ApiKey"] = CorrectionApiKey;
            }
            else if (CorrectionProvider == TextCorrectionProvider.Groq
                     && !string.IsNullOrWhiteSpace(CorrectionApiKey))
            {
                var groq = SettingsViewModel.EnsureObject(correction, "Groq");
                groq["ApiKey"] = CorrectionApiKey;
            }

            // Microphone
            SettingsViewModel.EnsureObject(section, "Audio")["DeviceIndex"] = SelectedMicrophoneIndex;

            // Mark setup as completed
            SettingsViewModel.EnsureObject(section, "App")["SetupCompleted"] = true;
        });

        IsCompleted = true;
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
