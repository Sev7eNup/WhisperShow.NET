using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using WhisperShow.Core.Services.Hotkey;

namespace WhisperShow.App.ViewModels.Settings;

public record MicrophoneInfo(int DeviceIndex, string Name);
public record LanguageInfo(string Code, string DisplayName, string Flag);

public partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly ILogger _logger;
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

    public GeneralSettingsViewModel(
        IGlobalHotkeyService hotkeyService,
        ILogger logger,
        Action scheduleSave,
        string toggleModifiers,
        string toggleKey,
        string pttModifiers,
        string pttKey,
        int selectedMicrophoneIndex,
        string? selectedLanguageCode)
    {
        _hotkeyService = hotkeyService;
        _logger = logger;
        _scheduleSave = scheduleSave;

        _toggleModifiers = toggleModifiers;
        _toggleKey = toggleKey;
        _pttModifiers = pttModifiers;
        _pttKey = pttKey;
        _selectedMicrophoneIndex = selectedMicrophoneIndex;
        _selectedLanguageCode = selectedLanguageCode;
        _isAutoDetectLanguage = selectedLanguageCode == null;

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

        System.Windows.Application.Current?.Dispatcher.Invoke(() => MicTestLevel = level);
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
        section["Hotkey"]!["Toggle"]!["Modifiers"] = ToggleModifiers;
        section["Hotkey"]!["Toggle"]!["Key"] = ToggleKey;
        section["Hotkey"]!["PushToTalk"]!["Modifiers"] = PttModifiers;
        section["Hotkey"]!["PushToTalk"]!["Key"] = PttKey;
        section["Audio"]!["DeviceIndex"] = SelectedMicrophoneIndex;
    }
}
