using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Voxwright.Core.Configuration;

namespace Voxwright.App.ViewModels.Settings;

public partial class IntegrationsSettingsViewModel : ObservableObject
{
    private readonly Action _scheduleSave;

    [ObservableProperty] private bool _variableRecognition;
    [ObservableProperty] private bool _fileTagging;
    [ObservableProperty] private bool _includeForLocalModels;

    public IntegrationsSettingsViewModel(Action scheduleSave, VoxwrightOptions options)
    {
        _scheduleSave = scheduleSave;
        _variableRecognition = options.Integration.VariableRecognition;
        _fileTagging = options.Integration.FileTagging;
        _includeForLocalModels = options.Integration.IncludeForLocalModels;
    }

    [RelayCommand]
    private void ToggleVariableRecognition() => _scheduleSave();

    [RelayCommand]
    private void ToggleFileTagging() => _scheduleSave();

    [RelayCommand]
    private void ToggleIncludeForLocalModels() => _scheduleSave();

    public void WriteSettings(JsonNode section)
    {
        var integration = SettingsViewModel.EnsureObject(section, "Integration");
        integration["VariableRecognition"] = VariableRecognition;
        integration["FileTagging"] = FileTagging;
        integration["IncludeForLocalModels"] = IncludeForLocalModels;
    }
}
