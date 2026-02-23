using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperShow.Core.Services.Snippets;
using WhisperShow.Core.Services.TextCorrection;

namespace WhisperShow.App.ViewModels.Settings;

public partial class DictionarySnippetsViewModel : ObservableObject
{
    private readonly IDictionaryService _dictionaryService;
    private readonly ISnippetService _snippetService;

    public ObservableCollection<string> DictionaryEntries { get; } = [];
    [ObservableProperty] private string _newDictionaryWord = "";

    public ObservableCollection<SnippetEntry> SnippetItems { get; } = [];
    [ObservableProperty] private string _newSnippetTrigger = "";
    [ObservableProperty] private string _newSnippetReplacement = "";
    [ObservableProperty] private bool _isEditingSnippet;
    private SnippetEntry? _editingSnippet;

    public DictionarySnippetsViewModel(IDictionaryService dictionaryService, ISnippetService snippetService)
    {
        _dictionaryService = dictionaryService;
        _snippetService = snippetService;

        LoadDictionaryEntries();
        LoadSnippets();
    }

    private void LoadDictionaryEntries()
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
