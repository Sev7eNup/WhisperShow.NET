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

/// <summary>
/// Identifies the available pages in the settings window.
/// </summary>
public enum SettingsPage
{
    /// <summary>Language, microphone, hotkey configuration.</summary>
    General,

    /// <summary>Theme, sound effects, auto-start at login.</summary>
    System,

    /// <summary>IDE variable recognition and file tagging toggles.</summary>
    Integrations,

    /// <summary>Whisper, Parakeet, and correction model download management.</summary>
    Models,

    /// <summary>Text correction provider, API keys, and combined audio model settings.</summary>
    Intelligence,

    /// <summary>Custom word dictionary for improving transcription accuracy.</summary>
    Dictionary,

    /// <summary>Trigger-to-replacement text snippet management.</summary>
    Snippets,

    /// <summary>Context-aware correction modes (Default, E-Mail, Code, etc.) with app-matching patterns.</summary>
    Modes,

    /// <summary>Usage statistics (total recordings, words, duration, etc.).</summary>
    Statistics
}

/// <summary>
/// Top-level coordinator for the settings window. Owns sub-ViewModels for each settings page
/// (General, System, Intelligence, Modes, Integrations, Models, Dictionary, Statistics) and
/// handles page navigation. Delegates persistence to <see cref="ISettingsPersistenceService"/>
/// by collecting all sub-VM mutations into a single JSON write.
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsPersistenceService _persistenceService;

    /// <summary>Language, microphone, and hotkey settings.</summary>
    public GeneralSettingsViewModel General { get; }

    /// <summary>Theme, sound effects, and auto-start settings.</summary>
    public SystemSettingsViewModel System { get; }

    /// <summary>Transcription and text correction provider settings.</summary>
    public TranscriptionSettingsViewModel Transcription { get; }

    /// <summary>IDE integration settings (variable recognition, file tagging).</summary>
    public IntegrationsSettingsViewModel Integrations { get; }

    /// <summary>Usage statistics display.</summary>
    public StatisticsViewModel Statistics { get; }

    /// <summary>Custom dictionary and snippet management.</summary>
    public DictionarySnippetsViewModel DictionarySnippets { get; }

    /// <summary>Correction mode management (built-in and custom modes).</summary>
    public ModesSettingsViewModel Modes { get; }

    // --- Page navigation ---
    [ObservableProperty]
    private SettingsPage _selectedPage = SettingsPage.General;

    /// <summary>Application version string displayed in the settings window footer.</summary>
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
            hotkeyService, logger, dispatcher, ScheduleSave, opts, vadModelManager);

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

    /// <summary>Unsubscribes from sub-ViewModel property change events.</summary>
    public void Dispose()
    {
        Transcription.PropertyChanged -= OnTranscriptionPropertyChanged;
    }
}
