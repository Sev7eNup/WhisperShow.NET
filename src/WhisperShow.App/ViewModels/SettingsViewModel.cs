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
using WhisperShow.Core.Services.Hotkey;

namespace WhisperShow.App.ViewModels;

public enum SettingsPage
{
    General,
    System,
    Transcription
}

public record MicrophoneInfo(int DeviceIndex, string Name);
public record LanguageInfo(string Code, string DisplayName, string Flag);

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IGlobalHotkeyService _hotkeyService;
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

    // --- System: Sound ---
    [ObservableProperty] private bool _soundEffectsEnabled = true;
    [ObservableProperty] private bool _muteWhileDictating = true;

    // --- System: Text Correction ---
    [ObservableProperty] private bool _textCorrectionEnabled;

    // --- System: Audio Compression ---
    [ObservableProperty] private bool _audioCompressionEnabled = true;

    // --- System: Combined Audio Model ---
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
    [ObservableProperty] private TranscriptionProvider _provider = TranscriptionProvider.OpenAI;
    [ObservableProperty] private bool _isEditingProvider;

    // --- Transcription: API Key ---
    [ObservableProperty] private string _openAiApiKey = "";
    [ObservableProperty] private bool _isEditingApiKey;
    [ObservableProperty] private string _openAiApiKeyDisplay = "";

    // --- Transcription: Model ---
    [ObservableProperty] private string _transcriptionModel = "whisper-1";
    [ObservableProperty] private bool _isEditingModel;

    // --- Transcription: GPU ---
    [ObservableProperty] private bool _gpuAcceleration = true;

    // Version
    public string VersionText => $"WhisperShow v{GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

    public SettingsViewModel(
        IOptions<WhisperShowOptions> options,
        IGlobalHotkeyService hotkeyService,
        ILogger<SettingsViewModel> logger)
    {
        _logger = logger;
        _hotkeyService = hotkeyService;

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

        // System: Sound
        _soundEffectsEnabled = opts.App.SoundEffects;
        _muteWhileDictating = opts.Audio.MuteWhileDictating;

        // System: Transcription
        _textCorrectionEnabled = opts.TextCorrection.Enabled;
        _audioCompressionEnabled = opts.Audio.CompressBeforeUpload;
        _useCombinedAudioModel = opts.TextCorrection.UseCombinedAudioModel;
        _combinedAudioModel = opts.TextCorrection.CombinedAudioModel;
        _autoDismissSeconds = opts.Overlay.AutoDismissSeconds;
        _maxRecordingSeconds = opts.Audio.MaxRecordingSeconds;

        // Transcription
        _provider = opts.Provider;
        _openAiApiKey = opts.OpenAI.ApiKey ?? "";
        _transcriptionModel = opts.Provider == TranscriptionProvider.OpenAI
            ? opts.OpenAI.Model
            : opts.Local.ModelName;
        _gpuAcceleration = opts.Local.GpuAcceleration;

        LoadMicrophones();
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
    private void Navigate(SettingsPage page) => SelectedPage = page;

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

    // --- System: Transcription ---

    [RelayCommand]
    private void ToggleTextCorrection() => ScheduleSave();

    [RelayCommand]
    private void ToggleAudioCompression() => ScheduleSave();

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
        Provider = provider;
        IsEditingProvider = false;
        TranscriptionModel = provider == TranscriptionProvider.OpenAI ? "whisper-1" : "ggml-small.bin";
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
        IsEditingModel = false;
        ScheduleSave();
    }

    [RelayCommand]
    private void ToggleGpuAcceleration()
    {
        GpuAcceleration = !GpuAcceleration;
        ScheduleSave();
    }

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
        section["OpenAI"]!["Model"] = Provider == TranscriptionProvider.OpenAI ? TranscriptionModel : "whisper-1";
        section["Local"]!["ModelName"] = Provider == TranscriptionProvider.Local ? TranscriptionModel : "ggml-small.bin";
        section["Local"]!["GpuAcceleration"] = GpuAcceleration;
        section["Language"] = SelectedLanguageCode;
        section["Hotkey"]!["Toggle"]!["Modifiers"] = ToggleModifiers;
        section["Hotkey"]!["Toggle"]!["Key"] = ToggleKey;
        section["Hotkey"]!["PushToTalk"]!["Modifiers"] = PttModifiers;
        section["Hotkey"]!["PushToTalk"]!["Key"] = PttKey;
        section["App"]!["LaunchAtLogin"] = LaunchAtLogin;
        section["App"]!["SoundEffects"] = SoundEffectsEnabled;
        section["Audio"]!["DeviceIndex"] = SelectedMicrophoneIndex;
        section["Audio"]!["MaxRecordingSeconds"] = MaxRecordingSeconds;
        section["Audio"]!["CompressBeforeUpload"] = AudioCompressionEnabled;
        section["Audio"]!["MuteWhileDictating"] = MuteWhileDictating;
        section["Overlay"]!["AutoDismissSeconds"] = AutoDismissSeconds;
        section["Overlay"]!["AlwaysVisible"] = OverlayAlwaysVisible;
        section["Overlay"]!["ShowInTaskbar"] = ShowInTaskbar;
        section["TextCorrection"]!["Enabled"] = TextCorrectionEnabled;
        section["TextCorrection"]!["UseCombinedAudioModel"] = UseCombinedAudioModel;
        section["TextCorrection"]!["CombinedAudioModel"] = CombinedAudioModel;

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(path, doc.ToJsonString(options));

        _logger.LogInformation("Settings saved to appsettings.json");
    }
}
