using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WriteSpeech.App.Views;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Services.Configuration;

namespace WriteSpeech.App.ViewModels.Settings;

/// <summary>
/// ViewModel for the system settings page.
/// Manages application-level preferences: launch at Windows login, UI theme (light/dark),
/// sound effects, audio muting during dictation, audio compression before cloud upload,
/// overlay visibility and scale, auto-dismiss timing for transcription results,
/// maximum recording duration, and setup wizard reset functionality.
/// </summary>
public partial class SystemSettingsViewModel : ObservableObject
{
    private readonly IAutoStartService _autoStartService;
    private readonly ISettingsPersistenceService _persistenceService;
    private readonly Action _scheduleSave;
    private readonly Action? _restartApp;

    // --- App settings ---
    [ObservableProperty] private bool _launchAtLogin;
    [ObservableProperty] private bool _overlayAlwaysVisible = true;
    [ObservableProperty] private bool _showResultOverlay;
    [ObservableProperty] private bool _showInTaskbar;
    [ObservableProperty] private bool _isDarkMode;

    // --- Sound ---
    [ObservableProperty] private bool _soundEffectsEnabled;
    [ObservableProperty] private bool _muteWhileDictating = true;

    // --- Audio Compression ---
    [ObservableProperty] private bool _audioCompressionEnabled;

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
        ISettingsPersistenceService persistenceService,
        Action scheduleSave,
        WriteSpeechOptions options,
        Action? restartApp = null)
    {
        _autoStartService = autoStartService;
        _persistenceService = persistenceService;
        _scheduleSave = scheduleSave;
        _restartApp = restartApp;

        _launchAtLogin = options.App.LaunchAtLogin;
        _overlayAlwaysVisible = options.Overlay.AlwaysVisible;
        _showResultOverlay = options.Overlay.ShowResultOverlay;
        _showInTaskbar = options.Overlay.ShowInTaskbar;
        _isDarkMode = string.Equals(options.App.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
        _soundEffectsEnabled = options.App.SoundEffects;
        _muteWhileDictating = options.Audio.MuteWhileDictating;
        _audioCompressionEnabled = options.Audio.CompressBeforeUpload;
        _overlayScale = options.Overlay.Scale;
        _autoDismissSeconds = options.Overlay.AutoDismissSeconds;
        _maxRecordingSeconds = options.Audio.MaxRecordingSeconds;
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
    private void ToggleShowResultOverlay() => _scheduleSave();

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

    /// <summary>Sets the auto-dismiss duration (minimum 1 second) for the transcription result overlay and persists the change.</summary>
    public void ApplyAutoDismiss(int seconds)
    {
        AutoDismissSeconds = Math.Max(1, seconds);
        IsEditingAutoDismiss = false;
        _scheduleSave();
    }

    [RelayCommand]
    private void StartEditingMaxRecording() => IsEditingMaxRecording = true;

    /// <summary>Sets the maximum recording duration (minimum 10 seconds) before automatic stop and persists the change.</summary>
    public void ApplyMaxRecording(int seconds)
    {
        MaxRecordingSeconds = Math.Max(10, seconds);
        IsEditingMaxRecording = false;
        _scheduleSave();
    }

    // --- Setup wizard ---

    /// <summary>Test-only override for the reset confirmation dialog. When set, bypasses the WPF ConfirmationDialog and returns the delegate result.</summary>
    internal Func<bool>? ConfirmResetOverride { get; set; }

    [RelayCommand]
    internal async Task ResetSetupWizard()
    {
        bool confirmed;
        if (ConfirmResetOverride is not null)
        {
            confirmed = ConfirmResetOverride();
        }
        else
        {
            var owner = Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive);

            var dialog = new ConfirmationDialog(
                "Reset Settings",
                "Are you sure you want to reset settings and re-run the setup wizard? The app will restart.",
                "Reset",
                owner);

            confirmed = dialog.ShowDialog() == true;
        }

        if (!confirmed)
            return;

        _persistenceService.ScheduleUpdate(section =>
        {
            SettingsViewModel.EnsureObject(section, "App")["SetupCompleted"] = false;
        });
        await _persistenceService.FlushAsync();
        _restartApp?.Invoke();
    }

    // --- Persistence ---

    /// <summary>Writes all system settings (app preferences, audio options, overlay configuration) into the given JSON configuration node for persistence.</summary>
    public void WriteSettings(JsonNode section)
    {
        var app = SettingsViewModel.EnsureObject(section, "App");
        app["LaunchAtLogin"] = LaunchAtLogin;
        app["SoundEffects"] = SoundEffectsEnabled;
        app["Theme"] = IsDarkMode ? "Dark" : "Light";

        var audio = SettingsViewModel.EnsureObject(section, "Audio");
        audio["MaxRecordingSeconds"] = MaxRecordingSeconds;
        audio["CompressBeforeUpload"] = AudioCompressionEnabled;
        audio["MuteWhileDictating"] = MuteWhileDictating;

        var overlay = SettingsViewModel.EnsureObject(section, "Overlay");
        overlay["AutoDismissSeconds"] = AutoDismissSeconds;
        overlay["AlwaysVisible"] = OverlayAlwaysVisible;
        overlay["ShowResultOverlay"] = ShowResultOverlay;
        overlay["ShowInTaskbar"] = ShowInTaskbar;
        overlay["Scale"] = OverlayScale;

    }
}
