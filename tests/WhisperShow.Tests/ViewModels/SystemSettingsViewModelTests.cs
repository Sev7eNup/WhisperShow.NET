using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using WhisperShow.App.ViewModels.Settings;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Services.Configuration;

namespace WhisperShow.Tests.ViewModels;

public class SystemSettingsViewModelTests
{
    private readonly IAutoStartService _autoStartService = Substitute.For<IAutoStartService>();
    private bool _saveCalled;

    private SystemSettingsViewModel CreateViewModel(Action<WhisperShowOptions>? configure = null)
    {
        _saveCalled = false;
        var options = new WhisperShowOptions();
        configure?.Invoke(options);
        return new SystemSettingsViewModel(
            _autoStartService,
            () => _saveCalled = true,
            options);
    }

    // --- Initialization ---

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var vm = CreateViewModel(o =>
        {
            o.App.LaunchAtLogin = true;
            o.Overlay.AlwaysVisible = false;
            o.Overlay.ShowResultOverlay = false;
            o.Overlay.ShowInTaskbar = true;
            o.App.Theme = "Dark";
            o.App.SoundEffects = false;
            o.Audio.MuteWhileDictating = false;
            o.Audio.CompressBeforeUpload = false;
            o.Overlay.Scale = 1.5;
            o.Overlay.AutoDismissSeconds = 20;
            o.Audio.MaxRecordingSeconds = 600;
        });

        vm.LaunchAtLogin.Should().BeTrue();
        vm.OverlayAlwaysVisible.Should().BeFalse();
        vm.ShowResultOverlay.Should().BeFalse();
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
        var vm = CreateViewModel(o => o.App.LaunchAtLogin = true);

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
    public void ToggleShowResultOverlay_TriggersSave()
    {
        var vm = CreateViewModel();
        vm.ToggleShowResultOverlayCommand.Execute(null);
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
        var vm = CreateViewModel();
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
        var vm = CreateViewModel(o =>
        {
            o.App.LaunchAtLogin = true;
            o.App.Theme = "Dark";
            o.App.SoundEffects = false;
            o.Audio.MuteWhileDictating = false;
            o.Audio.CompressBeforeUpload = false;
            o.Overlay.AlwaysVisible = false;
            o.Overlay.ShowResultOverlay = false;
            o.Overlay.ShowInTaskbar = true;
            o.Overlay.Scale = 1.5;
            o.Overlay.AutoDismissSeconds = 15;
            o.Audio.MaxRecordingSeconds = 600;
        });

        var json = JsonNode.Parse("""
        {
            "App": { "LaunchAtLogin": false, "SoundEffects": true, "Theme": "Light" },
            "Audio": { "MaxRecordingSeconds": 300, "CompressBeforeUpload": true, "MuteWhileDictating": true },
            "Overlay": { "AutoDismissSeconds": 10, "AlwaysVisible": true, "ShowResultOverlay": true, "ShowInTaskbar": false, "Scale": 1.0 }
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
        json["Overlay"]!["ShowResultOverlay"]!.GetValue<bool>().Should().BeFalse();
        json["Overlay"]!["ShowInTaskbar"]!.GetValue<bool>().Should().BeTrue();
        json["Overlay"]!["Scale"]!.GetValue<double>().Should().Be(1.5);
    }

    [Fact]
    public void WriteSettings_LightTheme_WritesLight()
    {
        var vm = CreateViewModel(o => o.App.Theme = "Light");

        var json = JsonNode.Parse("""
        {
            "App": { "LaunchAtLogin": false, "SoundEffects": true, "Theme": "Dark" },
            "Audio": { "MaxRecordingSeconds": 300, "CompressBeforeUpload": true, "MuteWhileDictating": true },
            "Overlay": { "AutoDismissSeconds": 10, "AlwaysVisible": true, "ShowResultOverlay": true, "ShowInTaskbar": false, "Scale": 1.0 }
        }
        """)!;

        vm.WriteSettings(json);

        json["App"]!["Theme"]!.GetValue<string>().Should().Be("Light");
    }

    [Fact]
    public void WriteSettings_MissingSections_CreatesThemAutomatically()
    {
        var vm = CreateViewModel();
        var json = JsonNode.Parse("{}")!;

        var act = () => vm.WriteSettings(json);

        act.Should().NotThrow();
        json["App"]!["LaunchAtLogin"]!.GetValue<bool>().Should().BeFalse();
        json["App"]!["Theme"]!.GetValue<string>().Should().Be("Dark");
        json["Audio"]!["MaxRecordingSeconds"]!.GetValue<int>().Should().Be(300);
        json["Overlay"]!["AutoDismissSeconds"]!.GetValue<int>().Should().Be(10);
        json["Overlay"]!["ShowResultOverlay"]!.GetValue<bool>().Should().BeTrue();
    }
}
