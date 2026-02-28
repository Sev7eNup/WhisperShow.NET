using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using WriteSpeech.App.ViewModels.Settings;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Models;
using WriteSpeech.Core.Services.Snippets;
using WriteSpeech.Core.Services.TextCorrection;

namespace WriteSpeech.Tests.ViewModels;

public class DictionarySnippetsViewModelTests
{
    private readonly IDictionaryService _dictionaryService;
    private readonly ISnippetService _snippetService;
    private bool _saveCalled;

    public DictionarySnippetsViewModelTests()
    {
        _dictionaryService = Substitute.For<IDictionaryService>();
        _snippetService = Substitute.For<ISnippetService>();

        // Default: return empty lists
        _dictionaryService.GetEntries().Returns([]);
        _snippetService.GetSnippets().Returns([]);
    }

    private DictionarySnippetsViewModel CreateViewModel(Action<WriteSpeechOptions>? configure = null)
    {
        _saveCalled = false;
        var options = new WriteSpeechOptions();
        configure?.Invoke(options);
        return new(_dictionaryService, _snippetService, () => _saveCalled = true, options);
    }

    // --- Dictionary ---

    [Fact]
    public void Constructor_LoadsEntriesFromService()
    {
        _dictionaryService.GetEntries().Returns(new List<string> { "Whisper", "GPT", "CUDA" });

        var vm = CreateViewModel();

        vm.DictionaryEntries.Should().Equal("Whisper", "GPT", "CUDA");
    }

    [Fact]
    public void AddDictionaryEntry_AddsToService_AndClearsInput()
    {
        var vm = CreateViewModel();
        vm.NewDictionaryWord = "Serilog";

        vm.AddDictionaryEntryCommand.Execute(null);

        _dictionaryService.Received(1).AddEntry("Serilog");
        vm.DictionaryEntries.Should().Contain("Serilog");
        vm.NewDictionaryWord.Should().BeEmpty();
    }

    [Fact]
    public void AddDictionaryEntry_SkipsEmpty()
    {
        var vm = CreateViewModel();
        vm.NewDictionaryWord = "   ";

        vm.AddDictionaryEntryCommand.Execute(null);

        _dictionaryService.DidNotReceive().AddEntry(Arg.Any<string>());
        vm.DictionaryEntries.Should().BeEmpty();
    }

    [Fact]
    public void RefreshEntries_PicksUpExternallyAddedEntries()
    {
        _dictionaryService.GetEntries().Returns(new List<string> { "Alpha" });
        var vm = CreateViewModel();
        vm.DictionaryEntries.Should().ContainSingle("Alpha");

        // Simulate external addition (e.g. auto-add from VocabResponseParser)
        _dictionaryService.GetEntries().Returns(new List<string> { "Alpha", "Beta", "Gamma" });

        vm.RefreshEntries();

        vm.DictionaryEntries.Should().Equal("Alpha", "Beta", "Gamma");
    }

    [Fact]
    public void RemoveDictionaryEntry_RemovesFromServiceAndCollection()
    {
        _dictionaryService.GetEntries().Returns(new List<string> { "Alpha", "Beta" });
        var vm = CreateViewModel();

        vm.RemoveDictionaryEntryCommand.Execute("Alpha");

        _dictionaryService.Received(1).RemoveEntry("Alpha");
        vm.DictionaryEntries.Should().NotContain("Alpha");
        vm.DictionaryEntries.Should().Contain("Beta");
    }

    // --- Snippets ---

    [Fact]
    public void Constructor_LoadsSnippetsFromService()
    {
        _snippetService.GetSnippets().Returns(new List<SnippetEntry>
        {
            new("brb", "be right back"),
            new("omw", "on my way")
        });

        var vm = CreateViewModel();

        vm.SnippetItems.Should().HaveCount(2);
        vm.SnippetItems[0].Trigger.Should().Be("brb");
        vm.SnippetItems[0].Replacement.Should().Be("be right back");
        vm.SnippetItems[1].Trigger.Should().Be("omw");
    }

    [Fact]
    public void SaveSnippet_InAddMode_AddsToService_AndClearsInputs()
    {
        var vm = CreateViewModel();
        vm.NewSnippetTrigger = "addr";
        vm.NewSnippetReplacement = "123 Main Street";

        vm.SaveSnippetCommand.Execute(null);

        _snippetService.Received(1).AddSnippet("addr", "123 Main Street");
        vm.SnippetItems.Should().ContainSingle(s => s.Trigger == "addr" && s.Replacement == "123 Main Street");
        vm.NewSnippetTrigger.Should().BeEmpty();
        vm.NewSnippetReplacement.Should().BeEmpty();
    }

    [Fact]
    public void SaveSnippet_SkipsWhenTriggerEmpty()
    {
        var vm = CreateViewModel();
        vm.NewSnippetTrigger = "";
        vm.NewSnippetReplacement = "some replacement";

        vm.SaveSnippetCommand.Execute(null);

        _snippetService.DidNotReceive().AddSnippet(Arg.Any<string>(), Arg.Any<string>());
        vm.SnippetItems.Should().BeEmpty();
    }

