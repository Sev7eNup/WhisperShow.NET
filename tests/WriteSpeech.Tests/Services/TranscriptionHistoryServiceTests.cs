using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WriteSpeech.Core.Configuration;
using WriteSpeech.Core.Services.History;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.Services;

public class TranscriptionHistoryServiceTests : IDisposable
{
    private readonly string _tempDir;

    public TranscriptionHistoryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"writespeech-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private TranscriptionHistoryService CreateService(int maxEntries = 20)
    {
        var optionsMonitor = OptionsHelper.CreateMonitor(o =>
            o.App = new AppOptions { MaxHistoryEntries = maxEntries });
        var service = new TranscriptionHistoryService(
            NullLogger<TranscriptionHistoryService>.Instance, optionsMonitor);
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

    [Fact]
    public async Task Dispose_DoesNotThrow()
    {
        var service = CreateService();
        await service.LoadAsync();
        service.AddEntry("Test", "OpenAI", 1);

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

    [Fact]
    public async Task AddEntry_NewestFirst_OrderPreserved()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.AddEntry("First", "OpenAI", 1);
        service.AddEntry("Second", "OpenAI", 1);
        service.AddEntry("Third", "OpenAI", 1);

        var entries = service.GetEntries();
        entries[0].Text.Should().Be("Third");
        entries[1].Text.Should().Be("Second");
        entries[2].Text.Should().Be("First");
    }

    [Fact]
    public async Task AddEntry_AtExactlyMaxEntries_RemovesOldest()
    {
        var service = CreateService(maxEntries: 3);
        await service.LoadAsync();

        service.AddEntry("One", "OpenAI", 1);
        service.AddEntry("Two", "OpenAI", 1);
        service.AddEntry("Three", "OpenAI", 1);
        service.GetEntries().Should().HaveCount(3);

        service.AddEntry("Four", "OpenAI", 1);

        var entries = service.GetEntries();
        entries.Should().HaveCount(3);
        entries[0].Text.Should().Be("Four");
        entries[2].Text.Should().Be("Two");
        entries.Should().NotContain(e => e.Text == "One");
    }

    [Fact]
    public async Task RemoveEntry_SpecificEntry_OthersPreserved()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.AddEntry("One", "OpenAI", 1);
        service.AddEntry("Two", "OpenAI", 1);
        service.AddEntry("Three", "OpenAI", 1);

        var entryToRemove = service.GetEntries().First(e => e.Text == "Two");
        service.RemoveEntry(entryToRemove);

        var entries = service.GetEntries();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.Text == "Three");
        entries.Should().Contain(e => e.Text == "One");
    }

    [Fact]
    public async Task LoadAsync_CorruptedJson_InitializesEmpty()
    {
        var filePath = Path.Combine(_tempDir, "corrupt-history.json");
        File.WriteAllText(filePath, "{ not valid json !!! }");

        var service = CreateService();
        SetFilePath(service, filePath);

        await service.LoadAsync();

        service.GetEntries().Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_CorruptedJson_CreatesBackupFile()
    {
        var filePath = Path.Combine(_tempDir, "backup-history.json");
        File.WriteAllText(filePath, "{ not valid json !!! }");

        var service = CreateService();
        SetFilePath(service, filePath);

        await service.LoadAsync();

        var backupFiles = Directory.GetFiles(_tempDir, "backup-history.json.corrupt-*");
        backupFiles.Should().HaveCount(1);
        File.ReadAllText(backupFiles[0]).Should().Be("{ not valid json !!! }");
    }

    [Fact]
    public async Task SaveAndLoad_PreservesAllFields()
    {
        var service = CreateService();
        await service.LoadAsync();
        service.AddEntry("Test text", "Parakeet", 42.5);
        await service.SaveAsync();

        var service2 = CreateService();
        SetFilePath(service2, Path.Combine(_tempDir, "history.json"));
        await service2.LoadAsync();

        var entry = service2.GetEntries()[0];
        entry.Text.Should().Be("Test text");
        entry.Provider.Should().Be("Parakeet");
        entry.DurationSeconds.Should().Be(42.5);
        entry.TimestampUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
