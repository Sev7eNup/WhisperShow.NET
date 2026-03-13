using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.Modes;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.Services;

public class ModeServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ModeService _service;

    public ModeServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"writespeech-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var optionsMonitor = OptionsHelper.CreateMonitor(_ => { });
        _service = new ModeService(NullLogger<ModeService>.Instance, optionsMonitor);
        SetFilePath(_service, Path.Combine(_tempDir, "modes.json"));
        _service.LoadAsync().GetAwaiter().GetResult();
    }

    private static void SetFilePath(ModeService service, string path)
    {
        var field = typeof(ModeService).GetField("_filePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(service, path);
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // --- GetModes ---

    [Fact]
    public void GetModes_ReturnsBuiltInModes()
    {
        var modes = _service.GetModes();
        modes.Should().HaveCount(6);
        modes.Select(m => m.Name).Should().Contain(["Default", "E-Mail", "Message", "Code", "Note", "Translate"]);
    }

    [Fact]
    public void GetModes_AllBuiltInModesAreMarkedAsBuiltIn()
    {
        var modes = _service.GetModes();
        modes.Where(m => m.IsBuiltIn).Should().HaveCount(6);
    }

    // --- AddMode ---

    [Fact]
    public void AddMode_AddsCustomMode()
    {
        _service.AddMode("MyMode", "My prompt", ["myapp"]);

        var modes = _service.GetModes();
        modes.Should().HaveCount(7);
        var custom = modes.First(m => m.Name == "MyMode");
        custom.IsBuiltIn.Should().BeFalse();
        custom.SystemPrompt.Should().Be("My prompt");
        custom.AppPatterns.Should().Equal("myapp");
    }

    [Fact]
    public void AddMode_DuplicateName_DoesNotAdd()
    {
        _service.AddMode("E-Mail", "duplicate", []);

        _service.GetModes().Should().HaveCount(6);
    }

    [Fact]
    public void AddMode_EmptyName_DoesNotAdd()
    {
        _service.AddMode("", "prompt", []);
        _service.AddMode("  ", "prompt", []);

        _service.GetModes().Should().HaveCount(6);
    }

    [Fact]
    public void AddMode_EmptyPrompt_DoesNotAdd()
    {
        _service.AddMode("Test", "", []);

        _service.GetModes().Should().HaveCount(6);
    }

    // --- RemoveMode ---

    [Fact]
    public void RemoveMode_RemovesCustomMode()
    {
        _service.AddMode("Custom", "prompt", []);
        _service.GetModes().Should().HaveCount(7);

        _service.RemoveMode("Custom");

        _service.GetModes().Should().HaveCount(6);
    }

    [Fact]
    public void RemoveMode_BuiltInMode_DoesNotRemove()
    {
        _service.RemoveMode("E-Mail");

        _service.GetModes().Should().HaveCount(6);
        _service.GetModes().Should().Contain(m => m.Name == "E-Mail");
    }

    [Fact]
    public void RemoveMode_ActiveMode_ResetsToNull()
    {
        _service.AddMode("Custom", "prompt", []);
        _service.SetActiveMode("Custom");
        _service.ActiveModeName.Should().Be("Custom");

        _service.RemoveMode("Custom");

        _service.ActiveModeName.Should().BeNull();
    }

    // --- UpdateMode ---

    [Fact]
    public void UpdateMode_UpdatesPromptAndPatterns()
    {
        _service.AddMode("Custom", "old prompt", ["app1"]);

        _service.UpdateMode("Custom", "Custom", "new prompt", ["app2", "app3"]);

        var mode = _service.GetModes().First(m => m.Name == "Custom");
        mode.SystemPrompt.Should().Be("new prompt");
        mode.AppPatterns.Should().Equal("app2", "app3");
    }

    [Fact]
    public void UpdateMode_BuiltInMode_CanUpdatePromptButNotName()
    {
        _service.UpdateMode("E-Mail", "Renamed", "custom email prompt", []);

        var mode = _service.GetModes().First(m => m.Name == "E-Mail");
        mode.SystemPrompt.Should().Be("custom email prompt");
        mode.Name.Should().Be("E-Mail"); // Name unchanged for built-in
    }

    [Fact]
    public void UpdateMode_CustomMode_CanRename()
    {
        _service.AddMode("OldName", "prompt", []);

        _service.UpdateMode("OldName", "NewName", "prompt", []);

        _service.GetModes().Should().Contain(m => m.Name == "NewName");
        _service.GetModes().Should().NotContain(m => m.Name == "OldName");
    }

    // --- ResolveSystemPrompt ---

    [Fact]
    public void ResolveSystemPrompt_DefaultMode_ReturnsNull()
    {
        _service.AutoSwitchEnabled = false;
        _service.SetActiveMode("Default");

        _service.ResolveSystemPrompt(null).Should().BeNull();
    }

    [Fact]
    public void ResolveSystemPrompt_NonDefaultMode_ReturnsPrompt()
    {
        _service.AutoSwitchEnabled = false;
        _service.SetActiveMode("E-Mail");

        var prompt = _service.ResolveSystemPrompt(null);
        prompt.Should().Be(CorrectionModeDefaults.ComposePrompt);
    }

    [Fact]
    public void ResolveSystemPrompt_AutoSwitch_NoMatch_FallsBackToPinned()
    {
        _service.AutoSwitchEnabled = true;
        _service.SetActiveMode("Code");

        var prompt = _service.ResolveSystemPrompt("UnknownApp");
        prompt.Should().Be(CorrectionModeDefaults.CodePrompt);
    }

    [Fact]
    public void ResolveSystemPrompt_AutoSwitch_NoMatch_NoPin_FallsBackToDefault()
    {
        _service.AutoSwitchEnabled = true;
        _service.SetActiveMode(null);

        var prompt = _service.ResolveSystemPrompt("UnknownApp");
        prompt.Should().BeNull(); // Default mode returns null
    }

    [Fact]
    public void ResolveSystemPrompt_NullProcessName_AutoSwitch_UsesPinned()
    {
        _service.AutoSwitchEnabled = true;
        _service.SetActiveMode("Note");

        var prompt = _service.ResolveSystemPrompt(null);
        prompt.Should().Be(CorrectionModeDefaults.NotePrompt);
    }

    // --- ResolveCombinedSystemPrompt ---

    [Fact]
    public void ResolveCombinedSystemPrompt_DelegatesToResolveSystemPrompt()
    {
        _service.AutoSwitchEnabled = true;

        var prompt = _service.ResolveCombinedSystemPrompt("Slack");
        prompt.Should().Be(CorrectionModeDefaults.MessagePrompt);
    }

    // --- Save & Load roundtrip ---

    [Fact]
    public async Task SaveAndLoad_RoundTripsCustomModes()
    {
        var filePath = Path.Combine(_tempDir, "modes-roundtrip.json");
        _service.AddMode("RoundTrip", "test prompt", ["testapp"]);

        // Force save
        await Task.Delay(500);

        // Create second service instance to load from same file
        var optionsMonitor = OptionsHelper.CreateMonitor(_ => { });
        var service2 = new ModeService(NullLogger<ModeService>.Instance, optionsMonitor);
        SetFilePath(service2, filePath);

        // But we need to use the original file path
        SetFilePath(_service, filePath);
        // Re-trigger save by adding another mode
        _service.AddMode("Trigger", "save", []);
        await Task.Delay(500);

        SetFilePath(service2, filePath);
        await service2.LoadAsync();

        var modes = service2.GetModes();
        modes.Should().Contain(m => m.Name == "RoundTrip");
        modes.Should().Contain(m => m.Name == "Trigger");
        service2.Dispose();
    }

    // --- Dispose ---

    [Fact]
    public async Task Dispose_DoesNotThrow()
    {
        var optionsMonitor = OptionsHelper.CreateMonitor(_ => { });
        var service = new ModeService(NullLogger<ModeService>.Instance, optionsMonitor);
        SetFilePath(service, Path.Combine(_tempDir, "dispose-test.json"));
        await service.LoadAsync();

        var act = () =>
        {
            service.Dispose();
            service.Dispose(); // Idempotent
        };

        act.Should().NotThrow();
    }

    // --- SetActiveMode ---

    [Fact]
    public void SetActiveMode_SetsAndReturns()
    {
        _service.SetActiveMode("Code");
        _service.ActiveModeName.Should().Be("Code");

        _service.SetActiveMode(null);
        _service.ActiveModeName.Should().BeNull();
    }

    // --- AutoSwitchEnabled ---

    [Fact]
    public async Task AutoSwitchEnabled_DefaultsFromOptions()
    {
        var optionsMonitor = OptionsHelper.CreateMonitor(o =>
        {
            o.TextCorrection.AutoSwitchMode = false;
        });
        var service = new ModeService(NullLogger<ModeService>.Instance, optionsMonitor);
        SetFilePath(service, Path.Combine(_tempDir, "auto-switch-test.json"));
        await service.LoadAsync();

        service.AutoSwitchEnabled.Should().BeFalse();
        service.Dispose();
    }

    // --- ModesChanged event ---

    [Fact]
    public void AddMode_FiresModesChanged()
    {
        var fired = false;
        _service.ModesChanged += () => fired = true;

        _service.AddMode("TestMode", "prompt", []);

        fired.Should().BeTrue();
    }

    [Fact]
    public void AddMode_DuplicateDoesNotFireModesChanged()
    {
        _service.AddMode("TestMode", "prompt", []);
        var fired = false;
        _service.ModesChanged += () => fired = true;

        _service.AddMode("TestMode", "another prompt", []);

        fired.Should().BeFalse();
    }

    [Fact]
    public void UpdateMode_FiresModesChanged()
    {
        _service.AddMode("TestMode", "prompt", []);
        var fired = false;
        _service.ModesChanged += () => fired = true;

        _service.UpdateMode("TestMode", "TestMode", "updated prompt", []);

        fired.Should().BeTrue();
    }

    [Fact]
    public void UpdateMode_NonExistentDoesNotFireModesChanged()
    {
        var fired = false;
        _service.ModesChanged += () => fired = true;

        _service.UpdateMode("NonExistent", "NewName", "prompt", []);

        fired.Should().BeFalse();
    }

    [Fact]
    public void RemoveMode_FiresModesChanged()
    {
        _service.AddMode("TestMode", "prompt", []);
        var fired = false;
        _service.ModesChanged += () => fired = true;

        _service.RemoveMode("TestMode");

        fired.Should().BeTrue();
    }

    [Fact]
    public void RemoveMode_NonExistentDoesNotFireModesChanged()
    {
        var fired = false;
        _service.ModesChanged += () => fired = true;

        _service.RemoveMode("NonExistent");

        fired.Should().BeFalse();
    }

    [Fact]
    public void RemoveMode_BuiltInDoesNotFireModesChanged()
    {
        var fired = false;
        _service.ModesChanged += () => fired = true;

        _service.RemoveMode("Default");

        fired.Should().BeFalse();
    }

    // --- TargetLanguage ---

    [Fact]
    public void GetModes_TranslateModeHasTargetLanguage()
    {
        var translate = _service.GetModes().First(m => m.Name == "Translate");
        translate.TargetLanguage.Should().Be("English");
        translate.IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public void ResolveTargetLanguage_TranslateMode_ReturnsLanguage()
    {
        _service.AutoSwitchEnabled = false;
        _service.SetActiveMode("Translate");

        _service.ResolveTargetLanguage(null).Should().Be("English");
    }

    [Fact]
    public void ResolveTargetLanguage_NonTranslateMode_ReturnsNull()
    {
        _service.AutoSwitchEnabled = false;
        _service.SetActiveMode("E-Mail");

        _service.ResolveTargetLanguage(null).Should().BeNull();
    }

    [Fact]
    public void ResolveTargetLanguage_DefaultMode_ReturnsNull()
    {
        _service.AutoSwitchEnabled = false;
        _service.SetActiveMode(null);

        _service.ResolveTargetLanguage(null).Should().BeNull();
    }

    [Fact]
    public void AddMode_WithTargetLanguage_StoresIt()
    {
        _service.AddMode("MyTranslate", "translate prompt", [], "French");

        var mode = _service.GetModes().First(m => m.Name == "MyTranslate");
        mode.TargetLanguage.Should().Be("French");
    }

    [Fact]
    public void UpdateMode_CanSetTargetLanguage()
    {
        _service.AddMode("Custom", "prompt", []);
        _service.UpdateMode("Custom", "Custom", "prompt", [], "Spanish");

        var mode = _service.GetModes().First(m => m.Name == "Custom");
        mode.TargetLanguage.Should().Be("Spanish");
    }

    [Fact]
    public void UpdateMode_CanClearTargetLanguage()
    {
        _service.AddMode("Custom", "prompt", [], "German");
        _service.UpdateMode("Custom", "Custom", "prompt", [], null);

        var mode = _service.GetModes().First(m => m.Name == "Custom");
        mode.TargetLanguage.Should().BeNull();
    }

    // --- Prompt content ---

    [Fact]
    public void BuiltInPrompts_ContainFillerWordRemoval()
    {
        var modes = _service.GetModes().Where(m => m.IsBuiltIn && m.Name != "Default");
        foreach (var mode in modes)
        {
            mode.SystemPrompt.Should().Contain("filler words", because: $"{mode.Name} mode should contain filler word removal");
        }
    }

    [Fact]
    public void BuiltInPrompts_ContainSelfCorrectionInstruction()
    {
        var modes = _service.GetModes().Where(m => m.IsBuiltIn && m.Name != "Default");
        foreach (var mode in modes)
        {
            mode.SystemPrompt.Should().Contain("corrects themselves", because: $"{mode.Name} mode should contain self-correction instruction");
        }
    }

    // --- E-Mail mode ---

    [Fact]
    public void EmailMode_ContainsGermanEmailInstructions()
    {
        var email = _service.GetModes().First(m => m.Name == "E-Mail");
        email.SystemPrompt.Should().Contain("German");
        email.SystemPrompt.Should().Contain("greeting");
        email.SystemPrompt.Should().Contain("closing");
    }

    [Fact]
    public void EmailMode_IsBuiltIn()
    {
        var email = _service.GetModes().First(m => m.Name == "E-Mail");
        email.IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public void EmailMode_HasNoAppPatterns()
    {
        var email = _service.GetModes().First(m => m.Name == "E-Mail");
        email.AppPatterns.Should().BeEmpty();
    }

    [Fact]
    public void EmailMode_HasNoTargetLanguage()
    {
        var email = _service.GetModes().First(m => m.Name == "E-Mail");
        email.TargetLanguage.Should().BeNull();
    }

    [Fact]
    public void ResolveSystemPrompt_EmailMode_ReturnsComposePrompt()
    {
        _service.AutoSwitchEnabled = false;
        _service.SetActiveMode("E-Mail");

        var prompt = _service.ResolveSystemPrompt(null);
        prompt.Should().Be(CorrectionModeDefaults.ComposePrompt);
    }

    [Fact]
    public void TranslatePrompt_DoesNotContainNeverTranslate()
    {
        var translate = _service.GetModes().First(m => m.Name == "Translate");
        translate.SystemPrompt.Should().NotContain("NEVER translate");
    }

    // --- Resolve Priority Tests ---

    [Fact]
    public void ResolveMode_AutoSwitchOn_MatchOverridesPinned()
    {
        _service.AutoSwitchEnabled = true;
        _service.SetActiveMode("E-Mail"); // Pinned to E-Mail

        // "Slack" matches Message mode — auto-switch should win over pin
        var prompt = _service.ResolveSystemPrompt("Slack");
        prompt.Should().Be(CorrectionModeDefaults.MessagePrompt);
    }

    [Fact]
    public void ResolveMode_AutoSwitchOff_PinnedOverridesMatch()
    {
        _service.AutoSwitchEnabled = false;
        _service.SetActiveMode("E-Mail"); // Pinned to E-Mail

        // Even though "Slack" would match Message, auto-switch is off → pinned wins
        var prompt = _service.ResolveSystemPrompt("Slack");
        prompt.Should().Be(CorrectionModeDefaults.ComposePrompt);
    }

    [Fact]
    public void ResolveMode_AutoSwitchOn_EmptyProcessName_UsesPinned()
    {
        _service.AutoSwitchEnabled = true;
        _service.SetActiveMode("Code");

        // Empty process name can't match anything → falls back to pinned
        var prompt = _service.ResolveSystemPrompt("");
        prompt.Should().Be(CorrectionModeDefaults.CodePrompt);
    }

    [Fact]
    public void UpdateMode_RenameActivePinnedMode_UpdatesActiveModeName()
    {
        _service.AddMode("MyCustom", "custom prompt", []);
        _service.SetActiveMode("MyCustom");
        _service.ActiveModeName.Should().Be("MyCustom");

        _service.UpdateMode("MyCustom", "Renamed", "custom prompt", []);

        _service.ActiveModeName.Should().Be("Renamed");
    }

    [Fact]
    public void ResolveMode_MessageModePatterns_AllMatch()
    {
        _service.AutoSwitchEnabled = true;

        foreach (var app in new[] { "Slack", "Teams", "Discord", "Telegram", "WhatsApp", "Signal" })
        {
            var prompt = _service.ResolveSystemPrompt(app);
            prompt.Should().Be(CorrectionModeDefaults.MessagePrompt,
                because: $"{app} should match Message mode");
        }
    }

    [Fact]
    public async Task Dispose_FlushesPendingSaves()
    {
        // Add a mode (triggers debounced save)
        _service.AddMode("FlushTest", "test prompt", ["testapp"]);

        // Dispose should flush immediately (no need to wait for debounce)
        _service.Dispose();

        var filePath = Path.Combine(_tempDir, "modes.json");
        File.Exists(filePath).Should().BeTrue("Dispose should flush pending saves to disk");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("FlushTest");
    }
}