    [Fact]
    public void RemoveSnippet_RemovesFromServiceAndCollection()
    {
        var entries = new List<SnippetEntry>
        {
            new("brb", "be right back"),
            new("omw", "on my way")
        };
        _snippetService.GetSnippets().Returns(entries);
        var vm = CreateViewModel();

        var toRemove = vm.SnippetItems.First(s => s.Trigger == "brb");
        vm.RemoveSnippetCommand.Execute(toRemove);

        _snippetService.Received(1).RemoveSnippet("brb");
        vm.SnippetItems.Should().NotContain(s => s.Trigger == "brb");
        vm.SnippetItems.Should().ContainSingle(s => s.Trigger == "omw");
    }

    // --- Edit Snippets ---

    [Fact]
    public void EditSnippet_FillsInputsAndEntersEditMode()
    {
        _snippetService.GetSnippets().Returns(new List<SnippetEntry>
        {
            new("brb", "be right back")
        });
        var vm = CreateViewModel();

        vm.EditSnippetCommand.Execute(vm.SnippetItems[0]);

        vm.IsEditingSnippet.Should().BeTrue();
        vm.NewSnippetTrigger.Should().Be("brb");
        vm.NewSnippetReplacement.Should().Be("be right back");
    }

    [Fact]
    public void SaveSnippet_InEditMode_UpdatesServiceAndCollection()
    {
        _snippetService.GetSnippets().Returns(new List<SnippetEntry>
        {
            new("brb", "be right back"),
            new("omw", "on my way")
        });
        var vm = CreateViewModel();

        // Enter edit mode for first snippet
        vm.EditSnippetCommand.Execute(vm.SnippetItems[0]);

        // Change the values
        vm.NewSnippetTrigger = "bbiab";
        vm.NewSnippetReplacement = "be back in a bit";

        vm.SaveSnippetCommand.Execute(null);

        _snippetService.Received(1).UpdateSnippet("brb", "bbiab", "be back in a bit");
        vm.SnippetItems[0].Trigger.Should().Be("bbiab");
        vm.SnippetItems[0].Replacement.Should().Be("be back in a bit");
        vm.IsEditingSnippet.Should().BeFalse();
        vm.NewSnippetTrigger.Should().BeEmpty();
        vm.NewSnippetReplacement.Should().BeEmpty();
    }

    [Fact]
    public void CancelEditSnippet_ClearsInputsAndExitsEditMode()
    {
        _snippetService.GetSnippets().Returns(new List<SnippetEntry>
        {
            new("brb", "be right back")
        });
        var vm = CreateViewModel();

        vm.EditSnippetCommand.Execute(vm.SnippetItems[0]);
        vm.NewSnippetTrigger = "changed";

        vm.CancelEditSnippetCommand.Execute(null);

        vm.IsEditingSnippet.Should().BeFalse();
        vm.NewSnippetTrigger.Should().BeEmpty();
        vm.NewSnippetReplacement.Should().BeEmpty();
    }

    [Fact]
    public void RemoveSnippet_WhileEditing_ExitsEditMode()
    {
        _snippetService.GetSnippets().Returns(new List<SnippetEntry>
        {
            new("brb", "be right back")
        });
        var vm = CreateViewModel();
        var snippet = vm.SnippetItems[0];

        vm.EditSnippetCommand.Execute(snippet);
        vm.RemoveSnippetCommand.Execute(snippet);

        vm.IsEditingSnippet.Should().BeFalse();
        vm.NewSnippetTrigger.Should().BeEmpty();
        vm.SnippetItems.Should().BeEmpty();
    }

    // --- IsCorrectionOff ---

    [Fact]
    public void Constructor_CorrectionOff_SetsIsCorrectionOffTrue()
    {
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.Off);
        vm.IsCorrectionOff.Should().BeTrue();
    }

    [Fact]
    public void Constructor_CorrectionEnabled_SetsIsCorrectionOffFalse()
    {
        var vm = CreateViewModel(o => o.TextCorrection.Provider = TextCorrectionProvider.OpenAI);
        vm.IsCorrectionOff.Should().BeFalse();
    }

    [Fact]
    public void IsCorrectionOff_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        using var monitor = vm.Monitor();

        vm.IsCorrectionOff = false;

        monitor.Should().RaisePropertyChangeFor(x => x.IsCorrectionOff);
    }

    // --- AutoAddToDictionary ---

    [Fact]
    public void Constructor_InitializesAutoAddToDictionary()
    {
        var vm = CreateViewModel(o => o.TextCorrection.AutoAddToDictionary = false);
        vm.AutoAddToDictionary.Should().BeFalse();
    }

    [Fact]
    public void Constructor_AutoAddToDictionary_DefaultsToTrue()
    {
        var vm = CreateViewModel();
        vm.AutoAddToDictionary.Should().BeTrue();
    }

    [Fact]
    public void ToggleAutoAddToDictionary_TriggersSave()
    {
        var vm = CreateViewModel();
        vm.ToggleAutoAddToDictionaryCommand.Execute(null);
        _saveCalled.Should().BeTrue();
    }

    // --- WriteSettings ---

    [Fact]
    public void WriteSettings_WritesAutoAddToDictionary()
    {
        var vm = CreateViewModel(o => o.TextCorrection.AutoAddToDictionary = false);
        var json = JsonNode.Parse("{}")!;

        vm.WriteSettings(json);

        json["TextCorrection"]!["AutoAddToDictionary"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void WriteSettings_WritesAutoAddToDictionary_True()
    {
        var vm = CreateViewModel();
        var json = JsonNode.Parse("{}")!;

        vm.WriteSettings(json);

        json["TextCorrection"]!["AutoAddToDictionary"]!.GetValue<bool>().Should().BeTrue();
    }
}
