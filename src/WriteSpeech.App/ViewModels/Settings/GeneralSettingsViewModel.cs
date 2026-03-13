using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Hotkey;
using WriteSpeech.Core.Services.ModelManagement;

namespace WriteSpeech.App.ViewModels.Settings;

/// <summary>Represents an audio input device with its NAudio device index and display name.</summary>
/// <param name="DeviceIndex">The zero-based NAudio device index used to open the microphone for recording.</param>
/// <param name="Name">The human-readable product name of the audio input device.</param>
public record MicrophoneInfo(int DeviceIndex, string Name);

/// <summary>Represents a language option for speech-to-text transcription.</summary>
/// <param name="Code">The ISO language code (e.g., "en", "de") sent to the transcription provider.</param>
/// <param name="DisplayName">The localized display name shown in the language picker UI.</param>
/// <param name="Flag">The filename of the flag image resource (e.g., "en.png") used as a visual indicator.</param>
public record LanguageInfo(string Code, string DisplayName, string Flag);

/// <summary>Identifies which settings dialog is currently open in the general settings page.</summary>
public enum SettingsDialogType
{
    /// <summary>No dialog is open.</summary>
    None,
    /// <summary>The hotkey configuration dialog is open.</summary>
    Hotkey,
    /// <summary>The microphone selection dialog is open.</summary>
    Microphone,
    /// <summary>The language picker dialog is open.</summary>
    Language
}

/// <summary>Identifies which hotkey binding is currently being captured from user input.</summary>
public enum HotkeyCaptureTarget
{
    /// <summary>No hotkey capture is in progress.</summary>
    None,
    /// <summary>Capturing a new toggle (start/stop recording) hotkey binding.</summary>
    Toggle,
    /// <summary>Capturing a new push-to-talk hotkey binding.</summary>
    PushToTalk
}

