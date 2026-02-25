using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.App.ViewModels.Settings;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Services.Hotkey;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.ViewModels;

public class GeneralSettingsViewModelTests
{
    private readonly IGlobalHotkeyService _hotkeyService = Substitute.For<IGlobalHotkeyService>();
    private bool _saveCalled;

    private GeneralSettingsViewModel CreateViewModel(Action<WriteSpeechOptions>? configure = null)
    {
        _saveCalled = false;
        var options = new WriteSpeechOptions { Language = "de" };
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

    // --- Mouse button hotkey support ---

    [Fact]
    public void Constructor_LoadsMouseButtonFromOptions()
    {
        var vm = CreateViewModel(o =>
        {
            o.Hotkey.Toggle.MouseButton = "XButton1";
            o.Hotkey.PushToTalk.MouseButton = "Middle";
        });

        vm.ToggleMouseButton.Should().Be("XButton1");
        vm.PttMouseButton.Should().Be("Middle");
    }

    [Fact]
    public void Constructor_LoadsHotkeyMethodFromOptions()
    {
        var vm = CreateViewModel(o => o.Hotkey.Method = "LowLevelHook");

        vm.HotkeyMethod.Should().Be("LowLevelHook");
        vm.IsLowLevelHookMode.Should().BeTrue();
    }

    [Fact]
    public void Constructor_DefaultHotkeyMethod_IsRegisterHotKey()
    {
        var vm = CreateViewModel();

        vm.HotkeyMethod.Should().Be("RegisterHotKey");
        vm.IsLowLevelHookMode.Should().BeFalse();
    }

    [Fact]
    public void ApplyNewHotkey_WithMouseButton_UpdatesToggleMouseButton()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);
        vm.StartCapturingToggleHotkeyCommand.Execute(null);

        vm.ApplyNewHotkey("Control", null, "XButton1");

