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
    private readonly ISettingsPersistenceService _persistenceService;

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
        IDispatcherService dispatcher,
        ISettingsPersistenceService persistenceService,
        ILogger<SettingsViewModel> logger)
    {
        _persistenceService = persistenceService;

        var opts = options.Value;

        General = new GeneralSettingsViewModel(
            hotkeyService, logger, dispatcher, ScheduleSave, opts);

        System = new SystemSettingsViewModel(
            autoStartService, ScheduleSave, opts);

        Transcription = new TranscriptionSettingsViewModel(
            modelManager, correctionModelManager, preloadService, logger, dispatcher, ScheduleSave, opts);

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

    private void ScheduleSave()
    {
        _persistenceService.ScheduleUpdate(section =>
        {
            General.WriteSettings(section);
            System.WriteSettings(section);
            Transcription.WriteSettings(section);
        });
    }

    /// <summary>
    /// Ensures a child JsonObject exists at the given key, creating it if missing.
    /// Used by sub-VM WriteSettings methods for defensive null-safety.
    /// </summary>
    internal static JsonObject EnsureObject(JsonNode parent, string key)
    {
        if (parent[key] is not JsonObject obj)
        {
            obj = new JsonObject();
            parent[key] = obj;
        }
        return obj;
    }
}
