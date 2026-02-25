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
        modes.Should().HaveCount(5);
        modes.Select(m => m.Name).Should().Contain(["Default", "Email", "Message", "Code", "Note"]);
    }

    [Fact]
    public void GetModes_AllBuiltInModesAreMarkedAsBuiltIn()
    {
        var modes = _service.GetModes();
        modes.Where(m => m.IsBuiltIn).Should().HaveCount(5);
    }

    // --- AddMode ---

    [Fact]
    public void AddMode_AddsCustomMode()
    {
        _service.AddMode("MyMode", "My prompt", ["myapp"]);

        var modes = _service.GetModes();
        modes.Should().HaveCount(6);
        var custom = modes.First(m => m.Name == "MyMode");
        custom.IsBuiltIn.Should().BeFalse();
        custom.SystemPrompt.Should().Be("My prompt");
        custom.AppPatterns.Should().Equal("myapp");
    }

    [Fact]
    public void AddMode_DuplicateName_DoesNotAdd()
    {
        _service.AddMode("Email", "duplicate", []);

        _service.GetModes().Should().HaveCount(5);
    }

    [Fact]
    public void AddMode_EmptyName_DoesNotAdd()
    {
        _service.AddMode("", "prompt", []);
        _service.AddMode("  ", "prompt", []);

        _service.GetModes().Should().HaveCount(5);
    }

    [Fact]
    public void AddMode_EmptyPrompt_DoesNotAdd()
    {
        _service.AddMode("Test", "", []);

        _service.GetModes().Should().HaveCount(5);
    }

    // --- RemoveMode ---

    [Fact]
    public void RemoveMode_RemovesCustomMode()
    {
        _service.AddMode("Custom", "prompt", []);
        _service.GetModes().Should().HaveCount(6);

        _service.RemoveMode("Custom");

        _service.GetModes().Should().HaveCount(5);
    }

    [Fact]
    public void RemoveMode_BuiltInMode_DoesNotRemove()
    {
        _service.RemoveMode("Email");

        _service.GetModes().Should().HaveCount(5);
        _service.GetModes().Should().Contain(m => m.Name == "Email");
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
        _service.UpdateMode("Email", "Renamed", "custom email prompt", []);

        var mode = _service.GetModes().First(m => m.Name == "Email");
        mode.SystemPrompt.Should().Be("custom email prompt");
        mode.Name.Should().Be("Email"); // Name unchanged for built-in
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
        _service.SetActiveMode("Email");

        var prompt = _service.ResolveSystemPrompt(null);
        prompt.Should().Be(CorrectionModeDefaults.EmailPrompt);
    }

    [Fact]
    public void ResolveSystemPrompt_AutoSwitch_MatchesProcessName()
    {
        _service.AutoSwitchEnabled = true;

        var prompt = _service.ResolveSystemPrompt("Outlook");
        prompt.Should().Be(CorrectionModeDefaults.EmailPrompt);
    }

    [Fact]
    public void ResolveSystemPrompt_AutoSwitch_CaseInsensitive()
    {
        _service.AutoSwitchEnabled = true;

        var prompt = _service.ResolveSystemPrompt("outlook");
        prompt.Should().Be(CorrectionModeDefaults.EmailPrompt);
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
    public void Dispose_DoesNotThrow()
    {
        var optionsMonitor = OptionsHelper.CreateMonitor(_ => { });
        var service = new ModeService(NullLogger<ModeService>.Instance, optionsMonitor);
        SetFilePath(service, Path.Combine(_tempDir, "dispose-test.json"));
        service.LoadAsync().GetAwaiter().GetResult();

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
    public void AutoSwitchEnabled_DefaultsFromOptions()
    {
        var optionsMonitor = OptionsHelper.CreateMonitor(o =>
        {
            o.TextCorrection.AutoSwitchMode = false;
        });
        var service = new ModeService(NullLogger<ModeService>.Instance, optionsMonitor);
        SetFilePath(service, Path.Combine(_tempDir, "auto-switch-test.json"));
        service.LoadAsync().GetAwaiter().GetResult();

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
}
