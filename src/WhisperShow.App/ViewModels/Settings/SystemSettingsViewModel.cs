using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperShow.Core.Services.Configuration;

namespace WhisperShow.App.ViewModels.Settings;

public partial class SystemSettingsViewModel : ObservableObject
{
    private readonly IAutoStartService _autoStartService;
    private readonly Action _scheduleSave;

    // --- App settings ---
    [ObservableProperty] private bool _launchAtLogin;
    [ObservableProperty] private bool _overlayAlwaysVisible = true;
    [ObservableProperty] private bool _showInTaskbar;
    [ObservableProperty] private bool _isDarkMode;

    // --- Sound ---
    [ObservableProperty] private bool _soundEffectsEnabled = true;
    [ObservableProperty] private bool _muteWhileDictating = true;

    // --- Audio Compression ---
    [ObservableProperty] private bool _audioCompressionEnabled = true;

    // --- Overlay scale ---
    [ObservableProperty] private double _overlayScale = 1.0;

    // --- Auto-dismiss ---
    [ObservableProperty] private int _autoDismissSeconds = 10;
    [ObservableProperty] private bool _isEditingAutoDismiss;

    // --- Max recording ---
    [ObservableProperty] private int _maxRecordingSeconds = 300;
    [ObservableProperty] private bool _isEditingMaxRecording;

    public SystemSettingsViewModel(
        IAutoStartService autoStartService,
        Action scheduleSave,
        bool launchAtLogin,
        bool overlayAlwaysVisible,
        bool showInTaskbar,
        bool isDarkMode,
        bool soundEffectsEnabled,
        bool muteWhileDictating,
        bool audioCompressionEnabled,
        double overlayScale,
        int autoDismissSeconds,
        int maxRecordingSeconds)
    {
        _autoStartService = autoStartService;
        _scheduleSave = scheduleSave;

        _launchAtLogin = launchAtLogin;
        _overlayAlwaysVisible = overlayAlwaysVisible;
        _showInTaskbar = showInTaskbar;
        _isDarkMode = isDarkMode;
        _soundEffectsEnabled = soundEffectsEnabled;
        _muteWhileDictating = muteWhileDictating;
        _audioCompressionEnabled = audioCompressionEnabled;
        _overlayScale = overlayScale;
        _autoDismissSeconds = autoDismissSeconds;
        _maxRecordingSeconds = maxRecordingSeconds;
    }

    // --- App settings ---

    [RelayCommand]
    private void ToggleLaunchAtLogin()
    {
        _autoStartService.SetAutoStart(LaunchAtLogin);
        _scheduleSave();
    }

    partial void OnOverlayScaleChanged(double value) => _scheduleSave();

    [RelayCommand]
    private void ToggleOverlayAlwaysVisible() => _scheduleSave();

    [RelayCommand]
    private void ToggleShowInTaskbar() => _scheduleSave();

    [RelayCommand]
    private void ToggleDarkMode() => _scheduleSave();

    // --- Sound ---

    [RelayCommand]
    private void ToggleSoundEffects() => _scheduleSave();

    [RelayCommand]
    private void ToggleMuteWhileDictating() => _scheduleSave();

    // --- Audio ---

    [RelayCommand]
    private void ToggleAudioCompression() => _scheduleSave();

    [RelayCommand]
    private void StartEditingAutoDismiss() => IsEditingAutoDismiss = true;

    public void ApplyAutoDismiss(int seconds)
    {
        AutoDismissSeconds = seconds;
        IsEditingAutoDismiss = false;
        _scheduleSave();
    }

    [RelayCommand]
    private void StartEditingMaxRecording() => IsEditingMaxRecording = true;

    public void ApplyMaxRecording(int seconds)
    {
        MaxRecordingSeconds = seconds;
        IsEditingMaxRecording = false;
        _scheduleSave();
    }

    // --- Persistence ---

    public void WriteSettings(JsonNode section)
    {
        section["App"]!["LaunchAtLogin"] = LaunchAtLogin;
        section["App"]!["SoundEffects"] = SoundEffectsEnabled;
        section["App"]!["Theme"] = IsDarkMode ? "Dark" : "Light";
        section["Audio"]!["MaxRecordingSeconds"] = MaxRecordingSeconds;
        section["Audio"]!["CompressBeforeUpload"] = AudioCompressionEnabled;
        section["Audio"]!["MuteWhileDictating"] = MuteWhileDictating;
        section["Overlay"]!["AutoDismissSeconds"] = AutoDismissSeconds;
        section["Overlay"]!["AlwaysVisible"] = OverlayAlwaysVisible;
        section["Overlay"]!["ShowInTaskbar"] = ShowInTaskbar;
        section["Overlay"]!["Scale"] = OverlayScale;
    }
}
