using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhisperShow.App.ViewModels.Settings;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Services;
using WhisperShow.Core.Services.Configuration;
using WhisperShow.Core.Services.Hotkey;
using WhisperShow.Core.Services.ModelManagement;
using WhisperShow.Core.Services.Snippets;
using WhisperShow.Core.Services.Statistics;
using WhisperShow.Core.Services.TextCorrection;

namespace WhisperShow.App.ViewModels;

public enum SettingsPage
{
    General,
    System,
    Models,
    Dictionary,
    Snippets,
    Statistics
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly DebouncedSaveHelper _saveHelper;

    // --- Sub-ViewModels ---
    public GeneralSettingsViewModel General { get; }
    public SystemSettingsViewModel System { get; }
    public TranscriptionSettingsViewModel Transcription { get; }
    public StatisticsViewModel Statistics { get; }
    public DictionarySnippetsViewModel DictionarySnippets { get; }

    // --- Page navigation ---
    [ObservableProperty]
    private SettingsPage _selectedPage = SettingsPage.General;

    // Version
    public string VersionText => $"WhisperShow v{GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

    public SettingsViewModel(
        IOptions<WhisperShowOptions> options,
        IGlobalHotkeyService hotkeyService,
        IDictionaryService dictionaryService,
        ISnippetService snippetService,
        IUsageStatsService statsService,
        IModelManager modelManager,
        ICorrectionModelManager correctionModelManager,
        IModelPreloadService preloadService,
        IAutoStartService autoStartService,
        ILogger<SettingsViewModel> logger)
    {
        _logger = logger;
        _saveHelper = new DebouncedSaveHelper(SaveSettingsAsync, logger, 300);

        var opts = options.Value;

        General = new GeneralSettingsViewModel(
            hotkeyService, logger, ScheduleSave,
            opts.Hotkey.Toggle.Modifiers,
            opts.Hotkey.Toggle.Key,
            opts.Hotkey.PushToTalk.Modifiers,
            opts.Hotkey.PushToTalk.Key,
            opts.Audio.DeviceIndex,
            opts.Language);

        System = new SystemSettingsViewModel(
            autoStartService, ScheduleSave,
            opts.App.LaunchAtLogin,
            opts.Overlay.AlwaysVisible,
            opts.Overlay.ShowInTaskbar,
            string.Equals(opts.App.Theme, "Dark", StringComparison.OrdinalIgnoreCase),
            opts.App.SoundEffects,
            opts.Audio.MuteWhileDictating,
            opts.Audio.CompressBeforeUpload,
            opts.Overlay.Scale,
            opts.Overlay.AutoDismissSeconds,
            opts.Audio.MaxRecordingSeconds);

        Transcription = new TranscriptionSettingsViewModel(
            modelManager, correctionModelManager, preloadService, logger, ScheduleSave,
            opts.Provider,
            opts.OpenAI.Endpoint ?? "",
            opts.OpenAI.ApiKey ?? "",
            opts.OpenAI.Model,
            opts.Local.ModelName,
            opts.Local.GpuAcceleration,
            opts.TextCorrection.Provider,
            opts.TextCorrection.Model,
            opts.TextCorrection.LocalGpuAcceleration,
            opts.TextCorrection.LocalModelName,
            opts.TextCorrection.UseCombinedAudioModel,
            opts.TextCorrection.CombinedAudioModel);

        Statistics = new StatisticsViewModel(statsService);
        DictionarySnippets = new DictionarySnippetsViewModel(dictionaryService, snippetService);
    }

    // --- Navigation ---

    [RelayCommand]
    private void Navigate(SettingsPage page)
    {
        SelectedPage = page;
        if (page == SettingsPage.Statistics)
            Statistics.Refresh();
        else if (page == SettingsPage.Models)
            Transcription.RefreshModels();
    }

    // --- Persistence ---

    private void ScheduleSave() => _saveHelper.Schedule();

    private async Task SaveSettingsAsync()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        var json = await File.ReadAllTextAsync(path);
        var doc = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip
        })!;

        var section = doc["WhisperShow"]!;

        General.WriteSettings(section);
        System.WriteSettings(section);
        Transcription.WriteSettings(section);

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(path, doc.ToJsonString(jsonOptions));

        _logger.LogInformation("Settings saved to appsettings.json");
    }
}
