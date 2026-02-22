using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Models;
using Whisper.net.Ggml;
using WhisperShow.Core.Services.Hotkey;
using WhisperShow.Core.Services.ModelManagement;
using WhisperShow.Core.Services.Snippets;
using WhisperShow.Core.Services.Statistics;
using WhisperShow.Core.Services.TextCorrection;

namespace WhisperShow.App.ViewModels;

public enum SettingsPage
{
    General,
    System,
    Models,
    Dictionary,
    Snippets,
    Statistics
}

public record MicrophoneInfo(int DeviceIndex, string Name);
public record LanguageInfo(string Code, string DisplayName, string Flag);

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly IDictionaryService _dictionaryService;
    private readonly ISnippetService _snippetService;
    private readonly IUsageStatsService _statsService;
    private readonly IModelManager _modelManager;
    private readonly ICorrectionModelManager _correctionModelManager;
    private readonly IModelPreloadService _preloadService;
    private CancellationTokenSource? _saveCts;

    // --- Page navigation ---
    [ObservableProperty]
    private SettingsPage _selectedPage = SettingsPage.General;

    // --- Dialog system ---
    [ObservableProperty] private bool _isDialogOpen;
    [ObservableProperty] private string _activeDialog = "";

    // --- General: Toggle hotkey ---
    [ObservableProperty] private string _toggleModifiers = "Control, Shift";
    [ObservableProperty] private string _toggleKey = "Space";
    [ObservableProperty] private string _toggleDisplayText = "";
    public ObservableCollection<string> ToggleBadges { get; } = [];

    // --- General: Push-to-Talk hotkey ---
    [ObservableProperty] private string _pttModifiers = "Control";
    [ObservableProperty] private string _pttKey = "Space";
    [ObservableProperty] private string _pttDisplayText = "";
    public ObservableCollection<string> PttBadges { get; } = [];

    // --- General: Hotkey capture state ---
    [ObservableProperty] private string _capturingHotkey = ""; // "", "Toggle", "PushToTalk"
    [ObservableProperty] private string _hotkeyDisplayText = "";

    // --- General: Microphone ---
    [ObservableProperty] private int _selectedMicrophoneIndex;
    [ObservableProperty] private string _selectedMicrophoneDisplay = "";
    public ObservableCollection<MicrophoneInfo> AvailableMicrophones { get; } = [];

    // --- General: Language ---
    [ObservableProperty] private string? _selectedLanguageCode;
    [ObservableProperty] private string _selectedLanguageDisplay = "";
    [ObservableProperty] private bool _isAutoDetectLanguage;
    [ObservableProperty] private string? _pendingLanguageCode;

    public ObservableCollection<LanguageInfo> AvailableLanguages { get; } =
    [
        new("de", "German", "\U0001F1E9\U0001F1EA"),
        new("en", "English", "\U0001F1EC\U0001F1E7"),
        new("fr", "French", "\U0001F1EB\U0001F1F7"),
        new("es", "Spanish", "\U0001F1EA\U0001F1F8"),
        new("it", "Italian", "\U0001F1EE\U0001F1F9"),
        new("pt", "Portuguese", "\U0001F1F5\U0001F1F9"),
        new("nl", "Dutch", "\U0001F1F3\U0001F1F1"),
        new("pl", "Polish", "\U0001F1F5\U0001F1F1"),
        new("ru", "Russian", "\U0001F1F7\U0001F1FA"),
        new("uk", "Ukrainian", "\U0001F1FA\U0001F1E6"),
        new("zh", "Chinese", "\U0001F1E8\U0001F1F3"),
        new("ja", "Japanese", "\U0001F1EF\U0001F1F5"),
        new("ko", "Korean", "\U0001F1F0\U0001F1F7"),
        new("ar", "Arabic", "\U0001F1F8\U0001F1E6"),
        new("tr", "Turkish", "\U0001F1F9\U0001F1F7"),
        new("sv", "Swedish", "\U0001F1F8\U0001F1EA"),
        new("da", "Danish", "\U0001F1E9\U0001F1F0"),
        new("no", "Norwegian", "\U0001F1F3\U0001F1F4"),
        new("fi", "Finnish", "\U0001F1EB\U0001F1EE"),
        new("cs", "Czech", "\U0001F1E8\U0001F1FF"),
    ];

    // --- System: App settings ---
    [ObservableProperty] private bool _launchAtLogin;
    [ObservableProperty] private bool _overlayAlwaysVisible = true;
    [ObservableProperty] private bool _showInTaskbar;
    [ObservableProperty] private bool _isDarkMode;

    // --- System: Sound ---
    [ObservableProperty] private bool _soundEffectsEnabled = true;
    [ObservableProperty] private bool _muteWhileDictating = true;

    // --- System: Audio Compression ---
    [ObservableProperty] private bool _audioCompressionEnabled = true;

    // --- Models: Text Correction ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCloudUsageHint))]
    private TextCorrectionProvider _correctionProvider = TextCorrectionProvider.Off;
    [ObservableProperty] private string _correctionCloudModel = "gpt-4o-mini";
    [ObservableProperty] private bool _isEditingCorrectionModel;
    [ObservableProperty] private bool _correctionGpuAcceleration = true;

    // --- Models: Combined Audio Model ---
    [ObservableProperty] private bool _useCombinedAudioModel;
    [ObservableProperty] private string _combinedAudioModel = "gpt-4o-mini-audio-preview";
    [ObservableProperty] private bool _isEditingCombinedAudioModel;

    // --- System: Auto-dismiss ---
    [ObservableProperty] private int _autoDismissSeconds = 10;
    [ObservableProperty] private bool _isEditingAutoDismiss;

    // --- System: Max recording ---
    [ObservableProperty] private int _maxRecordingSeconds = 300;
    [ObservableProperty] private bool _isEditingMaxRecording;

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

    // --- Dictionary ---
    public ObservableCollection<string> DictionaryEntries { get; } = [];
    [ObservableProperty] private string _newDictionaryWord = "";

    // --- Snippets ---
    public ObservableCollection<SnippetEntry> SnippetItems { get; } = [];
    [ObservableProperty] private string _newSnippetTrigger = "";
    [ObservableProperty] private string _newSnippetReplacement = "";

    // --- Models ---
    public ObservableCollection<ModelItemViewModel> ModelItems { get; } = [];
    public ObservableCollection<CorrectionModelItemViewModel> CorrectionModelItems { get; } = [];

    // --- Statistics ---
    [ObservableProperty] private int _totalTranscriptions;
    [ObservableProperty] private string _totalRecordingTimeDisplay = "0:00";
    [ObservableProperty] private string _averageDurationDisplay = "0.0s";
    [ObservableProperty] private string _estimatedCostDisplay = "$0.00";
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private string _providerBreakdownDisplay = "";

    // --- Cloud usage hint ---
    public bool ShowCloudUsageHint =>
        Provider == TranscriptionProvider.Local && CorrectionProvider == TextCorrectionProvider.Cloud;

    // Version
    public string VersionText => $"WhisperShow v{GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

    public SettingsViewModel(
        IOptions<WhisperShowOptions> options,
        IGlobalHotkeyService hotkeyService,
        IDictionaryService dictionaryService,
        ISnippetService snippetService,
        IUsageStatsService statsService,
        IModelManager modelManager,
        ICorrectionModelManager correctionModelManager,
        IModelPreloadService preloadService,
        ILogger<SettingsViewModel> logger)
    {
        _logger = logger;
        _hotkeyService = hotkeyService;
        _dictionaryService = dictionaryService;
        _snippetService = snippetService;
        _statsService = statsService;
        _modelManager = modelManager;
        _correctionModelManager = correctionModelManager;
        _preloadService = preloadService;

        var opts = options.Value;

        // General: Hotkeys
        _toggleModifiers = opts.Hotkey.Toggle.Modifiers;
        _toggleKey = opts.Hotkey.Toggle.Key;
        _pttModifiers = opts.Hotkey.PushToTalk.Modifiers;
        _pttKey = opts.Hotkey.PushToTalk.Key;
        _selectedMicrophoneIndex = opts.Audio.DeviceIndex;
        _selectedLanguageCode = opts.Language;
        _isAutoDetectLanguage = opts.Language == null;

        // System: App settings
        _launchAtLogin = opts.App.LaunchAtLogin;
        _overlayAlwaysVisible = opts.Overlay.AlwaysVisible;
        _showInTaskbar = opts.Overlay.ShowInTaskbar;
        _isDarkMode = string.Equals(opts.App.Theme, "Dark", StringComparison.OrdinalIgnoreCase);

        // System: Sound
        _soundEffectsEnabled = opts.App.SoundEffects;
        _muteWhileDictating = opts.Audio.MuteWhileDictating;

        // System: Audio
        _audioCompressionEnabled = opts.Audio.CompressBeforeUpload;

        // Models: Text Correction
        _correctionProvider = opts.TextCorrection.Provider;
        _correctionCloudModel = opts.TextCorrection.Model;
        _correctionGpuAcceleration = opts.TextCorrection.LocalGpuAcceleration;
        _correctionLocalModelName = opts.TextCorrection.LocalModelName;
        _useCombinedAudioModel = opts.TextCorrection.UseCombinedAudioModel;
        _combinedAudioModel = opts.TextCorrection.CombinedAudioModel;
        _autoDismissSeconds = opts.Overlay.AutoDismissSeconds;
        _maxRecordingSeconds = opts.Audio.MaxRecordingSeconds;

        // Transcription
        _provider = opts.Provider;
        _openAiEndpoint = opts.OpenAI.Endpoint ?? "";
        _openAiApiKey = opts.OpenAI.ApiKey ?? "";
        _openAiModelName = opts.OpenAI.Model;
        _localModelName = opts.Local.ModelName;
        _transcriptionModel = opts.Provider == TranscriptionProvider.OpenAI
            ? _openAiModelName
            : _localModelName;
        _gpuAcceleration = opts.Local.GpuAcceleration;

        LoadMicrophones();
        LoadDictionaryEntries();
        LoadSnippets();
        UpdateDisplayTexts();
        UpdateToggleBadges();
        UpdatePttBadges();
    }

    private void LoadMicrophones()
    {
        AvailableMicrophones.Clear();

        var deviceCount = WaveInEvent.DeviceCount;
        for (int i = 0; i < deviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            AvailableMicrophones.Add(new MicrophoneInfo(i, caps.ProductName));
        }

        if (AvailableMicrophones.Count == 0)
            AvailableMicrophones.Add(new MicrophoneInfo(0, "No devices found"));
    }

    private static string FormatKeys(string modifiers, string key)
        => $"{modifiers.Replace(",", " +").Replace("Control", "Ctrl")} + {key}";

    private void UpdateDisplayTexts()
    {
        // Hotkeys
        var toggleKeys = FormatKeys(ToggleModifiers, ToggleKey);
        var pttKeys = FormatKeys(PttModifiers, PttKey);
        ToggleDisplayText = $"Press {toggleKeys} to start and stop.";
        PttDisplayText = $"Hold {pttKeys} and speak.";
        HotkeyDisplayText = $"Toggle: {toggleKeys} · PTT: {pttKeys}";

        // Microphone
        var mic = AvailableMicrophones.FirstOrDefault(m => m.DeviceIndex == SelectedMicrophoneIndex);
        SelectedMicrophoneDisplay = mic is not null
            ? (SelectedMicrophoneIndex == 0 ? $"Auto-detect ({mic.Name})" : mic.Name)
            : "Auto-detect";

        // Language
        if (IsAutoDetectLanguage || SelectedLanguageCode == null)
        {
            SelectedLanguageDisplay = "Auto-detect";
        }
        else
        {
            var lang = AvailableLanguages.FirstOrDefault(l => l.Code == SelectedLanguageCode);
            SelectedLanguageDisplay = lang?.DisplayName ?? "Auto-detect";
        }

        // API Key
        OpenAiApiKeyDisplay = string.IsNullOrEmpty(OpenAiApiKey)
            ? "Not configured"
            : $"sk-...{OpenAiApiKey[^4..]}";
    }

    private static void UpdateBadges(ObservableCollection<string> badges, string modifiers, string key)
    {
        badges.Clear();
        foreach (var mod in modifiers.Split(',', StringSplitOptions.TrimEntries))
        {
            badges.Add(mod == "Control" ? "Ctrl" : mod);
        }
        badges.Add(key);
    }

    private void UpdateToggleBadges() => UpdateBadges(ToggleBadges, ToggleModifiers, ToggleKey);
    private void UpdatePttBadges() => UpdateBadges(PttBadges, PttModifiers, PttKey);

    // --- Navigation ---

    [RelayCommand]
    private void Navigate(SettingsPage page)
    {
        SelectedPage = page;
        if (page == SettingsPage.Statistics)
            RefreshStats();
        else if (page == SettingsPage.Models)
        {
            RefreshModels();
            RefreshCorrectionModels();
        }
    }

    // --- Dialog system ---

    [RelayCommand]
    private void OpenHotkeyDialog()
    {
        ActiveDialog = "Hotkey";
        IsDialogOpen = true;
        CapturingHotkey = "";
    }

    [RelayCommand]
    private void OpenMicrophoneDialog()
    {
        ActiveDialog = "Microphone";
        IsDialogOpen = true;
    }

    [RelayCommand]
    private void OpenLanguageDialog()
    {
        PendingLanguageCode = IsAutoDetectLanguage ? null : SelectedLanguageCode;
        ActiveDialog = "Language";
        IsDialogOpen = true;
    }

    [RelayCommand]
    private void CloseDialog()
    {
        IsDialogOpen = false;
        ActiveDialog = "";
        CapturingHotkey = "";
    }

    // --- Hotkey dialog ---

    [RelayCommand]
    private void StartCapturingToggleHotkey() => CapturingHotkey = "Toggle";

    [RelayCommand]
    private void StartCapturingPttHotkey() => CapturingHotkey = "PushToTalk";

    public void ApplyNewHotkey(string modifiers, string key)
    {
        if (CapturingHotkey == "Toggle")
        {
            ToggleModifiers = modifiers;
            ToggleKey = key;
            UpdateToggleBadges();
            _hotkeyService.UpdateToggleHotkey(modifiers, key);
        }
        else if (CapturingHotkey == "PushToTalk")
        {
            PttModifiers = modifiers;
            PttKey = key;
            UpdatePttBadges();
            _hotkeyService.UpdatePushToTalkHotkey(modifiers, key);
        }

        CapturingHotkey = "";
        UpdateDisplayTexts();
        ScheduleSave();
    }

    [RelayCommand]
    private void ResetHotkeyToDefault()
    {
        ToggleModifiers = "Control, Shift";
        ToggleKey = "Space";
        PttModifiers = "Control";
        PttKey = "Space";
        CapturingHotkey = "";
        UpdateToggleBadges();
        UpdatePttBadges();
        UpdateDisplayTexts();
        _hotkeyService.UpdateToggleHotkey("Control, Shift", "Space");
        _hotkeyService.UpdatePushToTalkHotkey("Control", "Space");
        ScheduleSave();
    }

    // --- Microphone dialog ---

    public void SelectMicrophone(int deviceIndex)
    {
        SelectedMicrophoneIndex = deviceIndex;
        UpdateDisplayTexts();
        CloseDialog();
        ScheduleSave();
    }

    // --- Language dialog ---

    [RelayCommand]
    private void SelectLanguage(string code)
    {
        PendingLanguageCode = code;
        IsAutoDetectLanguage = false;
    }

    [RelayCommand]
    private void ToggleAutoDetectLanguage()
    {
        IsAutoDetectLanguage = !IsAutoDetectLanguage;
        if (IsAutoDetectLanguage)
            PendingLanguageCode = null;
    }

    [RelayCommand]
    private void SaveAndCloseLanguage()
    {
        SelectedLanguageCode = IsAutoDetectLanguage ? null : PendingLanguageCode;
        UpdateDisplayTexts();
        CloseDialog();
        ScheduleSave();
    }

    // --- System: App settings ---

    [RelayCommand]
    private void ToggleLaunchAtLogin()
    {
        // IsChecked two-way binding already flipped the value
        SetAutoStart(LaunchAtLogin);
        ScheduleSave();
    }

    [RelayCommand]
    private void ToggleOverlayAlwaysVisible() => ScheduleSave();

    [RelayCommand]
    private void ToggleShowInTaskbar() => ScheduleSave();

    [RelayCommand]
    private void ToggleDarkMode() => ScheduleSave();

    private void SetAutoStart(bool enable)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                _logger.LogWarning("Cannot set autostart: ProcessPath is null");
                return;
            }

            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null)
            {
                _logger.LogWarning("Cannot open Run registry key for writing");
                return;
            }

            if (enable)
                key.SetValue("WhisperShow", $"\"{exePath}\"");
            else
                key.DeleteValue("WhisperShow", throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Action} autostart registry entry",
                enable ? "set" : "remove");
        }
    }

    // --- System: Sound ---

    [RelayCommand]
    private void ToggleSoundEffects() => ScheduleSave();

    [RelayCommand]
    private void ToggleMuteWhileDictating() => ScheduleSave();

    // --- System: Audio ---

    [RelayCommand]
    private void ToggleAudioCompression() => ScheduleSave();

    [RelayCommand]
    private void StartEditingAutoDismiss() => IsEditingAutoDismiss = true;

    public void ApplyAutoDismiss(int seconds)
    {
        AutoDismissSeconds = seconds;
        IsEditingAutoDismiss = false;
        ScheduleSave();
    }

    [RelayCommand]
    private void StartEditingMaxRecording() => IsEditingMaxRecording = true;

    public void ApplyMaxRecording(int seconds)
    {
        MaxRecordingSeconds = seconds;
        IsEditingMaxRecording = false;
        ScheduleSave();
    }

    // --- Transcription ---

    [RelayCommand]
    private void StartEditingProvider() => IsEditingProvider = true;

    public void ApplyProvider(TranscriptionProvider provider)
    {
        // Persist current model name before switching
        if (Provider == TranscriptionProvider.OpenAI) _openAiModelName = TranscriptionModel;
        else if (Provider == TranscriptionProvider.Local) _localModelName = TranscriptionModel;

        Provider = provider;
        IsEditingProvider = false;
        TranscriptionModel = provider == TranscriptionProvider.OpenAI ? _openAiModelName : _localModelName;
        ScheduleSave();

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
        ScheduleSave();
    }

    [RelayCommand]
    private void StartEditingApiKey() => IsEditingApiKey = true;

    public void ApplyApiKey(string key)
    {
        OpenAiApiKey = key;
        IsEditingApiKey = false;
        UpdateDisplayTexts();
        ScheduleSave();
    }

    [RelayCommand]
    private void StartEditingModel() => IsEditingModel = true;

    public void ApplyModel(string model)
    {
        TranscriptionModel = model;
        if (Provider == TranscriptionProvider.OpenAI) _openAiModelName = model;
        else if (Provider == TranscriptionProvider.Local) _localModelName = model;
        IsEditingModel = false;
        ScheduleSave();
    }

    [RelayCommand]
    private void ToggleGpuAcceleration()
    {
        GpuAcceleration = !GpuAcceleration;
        ScheduleSave();
    }

    // --- Text Correction ---

    [RelayCommand]
    private void SelectCorrectionProvider(string providerName)
    {
        if (Enum.TryParse<TextCorrectionProvider>(providerName, out var provider))
        {
            CorrectionProvider = provider;
            _logger.LogInformation("Text correction provider changed to: {Provider}", provider);
            ScheduleSave();

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
        ScheduleSave();
    }

    [RelayCommand]
    private void ToggleCorrectionGpuAcceleration()
    {
        CorrectionGpuAcceleration = !CorrectionGpuAcceleration;
        _logger.LogInformation("Correction GPU acceleration: {Enabled}", CorrectionGpuAcceleration);
        ScheduleSave();
    }

    [RelayCommand]
    private void ToggleCombinedAudioModel() => ScheduleSave();

    [RelayCommand]
    private void StartEditingCombinedAudioModel() => IsEditingCombinedAudioModel = true;

    public void ApplyCombinedAudioModel(string model)
    {
        CombinedAudioModel = model;
        IsEditingCombinedAudioModel = false;
        ScheduleSave();
    }

    // --- Correction Models ---

    [ObservableProperty] private string _correctionLocalModelName = "";

    [RelayCommand]
    private void RefreshCorrectionModels()
    {
        CorrectionModelItems.Clear();
        foreach (var model in _correctionModelManager.GetAllModels())
        {
            var item = new CorrectionModelItemViewModel(model);
            item.IsActive = model.FileName == CorrectionLocalModelName && model.IsDownloaded;
            if (item.IsActive) item.StatusText = "Active";
            CorrectionModelItems.Add(item);
        }
    }

    [RelayCommand]
    private async Task DownloadCorrectionModel(CorrectionModelItemViewModel item)
    {
        if (item.IsDownloading) return;

        item.IsDownloading = true;
        item.StatusText = "Downloading...";
        item.DownloadProgress = 0;

        try
        {
            var progress = new Progress<float>(p =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    item.DownloadProgress = p;
                    item.StatusText = $"Downloading... {p * 100:F0}%";
                });
            });

            await _correctionModelManager.DownloadModelAsync(item.FileName, progress);

            item.IsDownloaded = true;
            item.StatusText = "Downloaded";
            _logger.LogInformation("Correction model {Name} downloaded successfully", item.Name);

            // Auto-activate if no model is currently active
            if (!CorrectionModelItems.Any(m => m.IsActive))
                ActivateCorrectionModel(item);
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
    private void ActivateCorrectionModel(CorrectionModelItemViewModel item)
    {
        if (!item.IsDownloaded) return;
        foreach (var m in CorrectionModelItems)
        {
            m.IsActive = false;
            if (m.IsDownloaded) m.StatusText = "Downloaded";
        }
        item.IsActive = true;
        item.StatusText = "Active";
        CorrectionLocalModelName = item.FileName;
        ScheduleSave();

        _preloadService.PreloadCorrectionModel(item.FileName);
    }

    [RelayCommand]
    private void DeleteCorrectionModel(CorrectionModelItemViewModel item)
    {
        try
        {
            var model = _correctionModelManager.GetAllModels().FirstOrDefault(m => m.FileName == item.FileName);
            if (model is not null)
            {
                _correctionModelManager.DeleteModel(model);
                item.IsDownloaded = false;
                item.IsActive = false;
                item.StatusText = "Not downloaded";
                _logger.LogInformation("Correction model {Name} deleted", item.Name);
            }
        }
        catch (Exception ex)
        {
            item.StatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to delete correction model {Name}", item.Name);
        }
    }

    // --- Dictionary ---

    private void LoadDictionaryEntries()
    {
        DictionaryEntries.Clear();
        foreach (var entry in _dictionaryService.GetEntries())
            DictionaryEntries.Add(entry);
    }

    [RelayCommand]
    private void AddDictionaryEntry()
    {
        if (string.IsNullOrWhiteSpace(NewDictionaryWord)) return;
        var word = NewDictionaryWord.Trim();
        _dictionaryService.AddEntry(word);
        if (!DictionaryEntries.Contains(word, StringComparer.OrdinalIgnoreCase))
            DictionaryEntries.Add(word);
        NewDictionaryWord = "";
    }

    [RelayCommand]
    private void RemoveDictionaryEntry(string word)
    {
        _dictionaryService.RemoveEntry(word);
        DictionaryEntries.Remove(word);
    }

    // --- Snippets ---

    private void LoadSnippets()
    {
        SnippetItems.Clear();
        foreach (var entry in _snippetService.GetSnippets())
            SnippetItems.Add(entry);
    }

    [RelayCommand]
    private void AddSnippet()
    {
        if (string.IsNullOrWhiteSpace(NewSnippetTrigger) || string.IsNullOrWhiteSpace(NewSnippetReplacement)) return;
        var trigger = NewSnippetTrigger.Trim();
        var replacement = NewSnippetReplacement.Trim();
        _snippetService.AddSnippet(trigger, replacement);
        if (!SnippetItems.Any(s => s.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase)))
            SnippetItems.Add(new SnippetEntry(trigger, replacement));
        NewSnippetTrigger = "";
        NewSnippetReplacement = "";
    }

    [RelayCommand]
    private void RemoveSnippet(SnippetEntry snippet)
    {
        _snippetService.RemoveSnippet(snippet.Trigger);
        SnippetItems.Remove(snippet);
    }

    // --- Statistics ---

    [RelayCommand]
    private void RefreshStats()
    {
        var stats = _statsService.GetStats();
        TotalTranscriptions = stats.TotalTranscriptions;
        ErrorCount = stats.ErrorCount;
        TotalRecordingTimeDisplay = FormatDuration(stats.TotalRecordingSeconds);
        AverageDurationDisplay = $"{stats.AverageRecordingSeconds:F1}s";
        EstimatedCostDisplay = $"${stats.EstimatedApiCost:F4}";

        if (stats.TranscriptionsByProvider.Count > 0)
        {
            ProviderBreakdownDisplay = string.Join(", ",
                stats.TranscriptionsByProvider.Select(kv => $"{kv.Key}: {kv.Value}"));
        }
        else
        {
            ProviderBreakdownDisplay = "No data yet";
        }
    }

    [RelayCommand]
    private void ResetStats()
    {
        _statsService.Reset();
        RefreshStats();
    }

    private static string FormatDuration(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
            : $"{ts.Minutes}m {ts.Seconds}s";
    }

    // --- Models ---

    [RelayCommand]
    private void RefreshModels()
    {
        ModelItems.Clear();
        foreach (var model in _modelManager.GetAllModels())
        {
            var ggmlType = FileNameToGgmlType(model.FileName);
            var item = new ModelItemViewModel(model, ggmlType);
            item.IsActive = model.FileName == TranscriptionModel && model.IsDownloaded;
            ModelItems.Add(item);
        }
    }

    [RelayCommand]
    private async Task DownloadModel(ModelItemViewModel item)
    {
        if (item.IsDownloading) return;

        item.IsDownloading = true;
        item.StatusText = "Downloading...";
        item.DownloadProgress = 0;

        try
        {
            var progress = new Progress<float>(p =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    item.DownloadProgress = p;
                    item.StatusText = $"Downloading... {p * 100:F0}%";
                });
            });

            await _modelManager.DownloadModelAsync(item.GgmlType, progress);

            item.IsDownloaded = true;
            item.StatusText = "Downloaded";
            _logger.LogInformation("Model {Name} downloaded successfully", item.Name);

            // Auto-activate if no model is currently active
            if (!ModelItems.Any(m => m.IsActive))
                ActivateModel(item);
        }
        catch (Exception ex)
        {
            item.StatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to download model {Name}", item.Name);
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    [RelayCommand]
    private void ActivateModel(ModelItemViewModel item)
    {
        if (!item.IsDownloaded) return;
        foreach (var m in ModelItems)
        {
            m.IsActive = false;
            if (m.IsDownloaded) m.StatusText = "Downloaded";
        }
        item.IsActive = true;
        item.StatusText = "Active";
        TranscriptionModel = item.FileName;
        _localModelName = item.FileName;
        ScheduleSave();

        _preloadService.PreloadTranscriptionModel(item.FileName);
    }

    [RelayCommand]
    private void DeleteModel(ModelItemViewModel item)
    {
        try
        {
            var model = _modelManager.GetAllModels().FirstOrDefault(m => m.FileName == item.FileName);
            if (model is not null)
            {
                _modelManager.DeleteModel(model);
                item.IsDownloaded = false;
                item.StatusText = "Not downloaded";
                _logger.LogInformation("Model {Name} deleted", item.Name);
            }
        }
        catch (Exception ex)
        {
            item.StatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to delete model {Name}", item.Name);
        }
    }

    private static GgmlType FileNameToGgmlType(string fileName) => fileName switch
    {
        "ggml-tiny.bin" => GgmlType.Tiny,
        "ggml-base.bin" => GgmlType.Base,
        "ggml-small.bin" => GgmlType.Small,
        "ggml-medium.bin" => GgmlType.Medium,
        "ggml-large-v3.bin" => GgmlType.LargeV3,
        _ => throw new ArgumentException($"Unknown model: {fileName}")
    };

    // --- Persistence ---

    private void ScheduleSave()
    {
        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                await SaveSettingsAsync();
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings");
            }
        }, token);
    }

    private async Task SaveSettingsAsync()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        var json = await File.ReadAllTextAsync(path);
        var doc = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip
        })!;

        var section = doc["WhisperShow"]!;

        section["Provider"] = Provider.ToString();
        section["OpenAI"]!["ApiKey"] = OpenAiApiKey;
        section["OpenAI"]!["Model"] = _openAiModelName;
        section["OpenAI"]!["Endpoint"] = string.IsNullOrWhiteSpace(OpenAiEndpoint) ? null : OpenAiEndpoint;
        section["Local"]!["ModelName"] = _localModelName;
        section["Local"]!["GpuAcceleration"] = GpuAcceleration;
        section["Language"] = SelectedLanguageCode;
        section["Hotkey"]!["Toggle"]!["Modifiers"] = ToggleModifiers;
        section["Hotkey"]!["Toggle"]!["Key"] = ToggleKey;
        section["Hotkey"]!["PushToTalk"]!["Modifiers"] = PttModifiers;
        section["Hotkey"]!["PushToTalk"]!["Key"] = PttKey;
        section["App"]!["LaunchAtLogin"] = LaunchAtLogin;
        section["App"]!["SoundEffects"] = SoundEffectsEnabled;
        section["App"]!["Theme"] = IsDarkMode ? "Dark" : "Light";
        section["Audio"]!["DeviceIndex"] = SelectedMicrophoneIndex;
        section["Audio"]!["MaxRecordingSeconds"] = MaxRecordingSeconds;
        section["Audio"]!["CompressBeforeUpload"] = AudioCompressionEnabled;
        section["Audio"]!["MuteWhileDictating"] = MuteWhileDictating;
        section["Overlay"]!["AutoDismissSeconds"] = AutoDismissSeconds;
        section["Overlay"]!["AlwaysVisible"] = OverlayAlwaysVisible;
        section["Overlay"]!["ShowInTaskbar"] = ShowInTaskbar;
        section["TextCorrection"]!["Provider"] = CorrectionProvider.ToString();
        section["TextCorrection"]!["Model"] = CorrectionCloudModel;
        section["TextCorrection"]!["LocalModelName"] = CorrectionLocalModelName;
        section["TextCorrection"]!["LocalGpuAcceleration"] = CorrectionGpuAcceleration;
        section["TextCorrection"]!["UseCombinedAudioModel"] = UseCombinedAudioModel;
        section["TextCorrection"]!["CombinedAudioModel"] = CombinedAudioModel;

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(path, doc.ToJsonString(options));

        _logger.LogInformation("Settings saved to appsettings.json");
    }
}
