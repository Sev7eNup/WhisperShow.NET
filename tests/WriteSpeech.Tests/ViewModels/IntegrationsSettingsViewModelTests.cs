using System.Text.Json.Nodes;
using FluentAssertions;
using WriteSpeech.App.ViewModels;
using WriteSpeech.App.ViewModels.Settings;
using WriteSpeech.Core.Configuration;

namespace WriteSpeech.Tests.ViewModels;

public class IntegrationsSettingsViewModelTests
{
    private static WriteSpeechOptions DefaultOptions() => new();

    [Fact]
    public void Constructor_InitializesFromOptions()
    {
        var options = new WriteSpeechOptions
        {
            Integration = new IntegrationOptions
            {
                VariableRecognition = false,
                FileTagging = true
            }
        };

        var vm = new IntegrationsSettingsViewModel(() => { }, options);

        vm.VariableRecognition.Should().BeFalse();
        vm.FileTagging.Should().BeTrue();
    }

    [Fact]
    public void Constructor_UsesDefaultValues()
    {
        var vm = new IntegrationsSettingsViewModel(() => { }, DefaultOptions());

        // Defaults from IntegrationOptions: both true
        vm.VariableRecognition.Should().BeTrue();
        vm.FileTagging.Should().BeTrue();
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
    public void WriteSettings_PersistsCorrectly()
    {
        var vm = new IntegrationsSettingsViewModel(() => { }, DefaultOptions());
        vm.VariableRecognition = false;
        vm.FileTagging = true;

        var root = new JsonObject();
        vm.WriteSettings(root);

        var integration = root["Integration"]!.AsObject();
        integration["VariableRecognition"]!.GetValue<bool>().Should().BeFalse();
        integration["FileTagging"]!.GetValue<bool>().Should().BeTrue();
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
