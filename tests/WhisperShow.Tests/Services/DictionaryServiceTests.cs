using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WhisperShow.Core.Services.TextCorrection;

namespace WhisperShow.Tests.Services;

public class DictionaryServiceTests : IDisposable
{
    private readonly string _tempDir;

    public DictionaryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"whispershow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private DictionaryService CreateService()
    {
        var service = new DictionaryService(NullLogger<DictionaryService>.Instance);
        var field = typeof(DictionaryService).GetField("_filePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(service, Path.Combine(_tempDir, "dictionary.json"));
        return service;
    }

    [Fact]
    public void GetEntries_ThrowsWithoutLoadAsync()
    {
        var service = CreateService();
        var act = () => service.GetEntries();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not initialized*");
    }

    [Fact]
    public async Task GetEntries_ReturnsEmptyAfterLoad()
    {
        var service = CreateService();
        await service.LoadAsync();
        service.GetEntries().Should().BeEmpty();
    }

    [Fact]
    public async Task AddEntry_AddsWord()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.AddEntry("WhisperShow");

        service.GetEntries().Should().ContainSingle().Which.Should().Be("WhisperShow");
    }

    [Fact]
    public async Task AddEntry_IgnoresDuplicateCaseInsensitive()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.AddEntry("WhisperShow");
        service.AddEntry("whispershow");

        service.GetEntries().Should().HaveCount(1);
    }

    [Fact]
    public async Task RemoveEntry_RemovesWord()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.AddEntry("TestWord");
        service.RemoveEntry("testword");

        service.GetEntries().Should().BeEmpty();
    }

    [Fact]
    public async Task BuildPromptFragment_ReturnsEmptyForNoEntries()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.BuildPromptFragment().Should().BeEmpty();
    }

    [Fact]
    public async Task BuildPromptFragment_IncludesWords()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.AddEntry("WhisperShow");
        service.AddEntry("GPT");

        var fragment = service.BuildPromptFragment();
        fragment.Should().Contain("WhisperShow");
        fragment.Should().Contain("GPT");
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var service = CreateService();
        await service.LoadAsync();
        service.AddEntry("CustomWord");
        await service.SaveAsync();

        var service2 = CreateService();
        await service2.LoadAsync();

        service2.GetEntries().Should().ContainSingle().Which.Should().Be("CustomWord");
    }

    [Fact]
    public async Task Dispose_DoesNotThrow()
    {
        var service = CreateService();
        await service.LoadAsync();
        service.AddEntry("TestWord");

        var act = () => service.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var service = CreateService();
        service.Dispose();
        var act = () => service.Dispose();
        act.Should().NotThrow();
    }
}
