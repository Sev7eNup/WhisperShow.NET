using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WhisperShow.App.ViewModels.Settings;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Services.Hotkey;
using WhisperShow.Tests.TestHelpers;

namespace WhisperShow.Tests.ViewModels;

public class GeneralSettingsViewModelTests
{
    private readonly IGlobalHotkeyService _hotkeyService = Substitute.For<IGlobalHotkeyService>();
    private bool _saveCalled;

    private GeneralSettingsViewModel CreateViewModel(Action<WhisperShowOptions>? configure = null)
    {
        _saveCalled = false;
        var options = new WhisperShowOptions { Language = "de" };
        configure?.Invoke(options);
        return new GeneralSettingsViewModel(
            _hotkeyService,
            NullLogger<GeneralSettingsViewModel>.Instance,
            new SynchronousDispatcherService(),
            () => _saveCalled = true,
            options);
    }

    // --- WriteSettings ---

    [Fact]
    public void WriteSettings_WritesAllProperties()
    {
        var vm = CreateViewModel(o => o.Language = "en");
        vm.General_OpenLanguageDialogForTest();

        var json = JsonNode.Parse("""
        {
            "Language": null,
            "Hotkey": { "Toggle": { "Modifiers": "", "Key": "" }, "PushToTalk": { "Modifiers": "", "Key": "" } },
            "Audio": { "DeviceIndex": 0 }
        }
        """)!;

        vm.WriteSettings(json);

        json["Language"]!.GetValue<string>().Should().Be("en");
        json["Hotkey"]!["Toggle"]!["Modifiers"]!.GetValue<string>().Should().Be("Control, Shift");
        json["Hotkey"]!["Toggle"]!["Key"]!.GetValue<string>().Should().Be("Space");
        json["Hotkey"]!["PushToTalk"]!["Modifiers"]!.GetValue<string>().Should().Be("Control");
        json["Hotkey"]!["PushToTalk"]!["Key"]!.GetValue<string>().Should().Be("Space");
        json["Audio"]!["DeviceIndex"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public void WriteSettings_NullLanguage_WritesNull()
    {
        var vm = CreateViewModel(o => o.Language = null);

        var json = JsonNode.Parse("""
        {
            "Language": "de",
            "Hotkey": { "Toggle": { "Modifiers": "", "Key": "" }, "PushToTalk": { "Modifiers": "", "Key": "" } },
            "Audio": { "DeviceIndex": 0 }
        }
        """)!;

        vm.WriteSettings(json);

        json["Language"].Should().BeNull();
    }

    [Fact]
    public void WriteSettings_MissingSections_CreatesThemAutomatically()
    {
        var vm = CreateViewModel();
        var json = JsonNode.Parse("{}")!;

        var act = () => vm.WriteSettings(json);

        act.Should().NotThrow();
        json["Hotkey"]!["Toggle"]!["Modifiers"]!.GetValue<string>().Should().Be("Control, Shift");
        json["Hotkey"]!["Toggle"]!["Key"]!.GetValue<string>().Should().Be("Space");
        json["Hotkey"]!["PushToTalk"]!["Modifiers"]!.GetValue<string>().Should().Be("Control");
        json["Hotkey"]!["PushToTalk"]!["Key"]!.GetValue<string>().Should().Be("Space");
        json["Audio"]!["DeviceIndex"]!.GetValue<int>().Should().Be(0);
    }

    // --- Hotkey changes trigger save ---

    [Fact]
    public void ApplyNewHotkey_TriggersSave()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);
        vm.StartCapturingToggleHotkeyCommand.Execute(null);

        vm.ApplyNewHotkey("Alt", "F1");

        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ResetHotkeyToDefault_TriggersSave()
    {
        var vm = CreateViewModel();

        vm.ResetHotkeyToDefaultCommand.Execute(null);

        _saveCalled.Should().BeTrue();
    }

    // --- Microphone selection triggers save ---

    [Fact]
    public void SelectMicrophone_TriggersSave()
    {
        var vm = CreateViewModel();
        vm.OpenMicrophoneDialogCommand.Execute(null);

        vm.SelectMicrophone(1);

        _saveCalled.Should().BeTrue();
    }

    // --- Language ---

    [Fact]
    public void SaveAndCloseLanguage_TriggersSave()
    {
        var vm = CreateViewModel();
        vm.OpenLanguageDialogCommand.Execute(null);
        vm.SelectLanguageCommand.Execute("fr");

        vm.SaveAndCloseLanguageCommand.Execute(null);

        _saveCalled.Should().BeTrue();
        vm.SelectedLanguageCode.Should().Be("fr");
    }

    // --- Dialog state ---

    [Fact]
    public void OpenAndCloseDialog_ResetsAllState()
    {
        var vm = CreateViewModel();

        vm.OpenHotkeyDialogCommand.Execute(null);
        vm.StartCapturingToggleHotkeyCommand.Execute(null);
        vm.IsDialogOpen.Should().BeTrue();
        vm.CapturingHotkey.Should().Be(HotkeyCaptureTarget.Toggle);

        vm.CloseDialogCommand.Execute(null);
        vm.IsDialogOpen.Should().BeFalse();
        vm.ActiveDialog.Should().Be(SettingsDialogType.None);
        vm.CapturingHotkey.Should().Be(HotkeyCaptureTarget.None);
    }
}

// Test helper extension to avoid coupling to internal state
file static class GeneralSettingsViewModelTestExtensions
{
    public static void General_OpenLanguageDialogForTest(this GeneralSettingsViewModel vm)
    {
        // Language is already set via constructor, no need to open dialog for WriteSettings test
    }
}
