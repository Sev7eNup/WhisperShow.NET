using System.ComponentModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WriteSpeech.App.ViewModels.Settings;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services;
using WriteSpeech.Core.Services.Configuration;
using WriteSpeech.Core.Services.Hotkey;
using WriteSpeech.Core.Services.ModelManagement;
using WriteSpeech.Core.Services.Snippets;
using WriteSpeech.Core.Services.Modes;
using WriteSpeech.Core.Services.Statistics;
using WriteSpeech.Core.Services.TextCorrection;

namespace WriteSpeech.App.ViewModels;

public enum SettingsPage
{
    General,
    System,
    Integrations,
    Models,
    Intelligence,
    Dictionary,
    Snippets,
    Modes,
    Statistics
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsPersistenceService _persistenceService;

    // --- Sub-ViewModels ---
    public GeneralSettingsViewModel General { get; }
    public SystemSettingsViewModel System { get; }
    public TranscriptionSettingsViewModel Transcription { get; }
    public IntegrationsSettingsViewModel Integrations { get; }
    public StatisticsViewModel Statistics { get; }
    public DictionarySnippetsViewModel DictionarySnippets { get; }
    public ModesSettingsViewModel Modes { get; }

    // --- Page navigation ---
    [ObservableProperty]
    private SettingsPage _selectedPage = SettingsPage.General;

    // Version
    public string VersionText => $"WriteSpeech v{GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

    public SettingsViewModel(
        IOptions<WriteSpeechOptions> options,
        IGlobalHotkeyService hotkeyService,
        IDictionaryService dictionaryService,
        ISnippetService snippetService,
        IUsageStatsService statsService,
        IModelManager modelManager,
        ICorrectionModelManager correctionModelManager,
        IParakeetModelManager parakeetModelManager,
        IVadModelManager vadModelManager,
        IModelPreloadService preloadService,
        IAutoStartService autoStartService,
        IDispatcherService dispatcher,
        IModeService modeService,
        ISettingsPersistenceService persistenceService,
        ILogger<SettingsViewModel> logger)
    {
        _persistenceService = persistenceService;

        var opts = options.Value;

        General = new GeneralSettingsViewModel(
            hotkeyService, logger, dispatcher, ScheduleSave, opts);

        System = new SystemSettingsViewModel(
            autoStartService, persistenceService, ScheduleSave, opts, App.RestartApp);

        Transcription = new TranscriptionSettingsViewModel(
            modelManager, correctionModelManager, parakeetModelManager, vadModelManager,
            preloadService, logger, dispatcher, ScheduleSave, opts);

        Integrations = new IntegrationsSettingsViewModel(ScheduleSave, opts);

        Statistics = new StatisticsViewModel(statsService);
        DictionarySnippets = new DictionarySnippetsViewModel(dictionaryService, snippetService, ScheduleSave, opts);
        Modes = new ModesSettingsViewModel(modeService, ScheduleSave, opts);

        Transcription.PropertyChanged += OnTranscriptionPropertyChanged;
    }

    private void OnTranscriptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranscriptionSettingsViewModel.CorrectionProvider))
        {
            var isOff = Transcription.CorrectionProvider == TextCorrectionProvider.Off;
            DictionarySnippets.IsCorrectionOff = isOff;
            Modes.IsCorrectionOff = isOff;
        }
    }

    // --- Navigation ---

    [RelayCommand]
    private void Navigate(SettingsPage page)
    {
        SelectedPage = page;
        if (page == SettingsPage.Statistics)
            Statistics.Refresh();
        else if (page is SettingsPage.Models or SettingsPage.Intelligence)
            Transcription.RefreshModels();
        else if (page == SettingsPage.Dictionary)
            DictionarySnippets.RefreshEntries();
        else if (page == SettingsPage.Modes)
            Modes.RefreshModes();
    }

    // --- Persistence ---

    private void ScheduleSave()
    {
        _persistenceService.ScheduleUpdate(section =>
        {
            General.WriteSettings(section);
            System.WriteSettings(section);
            Transcription.WriteSettings(section);
            Integrations.WriteSettings(section);
            DictionarySnippets.WriteSettings(section);
            Modes.WriteSettings(section);
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
