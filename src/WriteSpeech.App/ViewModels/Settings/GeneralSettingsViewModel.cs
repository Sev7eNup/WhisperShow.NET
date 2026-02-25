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

namespace WriteSpeech.App.ViewModels.Settings;

public record MicrophoneInfo(int DeviceIndex, string Name);
public record LanguageInfo(string Code, string DisplayName, string Flag);

public enum SettingsDialogType { None, Hotkey, Microphone, Language }
public enum HotkeyCaptureTarget { None, Toggle, PushToTalk }

public partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly ILogger _logger;
    private readonly IDispatcherService _dispatcher;
    private readonly Action _scheduleSave;

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
    public ObservableCollection<string> ToggleBadges { get; } = [];

    // --- Push-to-Talk hotkey ---
    [ObservableProperty] private string _pttModifiers = "Control";
    [ObservableProperty] private string _pttKey = "Space";
    [ObservableProperty] private string? _pttMouseButton;
    [ObservableProperty] private string _pttDisplayText = "";
    public ObservableCollection<string> PttBadges { get; } = [];

    // --- Hotkey capture state ---
    [ObservableProperty] private HotkeyCaptureTarget _capturingHotkey;
    [ObservableProperty] private string _hotkeyDisplayText = "";

    // --- Microphone ---
    [ObservableProperty] private int _selectedMicrophoneIndex;
    [ObservableProperty] private string _selectedMicrophoneDisplay = "";
    public ObservableCollection<MicrophoneInfo> AvailableMicrophones { get; } = [];

    // --- Mic test ---
    [ObservableProperty] private bool _isMicTesting;
    [ObservableProperty] private float _micTestLevel;
    private WaveInEvent? _micTestWaveIn;

    // --- Language ---
    [ObservableProperty] private string? _selectedLanguageCode;
    [ObservableProperty] private string _selectedLanguageDisplay = "";
    [ObservableProperty] private bool _isAutoDetectLanguage;
    [ObservableProperty] private string? _pendingLanguageCode;

    public ObservableCollection<LanguageInfo> AvailableLanguages { get; } =
        new(SupportedLanguages.All.Select(l => new LanguageInfo(l.Code, l.Name, l.Flag)));

    public GeneralSettingsViewModel(
        IGlobalHotkeyService hotkeyService,
        ILogger logger,
        IDispatcherService dispatcher,
        Action scheduleSave,
        WriteSpeechOptions options)
    {
        _hotkeyService = hotkeyService;
        _logger = logger;
        _dispatcher = dispatcher;
        _scheduleSave = scheduleSave;

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

    internal static string FormatMouseButton(string? mouseButton) => mouseButton switch
    {
        "XButton1" => "Mouse 4",
        "XButton2" => "Mouse 5",
        "Middle" => "Middle Click",
        _ => mouseButton ?? ""
    };

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

    public void ApplyNewHotkey(string modifiers, string key)
        => ApplyNewHotkey(modifiers, key, null);

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

    public void SelectMicrophone(int deviceIndex)
    {
        SelectedMicrophoneIndex = deviceIndex;
        UpdateDisplayTexts();
        CloseDialog();
        _scheduleSave();

        if (IsMicTesting)
        {
            StopMicTestInternal();
            StartMicTestInternal();
        }
    }

    [RelayCommand]
    private void ToggleMicTest()
    {
        if (IsMicTesting)
            StopMicTest();
        else
            StartMicTestInternal();
    }

    public void StopMicTest()
    {
        if (!IsMicTesting) return;
        StopMicTestInternal();
    }

    private void StartMicTestInternal()
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
            StopMicTestInternal();
        }
    }

    private void StopMicTestInternal()
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

        SettingsViewModel.EnsureObject(section, "Audio")["DeviceIndex"] = SelectedMicrophoneIndex;
    }
}
