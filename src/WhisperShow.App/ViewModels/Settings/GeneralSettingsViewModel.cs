using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Services;
using WhisperShow.Core.Services.Hotkey;

namespace WhisperShow.App.ViewModels.Settings;

public record MicrophoneInfo(int DeviceIndex, string Name);
public record LanguageInfo(string Code, string DisplayName, string Flag);

public partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly ILogger _logger;
    private readonly IDispatcherService _dispatcher;
    private readonly Action _scheduleSave;

    // --- Dialog system ---
    [ObservableProperty] private bool _isDialogOpen;
    [ObservableProperty] private string _activeDialog = "";

    // --- Toggle hotkey ---
    [ObservableProperty] private string _toggleModifiers = "Control, Shift";
    [ObservableProperty] private string _toggleKey = "Space";
    [ObservableProperty] private string _toggleDisplayText = "";
    public ObservableCollection<string> ToggleBadges { get; } = [];

    // --- Push-to-Talk hotkey ---
    [ObservableProperty] private string _pttModifiers = "Control";
    [ObservableProperty] private string _pttKey = "Space";
    [ObservableProperty] private string _pttDisplayText = "";
    public ObservableCollection<string> PttBadges { get; } = [];

    // --- Hotkey capture state ---
    [ObservableProperty] private string _capturingHotkey = ""; // "", "Toggle", "PushToTalk"
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
    [
        new("de", "German", "/Resources/Flags/de.png"),
        new("en", "English", "/Resources/Flags/en.png"),
        new("fr", "French", "/Resources/Flags/fr.png"),
        new("es", "Spanish", "/Resources/Flags/es.png"),
        new("it", "Italian", "/Resources/Flags/it.png"),
        new("pt", "Portuguese", "/Resources/Flags/pt.png"),
        new("nl", "Dutch", "/Resources/Flags/nl.png"),
        new("pl", "Polish", "/Resources/Flags/pl.png"),
        new("ru", "Russian", "/Resources/Flags/ru.png"),
        new("uk", "Ukrainian", "/Resources/Flags/uk.png"),
        new("zh", "Chinese", "/Resources/Flags/zh.png"),
        new("ja", "Japanese", "/Resources/Flags/ja.png"),
        new("ko", "Korean", "/Resources/Flags/ko.png"),
        new("ar", "Arabic", "/Resources/Flags/ar.png"),
        new("tr", "Turkish", "/Resources/Flags/tr.png"),
        new("sv", "Swedish", "/Resources/Flags/sv.png"),
        new("da", "Danish", "/Resources/Flags/da.png"),
        new("no", "Norwegian", "/Resources/Flags/no.png"),
        new("fi", "Finnish", "/Resources/Flags/fi.png"),
        new("cs", "Czech", "/Resources/Flags/cs.png"),
    ];

    public GeneralSettingsViewModel(
        IGlobalHotkeyService hotkeyService,
        ILogger logger,
        IDispatcherService dispatcher,
        Action scheduleSave,
        WhisperShowOptions options)
    {
        _hotkeyService = hotkeyService;
        _logger = logger;
        _dispatcher = dispatcher;
        _scheduleSave = scheduleSave;

        _toggleModifiers = options.Hotkey.Toggle.Modifiers;
        _toggleKey = options.Hotkey.Toggle.Key;
        _pttModifiers = options.Hotkey.PushToTalk.Modifiers;
        _pttKey = options.Hotkey.PushToTalk.Key;
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
        var toggleKeys = FormatKeys(ToggleModifiers, ToggleKey);
        var pttKeys = FormatKeys(PttModifiers, PttKey);
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
        _scheduleSave();
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

    public void WriteSettings(JsonNode section)
    {
        section["Language"] = SelectedLanguageCode;

        var hotkey = SettingsViewModel.EnsureObject(section, "Hotkey");
        var toggle = SettingsViewModel.EnsureObject(hotkey, "Toggle");
        toggle["Modifiers"] = ToggleModifiers;
        toggle["Key"] = ToggleKey;
        var ptt = SettingsViewModel.EnsureObject(hotkey, "PushToTalk");
        ptt["Modifiers"] = PttModifiers;
        ptt["Key"] = PttKey;

        SettingsViewModel.EnsureObject(section, "Audio")["DeviceIndex"] = SelectedMicrophoneIndex;
    }
}
