using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WhisperShow.Core.Configuration;
using WhisperShow.Core.Services.History;

namespace WhisperShow.Tests.Services;

public class TranscriptionHistoryServiceTests : IDisposable
{
    private readonly string _tempDir;

    public TranscriptionHistoryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"whispershow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private TranscriptionHistoryService CreateService(int maxEntries = 20)
    {
        var options = Options.Create(new WhisperShowOptions
        {
            App = new AppOptions { MaxHistoryEntries = maxEntries }
        });
        var service = new TranscriptionHistoryService(
            NullLogger<TranscriptionHistoryService>.Instance, options);
        SetFilePath(service, Path.Combine(_tempDir, "history.json"));
        return service;
    }

    private static void SetFilePath(TranscriptionHistoryService service, string path)
    {
        var field = typeof(TranscriptionHistoryService).GetField("_filePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(service, path);
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
    public async Task AddEntry_AddsAndReturns()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.AddEntry("Hello world", "OpenAI", 2.5);

        var entries = service.GetEntries();
        entries.Should().HaveCount(1);
        entries[0].Text.Should().Be("Hello world");
        entries[0].Provider.Should().Be("OpenAI");
        entries[0].DurationSeconds.Should().Be(2.5);
    }

    [Fact]
    public async Task AddEntry_RespectsMaxEntries()
    {
        var service = CreateService(maxEntries: 3);
        await service.LoadAsync();

        service.AddEntry("One", "OpenAI", 1);
        service.AddEntry("Two", "OpenAI", 1);
        service.AddEntry("Three", "OpenAI", 1);
        service.AddEntry("Four", "OpenAI", 1);

        service.GetEntries().Should().HaveCount(3);
        service.GetEntries()[0].Text.Should().Be("Four");
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var service = CreateService();
        await service.LoadAsync();
        service.AddEntry("Test text", "Local", 3.0);
        await service.SaveAsync();

        var service2 = CreateService();
        SetFilePath(service2, Path.Combine(_tempDir, "history.json"));
        await service2.LoadAsync();

        service2.GetEntries().Should().HaveCount(1);
        service2.GetEntries()[0].Text.Should().Be("Test text");
    }

    [Fact]
    public async Task Clear_RemovesAllEntries()
    {
        var service = CreateService();
        await service.LoadAsync();
        service.AddEntry("Entry", "OpenAI", 1);

        service.Clear();

        service.GetEntries().Should().BeEmpty();
    }
}
