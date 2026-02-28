using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.Modes;

namespace WriteSpeech.App.ViewModels.Settings;

public partial class ModesSettingsViewModel : ObservableObject
{
    private readonly IModeService _modeService;
    private readonly Action _scheduleSave;

    [ObservableProperty] private bool _isCorrectionOff;
    [ObservableProperty] private bool _autoSwitchEnabled;
    [ObservableProperty] private ObservableCollection<ModeItem> _modes = [];
    [ObservableProperty] private string _newModeName = "";
    [ObservableProperty] private string _newModePrompt = "";
    [ObservableProperty] private string _newModeAppPatterns = "";
    [ObservableProperty] private string _newModeTargetLanguage = "";
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string? _editingOriginalName;

    public ModesSettingsViewModel(IModeService modeService, Action scheduleSave, WriteSpeechOptions options)
    {
        _modeService = modeService;
        _scheduleSave = scheduleSave;
        _isCorrectionOff = options.TextCorrection.Provider == TextCorrectionProvider.Off;
        _autoSwitchEnabled = modeService.AutoSwitchEnabled;
        RefreshModes();
    }

    public void RefreshModes()
    {
        var modes = _modeService.GetModes();
        var active = _modeService.ActiveModeName;
        Modes = new ObservableCollection<ModeItem>(
            modes.Select(m => new ModeItem(
                m.Name,
                m.SystemPrompt,
                string.Join(", ", m.AppPatterns),
                m.IsBuiltIn,
                !AutoSwitchEnabled && m.Name.Equals(active, StringComparison.OrdinalIgnoreCase),
                m.TargetLanguage)));
        AutoSwitchEnabled = _modeService.AutoSwitchEnabled;
    }

    [RelayCommand]
    private void ToggleAutoSwitch()
    {
        _modeService.AutoSwitchEnabled = AutoSwitchEnabled;
        if (AutoSwitchEnabled)
            _modeService.SetActiveMode(null);
        _scheduleSave();
        RefreshModes();
    }

    [RelayCommand]
    private void SaveMode()
    {
        if (string.IsNullOrWhiteSpace(NewModeName) || string.IsNullOrWhiteSpace(NewModePrompt))
            return;

        var patterns = ParseAppPatterns(NewModeAppPatterns);
        var targetLang = string.IsNullOrWhiteSpace(NewModeTargetLanguage) ? null : NewModeTargetLanguage.Trim();

        if (IsEditing && EditingOriginalName is not null)
        {
            _modeService.UpdateMode(EditingOriginalName, NewModeName, NewModePrompt, patterns, targetLang);
        }
        else
        {
            _modeService.AddMode(NewModeName, NewModePrompt, patterns, targetLang);
        }

        ClearEditor();
        RefreshModes();
    }

    [RelayCommand]
    private void EditMode(ModeItem? item)
    {
        if (item is null) return;
        NewModeName = item.Name;
        NewModePrompt = item.SystemPrompt;
        NewModeAppPatterns = item.AppPatterns;
        NewModeTargetLanguage = item.TargetLanguage ?? "";
        IsEditing = true;
        EditingOriginalName = item.Name;
    }

    [RelayCommand]
    private void RemoveMode(ModeItem? item)
    {
        if (item is null || item.IsBuiltIn) return;
        _modeService.RemoveMode(item.Name);
        RefreshModes();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        ClearEditor();
    }

    public void WriteSettings(JsonNode section)
    {
        var tc = SettingsViewModel.EnsureObject(section, "TextCorrection");
        tc["AutoSwitchMode"] = AutoSwitchEnabled;
        tc["ActiveMode"] = _modeService.ActiveModeName;
    }

    private void ClearEditor()
    {
        NewModeName = "";
        NewModePrompt = "";
        NewModeAppPatterns = "";
        NewModeTargetLanguage = "";
        IsEditing = false;
        EditingOriginalName = null;
    }

    private static IReadOnlyList<string> ParseAppPatterns(string? input) =>
        string.IsNullOrWhiteSpace(input)
            ? []
            : input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Where(s => s.Length > 0).ToList();
}

public record ModeItem(string Name, string SystemPrompt, string AppPatterns, bool IsBuiltIn, bool IsActive, string? TargetLanguage = null);
