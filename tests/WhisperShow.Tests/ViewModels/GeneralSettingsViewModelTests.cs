using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WhisperShow.App.ViewModels.Settings;
using WhisperShow.Core.Services.Hotkey;

namespace WhisperShow.Tests.ViewModels;

public class GeneralSettingsViewModelTests
{
    private readonly IGlobalHotkeyService _hotkeyService = Substitute.For<IGlobalHotkeyService>();
    private bool _saveCalled;

    private GeneralSettingsViewModel CreateViewModel(
        string toggleModifiers = "Control, Shift",
        string toggleKey = "Space",
        string pttModifiers = "Control",
        string pttKey = "Space",
        int selectedMicrophoneIndex = 0,
        string? selectedLanguageCode = "de")
    {
        _saveCalled = false;
        return new GeneralSettingsViewModel(
            _hotkeyService,
            NullLogger<GeneralSettingsViewModel>.Instance,
            () => _saveCalled = true,
            toggleModifiers, toggleKey,
            pttModifiers, pttKey,
            selectedMicrophoneIndex,
            selectedLanguageCode);
    }

    // --- WriteSettings ---

    [Fact]
    public void WriteSettings_WritesAllProperties()
    {
        var vm = CreateViewModel(selectedLanguageCode: "en");
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
        var vm = CreateViewModel(selectedLanguageCode: null);

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
        vm.CapturingHotkey.Should().Be("Toggle");

        vm.CloseDialogCommand.Execute(null);
        vm.IsDialogOpen.Should().BeFalse();
        vm.ActiveDialog.Should().BeEmpty();
        vm.CapturingHotkey.Should().BeEmpty();
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
