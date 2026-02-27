using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using WriteSpeech.App.ViewModels.Settings;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Services.Configuration;

namespace WriteSpeech.Tests.ViewModels;

public class SystemSettingsViewModelTests
{
    private readonly IAutoStartService _autoStartService = Substitute.For<IAutoStartService>();
    private readonly ISettingsPersistenceService _persistenceService = Substitute.For<ISettingsPersistenceService>();
    private bool _saveCalled;
    private bool _restartCalled;

    private SystemSettingsViewModel CreateViewModel(Action<WriteSpeechOptions>? configure = null, Action? restartApp = null)
    {
        _saveCalled = false;
        _restartCalled = false;
        var options = new WriteSpeechOptions();
        configure?.Invoke(options);
        return new SystemSettingsViewModel(
            _autoStartService,
            _persistenceService,
            () => _saveCalled = true,
            options,
            restartApp);
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

    // --- Clamping validation in ViewModel ---

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(15, 15)]
    public void ApplyAutoDismiss_ClampsToMinimum(int input, int expected)
    {
        var vm = CreateViewModel();
        vm.ApplyAutoDismiss(input);
        vm.AutoDismissSeconds.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(5, 10)]
    [InlineData(-1, 10)]
    [InlineData(10, 10)]
    [InlineData(600, 600)]
    public void ApplyMaxRecording_ClampsToMinimum(int input, int expected)
    {
        var vm = CreateViewModel();
        vm.ApplyMaxRecording(input);
        vm.MaxRecordingSeconds.Should().Be(expected);
    }

    // --- Reset setup wizard ---

    [Fact]
    public async Task ResetSetupWizard_WhenConfirmed_CallsScheduleUpdateAndFlush()
    {
        var vm = CreateViewModel();
        vm.ConfirmResetOverride = () => true;

        await vm.ResetSetupWizardCommand.ExecuteAsync(null);

        _persistenceService.Received(1).ScheduleUpdate(Arg.Any<Action<JsonNode>>());
        await _persistenceService.Received(1).FlushAsync();
    }

    [Fact]
    public async Task ResetSetupWizard_WhenConfirmed_SetsSetupCompletedFalse()
    {
        var vm = CreateViewModel();
        vm.ConfirmResetOverride = () => true;

        JsonNode? capturedSection = null;
        _persistenceService.When(x => x.ScheduleUpdate(Arg.Any<Action<JsonNode>>()))
            .Do(call =>
            {
                capturedSection = new JsonObject();
                call.Arg<Action<JsonNode>>()(capturedSection);
            });

        await vm.ResetSetupWizardCommand.ExecuteAsync(null);

        capturedSection!["App"]!["SetupCompleted"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task ResetSetupWizard_WhenConfirmed_CallsRestartCallback()
    {
        var vm = CreateViewModel(restartApp: () => _restartCalled = true);
        vm.ConfirmResetOverride = () => true;

        await vm.ResetSetupWizardCommand.ExecuteAsync(null);

        _restartCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ResetSetupWizard_WhenConfirmed_WithoutRestartCallback_DoesNotThrow()
    {
        var vm = CreateViewModel(restartApp: null);
        vm.ConfirmResetOverride = () => true;

        var act = async () => await vm.ResetSetupWizardCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ResetSetupWizard_WhenCancelled_DoesNotCallScheduleUpdate()
    {
        var vm = CreateViewModel();
        vm.ConfirmResetOverride = () => false;

        await vm.ResetSetupWizardCommand.ExecuteAsync(null);

        _persistenceService.DidNotReceive().ScheduleUpdate(Arg.Any<Action<JsonNode>>());
    }

    [Fact]
    public async Task ResetSetupWizard_WhenCancelled_DoesNotCallRestart()
    {
        var vm = CreateViewModel(restartApp: () => _restartCalled = true);
        vm.ConfirmResetOverride = () => false;

        await vm.ResetSetupWizardCommand.ExecuteAsync(null);

        _restartCalled.Should().BeFalse();
    }
}
