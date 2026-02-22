using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WhisperShow.Core.Services.Snippets;

namespace WhisperShow.Tests.Services;

public class SnippetServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnippetService _service;

    public SnippetServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"whispershow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Use a custom file path via reflection to avoid polluting real AppData
        _service = new SnippetService(NullLogger<SnippetService>.Instance);
        var field = typeof(SnippetService).GetField("_filePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(_service, Path.Combine(_tempDir, "snippets.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void ApplySnippets_ReplacesKeywordCaseInsensitive()
    {
        _service.AddSnippet("greeting", "Dear Sir or Madam,");

        var result = _service.ApplySnippets("Please write Greeting to Mr. Smith.");

        result.Should().Be("Please write Dear Sir or Madam, to Mr. Smith.");
    }

    [Fact]
    public void ApplySnippets_ReplacesMultipleOccurrences()
    {
        _service.AddSnippet("sig", "Best regards, John");

        var result = _service.ApplySnippets("Start with sig and end with sig too.");

        result.Should().Be("Start with Best regards, John and end with Best regards, John too.");
    }

    [Fact]
    public void ApplySnippets_RespectsWordBoundaries()
    {
        _service.AddSnippet("test", "REPLACED");

        var result = _service.ApplySnippets("This is a test but testing should not match.");

        result.Should().Be("This is a REPLACED but testing should not match.");
    }

    [Fact]
    public void ApplySnippets_MultipleSnippets()
    {
        _service.AddSnippet("hello", "Good morning");
        _service.AddSnippet("bye", "Kind regards");

        var result = _service.ApplySnippets("hello world, bye now");

        result.Should().Be("Good morning world, Kind regards now");
    }

    [Fact]
    public void ApplySnippets_EmptyText_ReturnsEmpty()
    {
        _service.AddSnippet("test", "REPLACED");

        _service.ApplySnippets("").Should().Be("");
        _service.ApplySnippets("  ").Should().Be("  ");
    }

    [Fact]
    public void ApplySnippets_NoSnippets_ReturnsOriginal()
    {
        var result = _service.ApplySnippets("Hello world");

        result.Should().Be("Hello world");
    }

    [Fact]
    public void AddSnippet_DuplicateTrigger_IsIgnored()
    {
        _service.AddSnippet("greet", "Hello");
        _service.AddSnippet("GREET", "Goodbye");

        _service.GetSnippets().Should().HaveCount(1);
        _service.GetSnippets()[0].Replacement.Should().Be("Hello");
    }

    [Fact]
    public void RemoveSnippet_RemovesExisting()
    {
        _service.AddSnippet("test", "value");
        _service.RemoveSnippet("TEST");

        _service.GetSnippets().Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        _service.AddSnippet("intro", "Dear colleagues,");
        _service.AddSnippet("outro", "Best regards");

        await _service.SaveAsync();

        // Create new service instance pointing to same file
        var service2 = new SnippetService(NullLogger<SnippetService>.Instance);
        var field = typeof(SnippetService).GetField("_filePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(service2, Path.Combine(_tempDir, "snippets.json"));

        await service2.LoadAsync();

        service2.GetSnippets().Should().HaveCount(2);
        service2.GetSnippets().Should().Contain(s => s.Trigger == "intro");
        service2.GetSnippets().Should().Contain(s => s.Trigger == "outro");
    }

    [Fact]
    public void ApplySnippets_SpecialRegexChars_DoNotCrash()
    {
        // Triggers with regex special chars should not throw
        _service.AddSnippet("c#", "C Sharp");

        var result = _service.ApplySnippets("I love c# programming.");

        // Even if word-boundary matching may not trigger for all special chars,
        // the service must not throw due to unescaped regex characters
        result.Should().NotBeNull();
    }
}
