using FluentAssertions;
using NSubstitute;
using WriteSpeech.App.ViewModels;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.History;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.ViewModels;

public class HistoryViewModelTests
{
    private readonly ITranscriptionHistoryService _historyService = Substitute.For<ITranscriptionHistoryService>();

    private HistoryViewModel CreateViewModel()
    {
        return new HistoryViewModel(_historyService, new SynchronousDispatcherService());
    }

    private static TranscriptionHistoryEntry MakeEntry(string text) => new()
    {
        Text = text,
        Provider = "OpenAI",
        DurationSeconds = 1.0
    };

    [Fact]
    public void Refresh_PopulatesEntries()
    {
        var entries = new List<TranscriptionHistoryEntry>
        {
            MakeEntry("Hello world"),
            MakeEntry("Good morning")
        };
        _historyService.GetEntries().Returns(entries);

        var vm = CreateViewModel();
        vm.Refresh();

        vm.Entries.Should().HaveCount(2);
        vm.TotalCount.Should().Be(2);
    }

    [Fact]
    public void SearchQuery_FiltersEntries()
    {
        _historyService.GetEntries().Returns(new List<TranscriptionHistoryEntry>
        {
            MakeEntry("Hello world"),
            MakeEntry("Good morning"),
            MakeEntry("Hello again")
        });

        var vm = CreateViewModel();
        vm.Refresh();
        vm.SearchQuery = "Hello";

        vm.Entries.Should().HaveCount(2);
        vm.Entries.Should().AllSatisfy(e => e.Text.Should().Contain("Hello"));
    }

    [Fact]
    public void SearchQuery_CaseInsensitive()
    {
        _historyService.GetEntries().Returns(new List<TranscriptionHistoryEntry>
        {
            MakeEntry("Hello world"),
            MakeEntry("HELLO AGAIN")
        });

        var vm = CreateViewModel();
        vm.Refresh();
        vm.SearchQuery = "hello";

        vm.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void SearchQuery_NoMatches_EmptyEntries()
    {
        _historyService.GetEntries().Returns(new List<TranscriptionHistoryEntry>
        {
            MakeEntry("Hello world")
        });

        var vm = CreateViewModel();
        vm.Refresh();
        vm.SearchQuery = "xyz";

        vm.Entries.Should().BeEmpty();
    }

    [Fact]
    public void SearchQuery_EmptyString_ShowsAllEntries()
    {
        _historyService.GetEntries().Returns(new List<TranscriptionHistoryEntry>
        {
            MakeEntry("Hello world"),
            MakeEntry("Good morning")
        });

        var vm = CreateViewModel();
        vm.Refresh();
        vm.SearchQuery = "Hello";
        vm.Entries.Should().HaveCount(1);

        vm.SearchQuery = "";
        vm.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void EntryCountDisplay_NoFilter_ShowsTotal()
    {
        _historyService.GetEntries().Returns(new List<TranscriptionHistoryEntry>
        {
            MakeEntry("One"),
            MakeEntry("Two"),
            MakeEntry("Three")
        });

        var vm = CreateViewModel();
        vm.Refresh();

        vm.EntryCountDisplay.Should().Be("3 entries");
    }

    [Fact]
    public void EntryCountDisplay_WithFilter_ShowsFilteredOfTotal()
    {
        _historyService.GetEntries().Returns(new List<TranscriptionHistoryEntry>
        {
            MakeEntry("Hello world"),
            MakeEntry("Good morning"),
            MakeEntry("Hello again")
        });

        var vm = CreateViewModel();
        vm.Refresh();
        vm.SearchQuery = "Hello";

        vm.EntryCountDisplay.Should().Be("2 of 3 entries");
    }

    [Fact]
    public void ShowNoResults_WhenFilteredAndEmpty_IsTrue()
    {
        _historyService.GetEntries().Returns(new List<TranscriptionHistoryEntry>
        {
            MakeEntry("Hello world")
        });

        var vm = CreateViewModel();
        vm.Refresh();
        vm.SearchQuery = "xyz";

        vm.ShowNoResults.Should().BeTrue();
    }

    [Fact]
    public void ShowNoResults_WhenNoFilter_IsFalse()
    {
        _historyService.GetEntries().Returns(new List<TranscriptionHistoryEntry>());

        var vm = CreateViewModel();
        vm.Refresh();

        vm.ShowNoResults.Should().BeFalse();
    }

    [Fact]
    public void RemoveEntry_WhileFiltered_RemovesFromBothLists()
    {
        var entry1 = MakeEntry("Hello world");
        var entry2 = MakeEntry("Good morning");
        _historyService.GetEntries().Returns(new List<TranscriptionHistoryEntry> { entry1, entry2 });

        var vm = CreateViewModel();
        vm.Refresh();
        vm.SearchQuery = "Hello";
        vm.Entries.Should().HaveCount(1);

        vm.RemoveEntryCommand.Execute(entry1);

        vm.Entries.Should().BeEmpty();
        vm.TotalCount.Should().Be(1);
        _historyService.Received(1).RemoveEntry(entry1);
    }

    [Fact]
    public void ClearAll_ResetsEverything()
    {
        _historyService.GetEntries().Returns(new List<TranscriptionHistoryEntry>
        {
            MakeEntry("Hello world"),
            MakeEntry("Good morning")
        });

        var vm = CreateViewModel();
        vm.Refresh();
        vm.SearchQuery = "Hello";

        vm.ClearAllCommand.Execute(null);

        vm.Entries.Should().BeEmpty();
        vm.TotalCount.Should().Be(0);
        vm.SearchQuery.Should().BeEmpty();
        _historyService.Received(1).Clear();
    }

    [Fact]
    public void ClearSearch_ResetsSearchQuery()
    {
        _historyService.GetEntries().Returns(new List<TranscriptionHistoryEntry>
        {
            MakeEntry("Hello world")
        });

        var vm = CreateViewModel();
        vm.Refresh();
        vm.SearchQuery = "Hello";

        vm.ClearSearchCommand.Execute(null);

        vm.SearchQuery.Should().BeEmpty();
        vm.Entries.Should().HaveCount(1);
    }

    [Fact]
    public void Refresh_EmptyHistory_ShowsEmptyState()
    {
        _historyService.GetEntries().Returns(new List<TranscriptionHistoryEntry>());

        var vm = CreateViewModel();
        vm.Refresh();

        vm.Entries.Should().BeEmpty();
        vm.TotalCount.Should().Be(0);
        vm.EntryCountDisplay.Should().Be("0 entries");
    }

    [Fact]
    public void EntryCountDisplay_SingleEntry_ShowsCount()
    {
        _historyService.GetEntries().Returns(new List<TranscriptionHistoryEntry>
        {
            MakeEntry("Hello")
        });

        var vm = CreateViewModel();
        vm.Refresh();

        vm.EntryCountDisplay.Should().Be("1 entries");
    }
}
