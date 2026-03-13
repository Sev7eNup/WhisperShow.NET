using System.Text.Json.Nodes;
using FluentAssertions;
using Voxwright.App.ViewModels;
using Voxwright.App.ViewModels.Settings;
using Voxwright.Core.Configuration;

namespace Voxwright.Tests.ViewModels;

public class IntegrationsSettingsViewModelTests
{
    private static VoxwrightOptions DefaultOptions() => new();

    [Fact]
    public void Constructor_InitializesFromOptions()
    {
        var options = new VoxwrightOptions
        {
            Integration = new IntegrationOptions
            {
                VariableRecognition = false,
                FileTagging = true,
                IncludeForLocalModels = true
            }
        };

        var vm = new IntegrationsSettingsViewModel(() => { }, options);

        vm.VariableRecognition.Should().BeFalse();
        vm.FileTagging.Should().BeTrue();
        vm.IncludeForLocalModels.Should().BeTrue();
    }

    [Fact]
    public void Constructor_UsesDefaultValues()
    {
        var vm = new IntegrationsSettingsViewModel(() => { }, DefaultOptions());

        // Defaults from IntegrationOptions: both true, IncludeForLocalModels false
        vm.VariableRecognition.Should().BeTrue();
        vm.FileTagging.Should().BeTrue();
        vm.IncludeForLocalModels.Should().BeFalse();
    }

    [Fact]
    public void ToggleVariableRecognitionCommand_SchedulesSave()
    {
        var saveCount = 0;
        var vm = new IntegrationsSettingsViewModel(() => saveCount++, DefaultOptions());

        vm.ToggleVariableRecognitionCommand.Execute(null);

        saveCount.Should().Be(1);
    }

    [Fact]
    public void ToggleFileTaggingCommand_SchedulesSave()
    {
        var saveCount = 0;
        var vm = new IntegrationsSettingsViewModel(() => saveCount++, DefaultOptions());

        vm.ToggleFileTaggingCommand.Execute(null);

        saveCount.Should().Be(1);
    }

    [Fact]
    public void ToggleIncludeForLocalModelsCommand_SchedulesSave()
    {
        var saveCount = 0;
        var vm = new IntegrationsSettingsViewModel(() => saveCount++, DefaultOptions());

        vm.ToggleIncludeForLocalModelsCommand.Execute(null);

        saveCount.Should().Be(1);
    }

    [Fact]
    public void WriteSettings_PersistsCorrectly()
    {
        var options = new VoxwrightOptions
        {
            Integration = new IntegrationOptions { IncludeForLocalModels = true }
        };
        var vm = new IntegrationsSettingsViewModel(() => { }, options);
        vm.VariableRecognition = false;
        vm.FileTagging = true;

        var root = new JsonObject();
        vm.WriteSettings(root);

        var integration = root["Integration"]!.AsObject();
        integration["VariableRecognition"]!.GetValue<bool>().Should().BeFalse();
        integration["FileTagging"]!.GetValue<bool>().Should().BeTrue();
        integration["IncludeForLocalModels"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void WriteSettings_CreatesSectionIfMissing()
    {
        var vm = new IntegrationsSettingsViewModel(() => { }, DefaultOptions());

        var root = new JsonObject();
        vm.WriteSettings(root);

        root["Integration"].Should().NotBeNull();
    }
}