/// <summary>
/// ViewModel for the General settings page in the speech-to-text overlay application.
/// Manages microphone selection, transcription language, hotkey configuration (toggle and push-to-talk),
/// hotkey method switching (RegisterHotKey vs. LowLevelHook), and voice activity detection (VAD) settings.
/// Changes are persisted to appsettings.json via a debounced save callback.
/// </summary>
public partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly ILogger _logger;
    private readonly IDispatcherService _dispatcher;
    private readonly Action _scheduleSave;
    private readonly IVadModelManager _vadModelManager;

    // --- Dialog system ---
    [ObservableProperty] private bool _isDialogOpen;
    [ObservableProperty] private SettingsDialogType _activeDialog;

    // --- Hotkey method ---
    [ObservableProperty] private string _hotkeyMethod = "RegisterHotKey";
    [ObservableProperty] private bool _isLowLevelHookMode;

    // --- Toggle hotkey ---
    [ObservableProperty] private string _toggleModifiers = "Control, Shift";
    [ObservableProperty] private string _toggleKey = "Space";
    [ObservableProperty] private string? _toggleMouseButton;
    [ObservableProperty] private string _toggleDisplayText = "";
    /// <summary>Individual modifier and key badge strings displayed in the toggle hotkey UI (e.g., "Ctrl", "Shift", "Space").</summary>
    public ObservableCollection<string> ToggleBadges { get; } = [];

    // --- Push-to-Talk hotkey ---
    [ObservableProperty] private string _pttModifiers = "Control";
    [ObservableProperty] private string _pttKey = "Space";
    [ObservableProperty] private string? _pttMouseButton;
    [ObservableProperty] private string _pttDisplayText = "";
    /// <summary>Individual modifier and key badge strings displayed in the push-to-talk hotkey UI.</summary>
    public ObservableCollection<string> PttBadges { get; } = [];

    // --- Hotkey capture state ---
    [ObservableProperty] private HotkeyCaptureTarget _capturingHotkey;
    [ObservableProperty] private string _hotkeyDisplayText = "";

    // --- Microphone ---
    [ObservableProperty] private int _selectedMicrophoneIndex;
    [ObservableProperty] private string _selectedMicrophoneDisplay = "";
    /// <summary>List of audio input devices available on the system, populated via NAudio enumeration.</summary>
    public ObservableCollection<MicrophoneInfo> AvailableMicrophones { get; } = [];

    // --- Mic test ---
    [ObservableProperty] private bool _isMicTesting;
    [ObservableProperty] private float _micTestLevel;
    private MicTestHelper? _micTestHelper;

    // --- Language ---
    [ObservableProperty] private string? _selectedLanguageCode;
    [ObservableProperty] private string _selectedLanguageDisplay = "";
    [ObservableProperty] private bool _isAutoDetectLanguage;
    [ObservableProperty] private string? _pendingLanguageCode;

    // --- Voice Activity Detection ---
    [ObservableProperty] private bool _vadEnabled;
    [ObservableProperty] private float _vadSilenceDuration = 1.5f;
    [ObservableProperty] private float _vadSensitivity = 0.5f;
    [ObservableProperty] private bool _isVadModelDownloading;
    [ObservableProperty] private string _vadDownloadStatus = "";

    /// <summary>All languages supported by the transcription engine, each with a code, display name, and flag icon.</summary>
    public ObservableCollection<LanguageInfo> AvailableLanguages { get; } =
        new(SupportedLanguages.All.Select(l => new LanguageInfo(l.Code, l.Name, l.Flag)));

    public GeneralSettingsViewModel(
        IGlobalHotkeyService hotkeyService,
        ILogger logger,
        IDispatcherService dispatcher,
        Action scheduleSave,
        WriteSpeechOptions options,
        IVadModelManager vadModelManager)
    {
        _hotkeyService = hotkeyService;
        _logger = logger;
        _dispatcher = dispatcher;
        _scheduleSave = scheduleSave;
        _vadModelManager = vadModelManager;

        _hotkeyMethod = options.Hotkey.Method;
        _isLowLevelHookMode = options.Hotkey.Method == "LowLevelHook";
        _toggleModifiers = options.Hotkey.Toggle.Modifiers;
        _toggleKey = options.Hotkey.Toggle.Key;
        _toggleMouseButton = options.Hotkey.Toggle.MouseButton;
        _pttModifiers = options.Hotkey.PushToTalk.Modifiers;
        _pttKey = options.Hotkey.PushToTalk.Key;
        _pttMouseButton = options.Hotkey.PushToTalk.MouseButton;
        _selectedMicrophoneIndex = options.Audio.DeviceIndex;
        _selectedLanguageCode = options.Language;
        _isAutoDetectLanguage = options.Language == null;
        _vadEnabled = options.Audio.VoiceActivity.Enabled;
        _vadSilenceDuration = options.Audio.VoiceActivity.SilenceDurationSeconds;
        _vadSensitivity = options.Audio.VoiceActivity.Threshold;

        LoadMicrophones();
        UpdateDisplayTexts();
        UpdateToggleBadges();
        UpdatePttBadges();
    }

    private void LoadMicrophones()
    {
        AvailableMicrophones.Clear();
        foreach (var mic in EnumerateMicrophones())
            AvailableMicrophones.Add(mic);
    }

    /// <summary>
    /// Enumerates available audio input devices via NAudio.
    /// Returns a fallback entry if no devices are found.
    /// </summary>
    internal static List<MicrophoneInfo> EnumerateMicrophones()
    {
        var result = new List<MicrophoneInfo>();
        var deviceCount = WaveInEvent.DeviceCount;
        for (int i = 0; i < deviceCount; i++)
        {
            try
            {
                var caps = WaveInEvent.GetCapabilities(i);
                result.Add(new MicrophoneInfo(i, caps.ProductName));
            }
            catch
            {
                // Skip devices that fail to report capabilities
            }
        }

        if (result.Count == 0)
            result.Add(new MicrophoneInfo(0, "No devices found"));

        return result;
    }

    /// <summary>Converts an internal mouse button identifier (e.g., "XButton1") to a user-friendly display string (e.g., "Mouse 4").</summary>
    internal static string FormatMouseButton(string? mouseButton) => mouseButton switch
    {
        "XButton1" => "Mouse 4",
        "XButton2" => "Mouse 5",
        "Middle" => "Middle Click",
        _ => mouseButton ?? ""
    };

    /// <summary>
    /// Formats a hotkey binding into a human-readable display string (e.g., "Ctrl + Shift + Space" or "Ctrl + Mouse 4").
    /// Used for hotkey labels and tooltip text throughout the settings UI.
    /// </summary>
    internal static string FormatKeys(string modifiers, string key, string? mouseButton = null)
    {
        var trigger = !string.IsNullOrEmpty(mouseButton) ? FormatMouseButton(mouseButton) : key;
        var mods = modifiers.Replace(",", " +").Replace("Control", "Ctrl");
        return string.IsNullOrEmpty(mods) ? trigger : $"{mods} + {trigger}";
    }

    private void UpdateDisplayTexts()
    {
        var toggleKeys = FormatKeys(ToggleModifiers, ToggleKey, ToggleMouseButton);
        var pttKeys = FormatKeys(PttModifiers, PttKey, PttMouseButton);
        ToggleDisplayText = $"Press {toggleKeys} to start and stop.";
        PttDisplayText = $"Hold {pttKeys} and speak.";
        HotkeyDisplayText = $"Toggle: {toggleKeys} · PTT: {pttKeys}";

        var mic = AvailableMicrophones.FirstOrDefault(m => m.DeviceIndex == SelectedMicrophoneIndex);
        SelectedMicrophoneDisplay = mic is not null
            ? (SelectedMicrophoneIndex == 0 ? $"Auto-detect ({mic.Name})" : mic.Name)
            : "Auto-detect";

        if (IsAutoDetectLanguage || SelectedLanguageCode == null)
        {
            SelectedLanguageDisplay = "Auto-detect";
        }
        else
        {
            var lang = AvailableLanguages.FirstOrDefault(l => l.Code == SelectedLanguageCode);
            SelectedLanguageDisplay = lang?.DisplayName ?? "Auto-detect";
        }
    }

    private static void UpdateBadges(ObservableCollection<string> badges, string modifiers, string key, string? mouseButton = null)
    {
        badges.Clear();
        if (!string.IsNullOrEmpty(modifiers))
        {
            foreach (var mod in modifiers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                badges.Add(mod == "Control" ? "Ctrl" : mod);
            }
        }
        if (!string.IsNullOrEmpty(mouseButton))
            badges.Add(FormatMouseButton(mouseButton));
        else if (!string.IsNullOrEmpty(key))
            badges.Add(key);
    }

    private void UpdateToggleBadges() => UpdateBadges(ToggleBadges, ToggleModifiers, ToggleKey, ToggleMouseButton);
    private void UpdatePttBadges() => UpdateBadges(PttBadges, PttModifiers, PttKey, PttMouseButton);

    // --- Dialog system ---

    [RelayCommand]
    private void OpenHotkeyDialog()
    {
        ActiveDialog = SettingsDialogType.Hotkey;
        IsDialogOpen = true;
        CapturingHotkey = HotkeyCaptureTarget.None;
    }

    [RelayCommand]
    private void OpenMicrophoneDialog()
    {
        ActiveDialog = SettingsDialogType.Microphone;
        IsDialogOpen = true;
        StartMicTestInternal();
    }

    [RelayCommand]
    private void OpenLanguageDialog()
    {
        PendingLanguageCode = IsAutoDetectLanguage ? null : SelectedLanguageCode;
        ActiveDialog = SettingsDialogType.Language;
        IsDialogOpen = true;
    }

    [RelayCommand]
    private void CloseDialog()
    {
        if (ActiveDialog == SettingsDialogType.Microphone && IsMicTesting)
            StopMicTestInternal();

        IsDialogOpen = false;
        ActiveDialog = SettingsDialogType.None;
        CapturingHotkey = HotkeyCaptureTarget.None;
        _hotkeyService.SuppressActions = false;
    }

    // --- Hotkey dialog ---

    [RelayCommand]
    private void StartCapturingToggleHotkey()
    {
        CapturingHotkey = HotkeyCaptureTarget.Toggle;
        _hotkeyService.SuppressActions = true;
    }

    [RelayCommand]
    private void StartCapturingPttHotkey()
    {
        CapturingHotkey = HotkeyCaptureTarget.PushToTalk;
        _hotkeyService.SuppressActions = true;
    }

    /// <summary>Applies a captured keyboard-only hotkey binding to the currently active capture target (toggle or push-to-talk).</summary>
    public void ApplyNewHotkey(string modifiers, string key)
        => ApplyNewHotkey(modifiers, key, null);

    /// <summary>
    /// Applies a captured hotkey binding (keyboard or mouse) to the currently active capture target.
    /// Updates the hotkey service registration, refreshes display text and badges, and triggers a settings save.
    /// </summary>
    public void ApplyNewHotkey(string modifiers, string? key, string? mouseButton)
    {
        if (CapturingHotkey == HotkeyCaptureTarget.Toggle)
        {
            ToggleModifiers = modifiers;
            ToggleKey = key ?? "";
            ToggleMouseButton = mouseButton;
            UpdateToggleBadges();
            _hotkeyService.UpdateToggleHotkey(modifiers, key, mouseButton);
        }
        else if (CapturingHotkey == HotkeyCaptureTarget.PushToTalk)
        {
            PttModifiers = modifiers;
            PttKey = key ?? "";
            PttMouseButton = mouseButton;
            UpdatePttBadges();
            _hotkeyService.UpdatePushToTalkHotkey(modifiers, key, mouseButton);
        }

        CapturingHotkey = HotkeyCaptureTarget.None;
        _hotkeyService.SuppressActions = false;
        UpdateDisplayTexts();
        _scheduleSave();
    }

    [RelayCommand]
    private void ResetHotkeyToDefault()
    {
        ToggleModifiers = "Control, Shift";
        ToggleKey = "Space";
        ToggleMouseButton = null;
        PttModifiers = "Control";
        PttKey = "Space";
        PttMouseButton = null;
        CapturingHotkey = HotkeyCaptureTarget.None;
        UpdateToggleBadges();
        UpdatePttBadges();
        UpdateDisplayTexts();
        _hotkeyService.UpdateToggleHotkey("Control, Shift", "Space", null);
        _hotkeyService.UpdatePushToTalkHotkey("Control", "Space", null);
        _scheduleSave();
    }

    // --- Microphone dialog ---

    /// <summary>Selects a microphone by its NAudio device index, updates the display text, triggers a save, and restarts the mic level test.</summary>
    public void SelectMicrophone(int deviceIndex)
    {
        SelectedMicrophoneIndex = deviceIndex;
        UpdateDisplayTexts();
        _scheduleSave();

        StopMicTestInternal();
        StartMicTestInternal();
    }

    /// <summary>Stops the microphone level test if one is currently running.</summary>
    public void StopMicTest()
    {
        if (!IsMicTesting) return;
        StopMicTestInternal();
    }

    private void StartMicTestInternal()
    {
        _micTestHelper ??= new MicTestHelper(_dispatcher, _logger, level =>
        {
            MicTestLevel = level;
            IsMicTesting = _micTestHelper?.IsTesting ?? false;
        });
        _micTestHelper.Start(SelectedMicrophoneIndex);
        IsMicTesting = _micTestHelper.IsTesting;
    }

    private void StopMicTestInternal()
    {
        _micTestHelper?.Stop();
        IsMicTesting = false;
        MicTestLevel = 0;
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
        _scheduleSave();
    }

    // --- Voice Activity Detection ---

    partial void OnVadEnabledChanged(bool value)
    {
        _scheduleSave();
        if (value && !_vadModelManager.IsModelDownloaded)
            _ = DownloadVadModelAsync();
    }

    private async Task DownloadVadModelAsync()
    {
        IsVadModelDownloading = true;
        VadDownloadStatus = "Downloading VAD model...";
        try
        {
            var progress = new Progress<float>(p =>
            {
                _dispatcher.Invoke(() =>
                    VadDownloadStatus = $"Downloading VAD model... {p * 100:F0}%");
            });
            await _vadModelManager.DownloadModelAsync(progress);
            VadDownloadStatus = "";
            _logger.LogInformation("VAD model auto-downloaded on enable");
        }
        catch (Exception ex)
        {
            VadDownloadStatus = $"Download failed: {ex.Message}";
            _logger.LogError(ex, "Failed to auto-download VAD model");
        }
        finally
        {
            IsVadModelDownloading = false;
        }
    }

    partial void OnVadSilenceDurationChanged(float value) => _scheduleSave();
    partial void OnVadSensitivityChanged(float value) => _scheduleSave();

    // --- Persistence ---

    [RelayCommand]
    private void SetHotkeyMethod(string method)
    {
        // When switching to RegisterHotKey, clear mouse button bindings
        // because the validator rejects MouseButton + RegisterHotKey.
        if (method == "RegisterHotKey"
            && (ToggleMouseButton is not null || PttMouseButton is not null))
        {
            ResetHotkeyToDefault();
        }

        HotkeyMethod = method;
        IsLowLevelHookMode = method == "LowLevelHook";
        _hotkeyService.SwitchMethod(method);
        _scheduleSave();
    }

    /// <summary>Writes the general settings (language, hotkey bindings, microphone, VAD) into the given JSON configuration node for persistence.</summary>
    public void WriteSettings(JsonNode section)
    {
        section["Language"] = SelectedLanguageCode;

        var hotkey = SettingsViewModel.EnsureObject(section, "Hotkey");
        hotkey["Method"] = HotkeyMethod;
        var toggle = SettingsViewModel.EnsureObject(hotkey, "Toggle");
        toggle["Modifiers"] = ToggleModifiers;
        toggle["Key"] = ToggleKey;
        toggle["MouseButton"] = ToggleMouseButton;
        var ptt = SettingsViewModel.EnsureObject(hotkey, "PushToTalk");
        ptt["Modifiers"] = PttModifiers;
        ptt["Key"] = PttKey;
        ptt["MouseButton"] = PttMouseButton;

        var audio = SettingsViewModel.EnsureObject(section, "Audio");
        audio["DeviceIndex"] = SelectedMicrophoneIndex;

        var vad = SettingsViewModel.EnsureObject(audio, "VoiceActivity");
        vad["Enabled"] = VadEnabled;
        vad["SilenceDurationSeconds"] = VadSilenceDuration;
        vad["Threshold"] = VadSensitivity;
    }
}
