using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Voxwright.Core.Models;
using Voxwright.Core.Services;
using Voxwright.Core.Services.History;
namespace Voxwright.App.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly ITranscriptionHistoryService _historyService;
    private readonly IDispatcherService _dispatcher;
    private List<TranscriptionHistoryEntry> _allEntries = [];

    public ObservableCollection<TranscriptionHistoryEntry> Entries { get; } = [];

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private int _totalCount;

    public string EntryCountDisplay =>
        string.IsNullOrWhiteSpace(SearchQuery) || Entries.Count == TotalCount
            ? $"{TotalCount} entries"
            : $"{Entries.Count} of {TotalCount} entries";

    public bool ShowNoResults => Entries.Count == 0 && !string.IsNullOrWhiteSpace(SearchQuery);

    public HistoryViewModel(
        ITranscriptionHistoryService historyService,
        IDispatcherService dispatcher)
    {
        _historyService = historyService;
        _dispatcher = dispatcher;
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    public void Refresh()
    {
        _allEntries = _historyService.GetEntries().ToList();
        TotalCount = _allEntries.Count;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Entries.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchQuery)
            ? _allEntries
            : _allEntries.Where(e => e.Text.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        foreach (var entry in filtered)
            Entries.Add(entry);

        OnPropertyChanged(nameof(EntryCountDisplay));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    [RelayCommand]
    private void CopyEntry(TranscriptionHistoryEntry entry)
    {
        _dispatcher.Invoke(() => System.Windows.Clipboard.SetText(entry.Text));
    }

    [RelayCommand]
    private void RemoveEntry(TranscriptionHistoryEntry entry)
    {
        _historyService.RemoveEntry(entry);
        _allEntries.Remove(entry);
        Entries.Remove(entry);
        TotalCount = _allEntries.Count;
        OnPropertyChanged(nameof(EntryCountDisplay));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    [RelayCommand]
    private void ClearAll()
    {
        _historyService.Clear();
        _allEntries.Clear();
        Entries.Clear();
        SearchQuery = "";
        TotalCount = 0;
        OnPropertyChanged(nameof(EntryCountDisplay));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = "";
    }
}
