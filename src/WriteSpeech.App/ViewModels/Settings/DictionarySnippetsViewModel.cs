using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.Snippets;
using WriteSpeech.Core.Services.TextCorrection;

namespace WriteSpeech.App.ViewModels.Settings;

/// <summary>
/// ViewModel for the dictionary and snippets settings page.
/// Manages two features: (1) a custom word dictionary whose entries are injected into AI correction prompts
/// to improve recognition of proper nouns, brand names, and technical terms; and (2) text snippets that define
/// trigger-to-replacement substitutions applied after transcription (e.g., "addr" becomes a full mailing address).
/// Both collections are persisted to JSON files in the user's AppData folder.
/// </summary>
public partial class DictionarySnippetsViewModel : ObservableObject
{
    private readonly IDictionaryService _dictionaryService;
    private readonly ISnippetService _snippetService;
    private readonly Action _scheduleSave;

    // --- Dictionary settings ---
    [ObservableProperty] private bool _isCorrectionOff;
    [ObservableProperty] private bool _autoAddToDictionary = true;

    /// <summary>The current list of custom dictionary words that are injected into AI correction prompts for better recognition.</summary>
    public ObservableCollection<string> DictionaryEntries { get; } = [];
    [ObservableProperty] private string _newDictionaryWord = "";

    /// <summary>The current list of text snippets, each mapping a trigger word to its replacement text.</summary>
    public ObservableCollection<SnippetEntry> SnippetItems { get; } = [];
    [ObservableProperty] private string _newSnippetTrigger = "";
    [ObservableProperty] private string _newSnippetReplacement = "";
    [ObservableProperty] private bool _isEditingSnippet;
    private SnippetEntry? _editingSnippet;

    public DictionarySnippetsViewModel(
        IDictionaryService dictionaryService,
        ISnippetService snippetService,
        Action scheduleSave,
        WriteSpeechOptions options)
    {
        _dictionaryService = dictionaryService;
        _snippetService = snippetService;
        _scheduleSave = scheduleSave;

        _isCorrectionOff = options.TextCorrection.Provider == TextCorrectionProvider.Off;
        _autoAddToDictionary = options.TextCorrection.AutoAddToDictionary;

        RefreshEntries();
        LoadSnippets();
    }

    [RelayCommand]
    private void ToggleAutoAddToDictionary() => _scheduleSave();

    /// <summary>Writes the auto-add-to-dictionary setting into the given JSON configuration node for persistence.</summary>
    public void WriteSettings(JsonNode section)
    {
        var correction = SettingsViewModel.EnsureObject(section, "TextCorrection");
        correction["AutoAddToDictionary"] = AutoAddToDictionary;
    }

    /// <summary>Reloads the dictionary word list from the dictionary service, replacing the current entries.</summary>
    public void RefreshEntries()
    {
        DictionaryEntries.Clear();
        foreach (var entry in _dictionaryService.GetEntries())
            DictionaryEntries.Add(entry);
    }

    [RelayCommand]
    private void AddDictionaryEntry()
    {
        if (string.IsNullOrWhiteSpace(NewDictionaryWord)) return;
        var word = NewDictionaryWord.Trim();
        _dictionaryService.AddEntry(word);
        if (!DictionaryEntries.Contains(word, StringComparer.OrdinalIgnoreCase))
            DictionaryEntries.Add(word);
        NewDictionaryWord = "";
    }

    [RelayCommand]
    private void RemoveDictionaryEntry(string word)
    {
        _dictionaryService.RemoveEntry(word);
        DictionaryEntries.Remove(word);
    }

    private void LoadSnippets()
    {
        SnippetItems.Clear();
        foreach (var entry in _snippetService.GetSnippets())
            SnippetItems.Add(entry);
    }

    [RelayCommand]
    private void SaveSnippet()
    {
        if (string.IsNullOrWhiteSpace(NewSnippetTrigger) || string.IsNullOrWhiteSpace(NewSnippetReplacement)) return;
        var trigger = NewSnippetTrigger.Trim();
        var replacement = NewSnippetReplacement.Trim();

        if (IsEditingSnippet && _editingSnippet is not null)
        {
            // Update existing snippet
            _snippetService.UpdateSnippet(_editingSnippet.Trigger, trigger, replacement);
            var index = SnippetItems.IndexOf(_editingSnippet);
            if (index >= 0)
                SnippetItems[index] = new SnippetEntry(trigger, replacement);
            _editingSnippet = null;
            IsEditingSnippet = false;
        }
        else
        {
            // Add new snippet
            _snippetService.AddSnippet(trigger, replacement);
            if (!SnippetItems.Any(s => s.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase)))
                SnippetItems.Add(new SnippetEntry(trigger, replacement));
        }

        NewSnippetTrigger = "";
        NewSnippetReplacement = "";
    }

    [RelayCommand]
    private void EditSnippet(SnippetEntry snippet)
    {
        _editingSnippet = snippet;
        IsEditingSnippet = true;
        NewSnippetTrigger = snippet.Trigger;
        NewSnippetReplacement = snippet.Replacement;
    }

    [RelayCommand]
    private void CancelEditSnippet()
    {
        _editingSnippet = null;
        IsEditingSnippet = false;
        NewSnippetTrigger = "";
        NewSnippetReplacement = "";
    }

    [RelayCommand]
    private void RemoveSnippet(SnippetEntry snippet)
    {
        _snippetService.RemoveSnippet(snippet.Trigger);
        SnippetItems.Remove(snippet);

        // If we were editing the removed snippet, exit edit mode
        if (_editingSnippet == snippet)
            CancelEditSnippet();
    }
}
