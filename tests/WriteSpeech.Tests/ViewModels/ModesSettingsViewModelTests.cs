using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WriteSpeech.App.ViewModels.Settings;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.Modes;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.ViewModels;

public class ModesSettingsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ModeService _modeService;

    public ModesSettingsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"writespeech-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var optionsMonitor = OptionsHelper.CreateMonitor(_ => { });
        _modeService = new ModeService(NullLogger<ModeService>.Instance, optionsMonitor);
        SetFilePath(_modeService, Path.Combine(_tempDir, "modes.json"));
        _modeService.LoadAsync().GetAwaiter().GetResult();
    }

    private static void SetFilePath(ModeService service, string path)
    {
        var field = typeof(ModeService).GetField("_filePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(service, path);
    }

    public void Dispose()
    {
        _modeService.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private ModesSettingsViewModel CreateViewModel(Action<WriteSpeechOptions>? configure = null)
    {
        var options = new WriteSpeechOptions();
        configure?.Invoke(options);
        return new(_modeService, () => { }, options);
    }

    [Fact]
    public void Constructor_LoadsBuiltInModes()
    {
        var vm = CreateViewModel();

        vm.Modes.Should().HaveCount(6);
        vm.Modes.Select(m => m.Name).Should().Contain(["Default", "E-Mail", "Message", "Code", "Note", "Translate"]);
    }

    [Fact]
    public void Constructor_SetsAutoSwitchFromService()
    {
        var vm = CreateViewModel();
        vm.AutoSwitchEnabled.Should().BeTrue();
    }

    [Fact]
    public void SaveModeCommand_AddsCustomMode()
    {
        var vm = CreateViewModel();
        vm.NewModeName = "Custom";
        vm.NewModePrompt = "Custom prompt";
        vm.NewModeAppPatterns = "myapp, otherapp";

        vm.SaveModeCommand.Execute(null);

        vm.Modes.Should().HaveCount(7);
        var custom = vm.Modes.First(m => m.Name == "Custom");
        custom.IsBuiltIn.Should().BeFalse();
        custom.AppPatterns.Should().Be("myapp, otherapp");
    }

    [Fact]
    public void SaveModeCommand_ClearsEditor()
    {
        var vm = CreateViewModel();
        vm.NewModeName = "Custom";
        vm.NewModePrompt = "Custom prompt";
        vm.NewModeAppPatterns = "myapp";

        vm.SaveModeCommand.Execute(null);

        vm.NewModeName.Should().BeEmpty();
        vm.NewModePrompt.Should().BeEmpty();
        vm.NewModeAppPatterns.Should().BeEmpty();
        vm.IsEditing.Should().BeFalse();
    }

    [Fact]
    public void RemoveModeCommand_RemovesCustomMode()
    {
        var vm = CreateViewModel();
        vm.NewModeName = "Custom";
        vm.NewModePrompt = "prompt";
        vm.SaveModeCommand.Execute(null);
        vm.Modes.Should().HaveCount(7);

        var custom = vm.Modes.First(m => m.Name == "Custom");
        vm.RemoveModeCommand.Execute(custom);

        vm.Modes.Should().HaveCount(6);
    }

    [Fact]
    public void RemoveModeCommand_BuiltInMode_DoesNotRemove()
    {
        var vm = CreateViewModel();

        var email = vm.Modes.First(m => m.Name == "E-Mail");
        vm.RemoveModeCommand.Execute(email);

        vm.Modes.Should().HaveCount(6);
    }

    [Fact]
    public void EditModeCommand_PopulatesEditor()
    {
        var vm = CreateViewModel();
        _modeService.AddMode("Custom", "my prompt", ["app1", "app2"]);
        vm.RefreshModes();

        var custom = vm.Modes.First(m => m.Name == "Custom");
        vm.EditModeCommand.Execute(custom);

        vm.NewModeName.Should().Be("Custom");
        vm.NewModePrompt.Should().Be("my prompt");
        vm.NewModeAppPatterns.Should().Be("app1, app2");
        vm.IsEditing.Should().BeTrue();
        vm.EditingOriginalName.Should().Be("Custom");
    }

    [Fact]
    public void CancelEditCommand_ClearsEditor()
    {
        var vm = CreateViewModel();
        vm.NewModeName = "test";
        vm.NewModePrompt = "prompt";
        vm.IsEditing = true;

        vm.CancelEditCommand.Execute(null);

        vm.NewModeName.Should().BeEmpty();
        vm.NewModePrompt.Should().BeEmpty();
        vm.IsEditing.Should().BeFalse();
    }

    [Fact]
    public void ToggleAutoSwitchCommand_UpdatesService()
    {
        var vm = CreateViewModel();
        vm.AutoSwitchEnabled.Should().BeTrue();

        vm.AutoSwitchEnabled = false;
        vm.ToggleAutoSwitchCommand.Execute(null);

        _modeService.AutoSwitchEnabled.Should().BeFalse();
    }

    // --- TargetLanguage ---

    [Fact]
    public void Constructor_TranslateModeHasTargetLanguage()
    {
        var vm = CreateViewModel();
        var translate = vm.Modes.First(m => m.Name == "Translate");
        translate.TargetLanguage.Should().Be("English");
    }

    [Fact]
    public void EditModeCommand_PopulatesTargetLanguage()
    {
        var vm = CreateViewModel();
        var translate = vm.Modes.First(m => m.Name == "Translate");
        vm.EditModeCommand.Execute(translate);

        vm.NewModeTargetLanguage.Should().Be("English");
    }

    [Fact]
    public void SaveModeCommand_WithTargetLanguage_PassesToService()
    {
        var vm = CreateViewModel();
        vm.NewModeName = "MyTranslate";
        vm.NewModePrompt = "translate prompt";
        vm.NewModeTargetLanguage = "French";

        vm.SaveModeCommand.Execute(null);

        var mode = _modeService.GetModes().First(m => m.Name == "MyTranslate");
        mode.TargetLanguage.Should().Be("French");
    }

    [Fact]
    public void SaveModeCommand_EmptyTargetLanguage_StoresNull()
    {
        var vm = CreateViewModel();
        vm.NewModeName = "Custom";
        vm.NewModePrompt = "prompt";
        vm.NewModeTargetLanguage = "";

        vm.SaveModeCommand.Execute(null);

        var mode = _modeService.GetModes().First(m => m.Name == "Custom");
        mode.TargetLanguage.Should().BeNull();
    }

    [Fact]
    public void CancelEditCommand_ClearsTargetLanguage()
    {
        var vm = CreateViewModel();
        vm.NewModeTargetLanguage = "German";

        vm.CancelEditCommand.Execute(null);

        vm.NewModeTargetLanguage.Should().BeEmpty();
    }

    // --- IsCorrectionOff ---

    [Fact]
    public void Constructor_CorrectionOff_SetsIsCorrectionOffTrue()
    {
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Off);
        vm.IsCorrectionOff.Should().BeTrue();
    }

    [Fact]
    public void Constructor_CorrectionEnabled_SetsIsCorrectionOffFalse()
    {
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Anthropic);
        vm.IsCorrectionOff.Should().BeFalse();
    }

    [Fact]
    public void IsCorrectionOff_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        using var monitor = vm.Monitor();

        vm.IsCorrectionOff = false;

        monitor.Should().RaisePropertyChangeFor(x => x.IsCorrectionOff);
    }

    // --- Validation Edge Cases ---

    [Fact]
    public void SaveModeCommand_DuplicateName_DoesNotAdd()
    {
        var vm = CreateViewModel();
        vm.NewModeName = "E-Mail"; // Built-in name
        vm.NewModePrompt = "duplicate prompt";

        vm.SaveModeCommand.Execute(null);

        vm.Modes.Should().HaveCount(6);
    }

    [Fact]
    public void SaveModeCommand_EmptyName_DoesNotAdd()
    {
        var vm = CreateViewModel();
        vm.NewModeName = "";
        vm.NewModePrompt = "some prompt";

        vm.SaveModeCommand.Execute(null);

        vm.Modes.Should().HaveCount(6);
    }

    [Fact]
    public void SaveModeCommand_EmptyPrompt_DoesNotAdd()
    {
        var vm = CreateViewModel();
        vm.NewModeName = "NewMode";
        vm.NewModePrompt = "";

        vm.SaveModeCommand.Execute(null);

        vm.Modes.Should().HaveCount(6);
    }

    [Fact]
    public void SaveModeCommand_EditingBuiltInMode_PreservesName()
    {
        var vm = CreateViewModel();
        var email = vm.Modes.First(m => m.Name == "E-Mail");
        vm.EditModeCommand.Execute(email);

        vm.NewModeName = "Renamed E-Mail"; // Try to rename built-in
        vm.NewModePrompt = "updated prompt";
        vm.SaveModeCommand.Execute(null);

        // Built-in mode name should be preserved
        vm.Modes.Should().Contain(m => m.Name == "E-Mail");
        var updated = vm.Modes.First(m => m.Name == "E-Mail");
        updated.SystemPrompt.Should().Be("updated prompt");
    }
}