        vm.ToggleMouseButton.Should().Be("XButton1");
        vm.ToggleKey.Should().BeEmpty();
        vm.ToggleModifiers.Should().Be("Control");
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ApplyNewHotkey_WithMouseButton_UpdatesPttMouseButton()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);
        vm.StartCapturingPttHotkeyCommand.Execute(null);

        vm.ApplyNewHotkey("", null, "XButton2");

        vm.PttMouseButton.Should().Be("XButton2");
        vm.PttKey.Should().BeEmpty();
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void ApplyNewHotkey_WithKeyboard_ClearsMouseButton()
    {
        var vm = CreateViewModel(o => o.Hotkey.Toggle.MouseButton = "XButton1");
        vm.OpenHotkeyDialogCommand.Execute(null);
        vm.StartCapturingToggleHotkeyCommand.Execute(null);

        vm.ApplyNewHotkey("Control, Shift", "F1", null);

        vm.ToggleMouseButton.Should().BeNull();
        vm.ToggleKey.Should().Be("F1");
    }

    [Fact]
    public void ResetHotkeyToDefault_ClearsMouseButtons()
    {
        var vm = CreateViewModel(o =>
        {
            o.Hotkey.Toggle.MouseButton = "XButton1";
            o.Hotkey.PushToTalk.MouseButton = "Middle";
        });

        vm.ResetHotkeyToDefaultCommand.Execute(null);

        vm.ToggleMouseButton.Should().BeNull();
        vm.PttMouseButton.Should().BeNull();
        vm.ToggleKey.Should().Be("Space");
        vm.PttKey.Should().Be("Space");
    }

    [Fact]
    public void WriteSettings_WritesMouseButtonAndMethod()
    {
        var vm = CreateViewModel(o =>
        {
            o.Hotkey.Method = "LowLevelHook";
            o.Hotkey.Toggle.MouseButton = "XButton1";
            o.Hotkey.PushToTalk.MouseButton = "Middle";
        });

        var json = JsonNode.Parse("{}")!;
        vm.WriteSettings(json);

        json["Hotkey"]!["Method"]!.GetValue<string>().Should().Be("LowLevelHook");
        json["Hotkey"]!["Toggle"]!["MouseButton"]!.GetValue<string>().Should().Be("XButton1");
        json["Hotkey"]!["PushToTalk"]!["MouseButton"]!.GetValue<string>().Should().Be("Middle");
    }

    [Fact]
    public void WriteSettings_NullMouseButton_WritesNull()
    {
        var vm = CreateViewModel();

        var json = JsonNode.Parse("{}")!;
        vm.WriteSettings(json);

        json["Hotkey"]!["Toggle"]!["MouseButton"].Should().BeNull();
        json["Hotkey"]!["PushToTalk"]!["MouseButton"].Should().BeNull();
    }

    [Fact]
    public void SetHotkeyMethod_UpdatesProperties()
    {
        var vm = CreateViewModel();

        vm.SetHotkeyMethodCommand.Execute("LowLevelHook");

        vm.HotkeyMethod.Should().Be("LowLevelHook");
        vm.IsLowLevelHookMode.Should().BeTrue();
        _saveCalled.Should().BeTrue();
    }

    [Fact]
    public void SetHotkeyMethod_CallsSwitchMethod()
    {
        var vm = CreateViewModel();

        vm.SetHotkeyMethodCommand.Execute("LowLevelHook");

        _hotkeyService.Received(1).SwitchMethod("LowLevelHook");
    }

    [Fact]
    public void SetHotkeyMethod_ToRegisterHotKey_ClearsMouseBindings()
    {
        var vm = CreateViewModel(o =>
        {
            o.Hotkey.Method = "LowLevelHook";
            o.Hotkey.Toggle.MouseButton = "Middle";
            o.Hotkey.PushToTalk.MouseButton = "XButton2";
        });

        vm.SetHotkeyMethodCommand.Execute("RegisterHotKey");

        vm.ToggleMouseButton.Should().BeNull();
        vm.PttMouseButton.Should().BeNull();
        vm.ToggleKey.Should().Be("Space");
        vm.ToggleModifiers.Should().Be("Control, Shift");
        vm.PttKey.Should().Be("Space");
        vm.PttModifiers.Should().Be("Control");
        vm.HotkeyMethod.Should().Be("RegisterHotKey");
        vm.IsLowLevelHookMode.Should().BeFalse();
    }

    [Fact]
    public void SetHotkeyMethod_ToRegisterHotKey_WithoutMouseBindings_KeepsCurrentKeys()
    {
        var vm = CreateViewModel(o =>
        {
            o.Hotkey.Method = "LowLevelHook";
            o.Hotkey.Toggle.Modifiers = "Alt";
            o.Hotkey.Toggle.Key = "F1";
            o.Hotkey.PushToTalk.Modifiers = "Alt";
            o.Hotkey.PushToTalk.Key = "F2";
        });

        vm.SetHotkeyMethodCommand.Execute("RegisterHotKey");

        vm.ToggleKey.Should().Be("F1");
        vm.ToggleModifiers.Should().Be("Alt");
        vm.PttKey.Should().Be("F2");
        vm.PttModifiers.Should().Be("Alt");
    }

    [Fact]
    public void ToggleBadges_WithMouseButton_ShowsMouseButtonName()
    {
        var vm = CreateViewModel(o =>
        {
            o.Hotkey.Toggle.Modifiers = "Control";
            o.Hotkey.Toggle.Key = "";
            o.Hotkey.Toggle.MouseButton = "XButton1";
        });

        vm.ToggleBadges.Should().Contain("Ctrl");
        vm.ToggleBadges.Should().Contain("Mouse 4");
        vm.ToggleBadges.Should().NotContain("");
    }

    [Fact]
    public void PttBadges_WithMiddleClick_ShowsMiddleClick()
    {
        var vm = CreateViewModel(o =>
        {
            o.Hotkey.PushToTalk.Modifiers = "";
            o.Hotkey.PushToTalk.Key = "";
            o.Hotkey.PushToTalk.MouseButton = "Middle";
        });

        vm.PttBadges.Should().Contain("Middle Click");
    }

    [Theory]
    [InlineData("XButton1", "Mouse 4")]
    [InlineData("XButton2", "Mouse 5")]
    [InlineData("Middle", "Middle Click")]
    public void FormatMouseButton_ReturnsExpectedDisplayName(string mouseButton, string expected)
    {
        GeneralSettingsViewModel.FormatMouseButton(mouseButton).Should().Be(expected);
    }

    // --- SuppressActions during capture ---

    [Fact]
    public void StartCapturingToggleHotkey_SetsSuppressActions()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);

        vm.StartCapturingToggleHotkeyCommand.Execute(null);

        _hotkeyService.SuppressActions.Should().BeTrue();
    }

    [Fact]
    public void StartCapturingPttHotkey_SetsSuppressActions()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);

        vm.StartCapturingPttHotkeyCommand.Execute(null);

        _hotkeyService.SuppressActions.Should().BeTrue();
    }

    [Fact]
    public void ApplyNewHotkey_ClearsSuppressActions()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);
        vm.StartCapturingToggleHotkeyCommand.Execute(null);

        vm.ApplyNewHotkey("Alt", "F1");

        _hotkeyService.SuppressActions.Should().BeFalse();
    }

    [Fact]
    public void CloseDialog_ClearsSuppressActions()
    {
        var vm = CreateViewModel();
        vm.OpenHotkeyDialogCommand.Execute(null);
        vm.StartCapturingToggleHotkeyCommand.Execute(null);

        vm.CloseDialogCommand.Execute(null);

        _hotkeyService.SuppressActions.Should().BeFalse();
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
