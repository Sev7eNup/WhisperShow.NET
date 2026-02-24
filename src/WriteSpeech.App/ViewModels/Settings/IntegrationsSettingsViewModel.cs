using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WriteSpeech.Core.Configuration;

namespace WriteSpeech.App.ViewModels.Settings;

public partial class IntegrationsSettingsViewModel : ObservableObject
{
    private readonly Action _scheduleSave;

    [ObservableProperty] private bool _variableRecognition;
    [ObservableProperty] private bool _fileTagging;

    public IntegrationsSettingsViewModel(Action scheduleSave, WriteSpeechOptions options)
    {
        _scheduleSave = scheduleSave;
        _variableRecognition = options.Integration.VariableRecognition;
        _fileTagging = options.Integration.FileTagging;
    }

    [RelayCommand]
    private void ToggleVariableRecognition() => _scheduleSave();

    [RelayCommand]
    private void ToggleFileTagging() => _scheduleSave();

    public void WriteSettings(JsonNode section)
    {
        var integration = SettingsViewModel.EnsureObject(section, "Integration");
        integration["VariableRecognition"] = VariableRecognition;
        integration["FileTagging"] = FileTagging;
    }
}
