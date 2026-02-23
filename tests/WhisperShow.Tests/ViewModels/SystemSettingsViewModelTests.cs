using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using WhisperShow.App.ViewModels.Settings;
using WhisperShow.Core.Services.Configuration;

namespace WhisperShow.Tests.ViewModels;

public class SystemSettingsViewModelTests
{
    private readonly IAutoStartService _autoStartService = Substitute.For<IAutoStartService>();
    private bool _saveCalled;

    private SystemSettingsViewModel CreateViewModel(
        bool launchAtLogin = false,
        bool overlayAlwaysVisible = true,
        bool showInTaskbar = false,
        bool isDarkMode = false,
        bool soundEffectsEnabled = true,
        bool muteWhileDictating = true,
        bool audioCompressionEnabled = true,
        double overlayScale = 1.0,
        int autoDismissSeconds = 10,
        int maxRecordingSeconds = 300)
    {
        _saveCalled = false;
        return new SystemSettingsViewModel(
            _autoStartService,
            () => _saveCalled = true,
            launchAtLogin, overlayAlwaysVisible, showInTaskbar, isDarkMode,
            soundEffectsEnabled, muteWhileDictating, audioCompressionEnabled,
            overlayScale, autoDismissSeconds, maxRecordingSeconds);
    }

    // --- Initialization ---

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var vm = CreateViewModel(
            launchAtLogin: true,
            overlayAlwaysVisible: false,
            showInTaskbar: true,
            isDarkMode: true,
            soundEffectsEnabled: false,
            muteWhileDictating: false,
            audioCompressionEnabled: false,
            overlayScale: 1.5,
            autoDismissSeconds: 20,
            maxRecordingSeconds: 600);

        vm.LaunchAtLogin.Should().BeTrue();
        vm.OverlayAlwaysVisible.Should().BeFalse();
        vm.ShowInTaskbar.Should().BeTrue();
        vm.IsDarkMode.Should().BeTrue();
        vm.SoundEffectsEnabled.Should().BeFalse();
        vm.MuteWhileDictating.Should().BeFalse();
        vm.AudioCompressionEnabled.Should().BeFalse();
        vm.OverlayScale.Should().Be(1.5);
        vm.AutoDismissSeconds.Should().Be(20);
        vm.MaxRecordingSeconds.Should().Be(600);
    }

    // --- Toggle commands trigger save ---

    [Fact]
    public void ToggleLaunchAtLogin_CallsAutoStartAndSave()
    {
        var vm = CreateViewModel(launchAtLogin: true);

        vm.ToggleLaunchAtLoginCommand.Execute(null);

        _autoStartService.Received(1).SetAutoStart(true);
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ToggleOverlayAlwaysVisible_TriggersSave()
    {
        var vm = CreateViewModel();
        vm.ToggleOverlayAlwaysVisibleCommand.Execute(null);
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ToggleDarkMode_TriggersSave()
    {
        var vm = CreateViewModel();
        vm.ToggleDarkModeCommand.Execute(null);
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void OverlayScaleChanged_TriggersSave()
    {
        var vm = CreateViewModel(overlayScale: 1.0);
        vm.OverlayScale = 1.5;
        _saveCalled.Should().BeTrue();
    }

    // --- Editing modes ---

    [Fact]
    public void ApplyAutoDismiss_SetsValueAndExitsEditing()
    {
        var vm = CreateViewModel();
        vm.StartEditingAutoDismissCommand.Execute(null);
        vm.IsEditingAutoDismiss.Should().BeTrue();

        vm.ApplyAutoDismiss(15);

        vm.AutoDismissSeconds.Should().Be(15);
        vm.IsEditingAutoDismiss.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ApplyMaxRecording_SetsValueAndExitsEditing()
    {
        var vm = CreateViewModel();
        vm.StartEditingMaxRecordingCommand.Execute(null);
        vm.IsEditingMaxRecording.Should().BeTrue();

        vm.ApplyMaxRecording(600);

        vm.MaxRecordingSeconds.Should().Be(600);
        vm.IsEditingMaxRecording.Should().BeFalse();
        _saveCalled.Should().BeTrue();
    }

    // --- WriteSettings ---

    [Fact]
    public void WriteSettings_WritesAllProperties()
    {
        var vm = CreateViewModel(
            launchAtLogin: true,
            isDarkMode: true,
            soundEffectsEnabled: false,
            muteWhileDictating: false,
            audioCompressionEnabled: false,
            overlayAlwaysVisible: false,
            showInTaskbar: true,
            overlayScale: 1.5,
            autoDismissSeconds: 15,
            maxRecordingSeconds: 600);

        var json = JsonNode.Parse("""
        {
            "App": { "LaunchAtLogin": false, "SoundEffects": true, "Theme": "Light" },
            "Audio": { "MaxRecordingSeconds": 300, "CompressBeforeUpload": true, "MuteWhileDictating": true },
            "Overlay": { "AutoDismissSeconds": 10, "AlwaysVisible": true, "ShowInTaskbar": false, "Scale": 1.0 }
        }
        """)!;

        vm.WriteSettings(json);

        json["App"]!["LaunchAtLogin"]!.GetValue<bool>().Should().BeTrue();
        json["App"]!["SoundEffects"]!.GetValue<bool>().Should().BeFalse();
        json["App"]!["Theme"]!.GetValue<string>().Should().Be("Dark");
        json["Audio"]!["MaxRecordingSeconds"]!.GetValue<int>().Should().Be(600);
        json["Audio"]!["CompressBeforeUpload"]!.GetValue<bool>().Should().BeFalse();
        json["Audio"]!["MuteWhileDictating"]!.GetValue<bool>().Should().BeFalse();
        json["Overlay"]!["AutoDismissSeconds"]!.GetValue<int>().Should().Be(15);
        json["Overlay"]!["AlwaysVisible"]!.GetValue<bool>().Should().BeFalse();
        json["Overlay"]!["ShowInTaskbar"]!.GetValue<bool>().Should().BeTrue();
        json["Overlay"]!["Scale"]!.GetValue<double>().Should().Be(1.5);
    }

    [Fact]
    public void WriteSettings_LightTheme_WritesLight()
    {
        var vm = CreateViewModel(isDarkMode: false);

        var json = JsonNode.Parse("""
        {
            "App": { "LaunchAtLogin": false, "SoundEffects": true, "Theme": "Dark" },
            "Audio": { "MaxRecordingSeconds": 300, "CompressBeforeUpload": true, "MuteWhileDictating": true },
            "Overlay": { "AutoDismissSeconds": 10, "AlwaysVisible": true, "ShowInTaskbar": false, "Scale": 1.0 }
        }
        """)!;

        vm.WriteSettings(json);

        json["App"]!["Theme"]!.GetValue<string>().Should().Be("Light");
    }
}
