using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WhisperShow.Core.Services.Statistics;

namespace WhisperShow.Tests.Services;

public class UsageStatsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public UsageStatsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"whispershow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private UsageStatsService CreateService()
    {
        var service = new UsageStatsService(NullLogger<UsageStatsService>.Instance);
        var field = typeof(UsageStatsService).GetField("_filePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(service, Path.Combine(_tempDir, "stats.json"));
        return service;
    }

    [Fact]
    public void GetStats_ThrowsWithoutLoadAsync()
    {
        var service = CreateService();
        var act = () => service.GetStats();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not initialized*");
    }

    [Fact]
    public async Task GetStats_ReturnsDefaultsAfterLoad()
    {
        var service = CreateService();
        await service.LoadAsync();

        var stats = service.GetStats();
        stats.TotalTranscriptions.Should().Be(0);
        stats.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task RecordTranscription_IncrementsStats()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.RecordTranscription(5.0, 80000, "OpenAI");

        var stats = service.GetStats();
        stats.TotalTranscriptions.Should().Be(1);
        stats.TotalRecordingSeconds.Should().Be(5.0);
        stats.TotalAudioBytesProcessed.Should().Be(80000);
        stats.TranscriptionsByProvider.Should().ContainKey("OpenAI").WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task RecordError_IncrementsErrorCount()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.RecordError();
        service.RecordError();

        service.GetStats().ErrorCount.Should().Be(2);
    }

    [Fact]
    public async Task Reset_ClearsAllStats()
    {
        var service = CreateService();
        await service.LoadAsync();
        service.RecordTranscription(5.0, 80000, "OpenAI");
        service.RecordError();

        service.Reset();

        var stats = service.GetStats();
        stats.TotalTranscriptions.Should().Be(0);
        stats.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var service = CreateService();
        await service.LoadAsync();
        service.RecordTranscription(10.0, 160000, "Local");
        await service.SaveAsync();

        var service2 = CreateService();
        await service2.LoadAsync();

        var stats = service2.GetStats();
        stats.TotalTranscriptions.Should().Be(1);
        stats.TranscriptionsByProvider.Should().ContainKey("Local");
    }

    [Fact]
    public async Task Dispose_DoesNotThrow()
    {
        var service = CreateService();
        await service.LoadAsync();
        service.RecordTranscription(1.0, 1000, "OpenAI");

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
