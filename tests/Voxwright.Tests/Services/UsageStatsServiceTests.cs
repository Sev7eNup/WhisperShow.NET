using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Voxwright.Core.Services.Statistics;

namespace Voxwright.Tests.Services;

public class UsageStatsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public UsageStatsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"writespeech-test-{Guid.NewGuid():N}");
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

        service.RecordTranscription(5.0, 80000, "OpenAI", 25, "Off");

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
        service.RecordTranscription(5.0, 80000, "OpenAI", 25, "Off");
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
        service.RecordTranscription(10.0, 160000, "Local", 50, "Cloud");
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
        service.RecordTranscription(1.0, 1000, "OpenAI", 5, "Off");

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
    public async Task RecordTranscription_TracksWordCount()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.RecordTranscription(5.0, 80000, "OpenAI", 25, "Off");
        service.RecordTranscription(3.0, 50000, "OpenAI", 15, "Off");

        service.GetStats().TotalWordsTranscribed.Should().Be(40);
    }

    [Fact]
    public async Task RecordTranscription_TracksLongestRecording()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.RecordTranscription(5.0, 80000, "OpenAI", 25, "Off");
        service.RecordTranscription(12.0, 200000, "OpenAI", 50, "Off");
        service.RecordTranscription(3.0, 50000, "OpenAI", 15, "Off");

        service.GetStats().LongestRecordingSeconds.Should().Be(12.0);
    }

    [Fact]
    public async Task RecordTranscription_TracksShortestRecording()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.RecordTranscription(5.0, 80000, "OpenAI", 25, "Off");
        service.RecordTranscription(12.0, 200000, "OpenAI", 50, "Off");
        service.RecordTranscription(3.0, 50000, "OpenAI", 15, "Off");

        service.GetStats().ShortestRecordingSeconds.Should().Be(3.0);
    }

    [Fact]
    public async Task RecordTranscription_ShortestRecording_NullBeforeFirstRecording()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.GetStats().ShortestRecordingSeconds.Should().BeNull();
    }

    [Fact]
    public async Task RecordTranscription_TracksCorrectionProvider()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.RecordTranscription(5.0, 80000, "OpenAI", 25, "Cloud");

        var stats = service.GetStats();
        stats.CorrectionsByProvider.Should().ContainKey("Cloud").WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task RecordTranscription_AggregatesMultipleCorrectionProviders()
    {
        var service = CreateService();
        await service.LoadAsync();

        service.RecordTranscription(5.0, 80000, "OpenAI", 25, "Cloud");
        service.RecordTranscription(3.0, 50000, "OpenAI", 15, "Cloud");
        service.RecordTranscription(4.0, 60000, "Local", 20, "Off");
        service.RecordTranscription(6.0, 90000, "OpenAI", 30, "Combined");

        var stats = service.GetStats();
        stats.CorrectionsByProvider["Cloud"].Should().Be(2);
        stats.CorrectionsByProvider["Off"].Should().Be(1);
        stats.CorrectionsByProvider["Combined"].Should().Be(1);
    }

    [Fact]
    public void SuccessRate_ComputesCorrectly()
    {
        var stats = new Voxwright.Core.Models.UsageStats
        {
            TotalTranscriptions = 9,
            ErrorCount = 1
        };

        stats.SuccessRatePercent.Should().Be(90.0);
    }

    [Fact]
    public void SuccessRate_ReturnsZero_WhenNoData()
    {
        var stats = new Voxwright.Core.Models.UsageStats();
        stats.SuccessRatePercent.Should().Be(0);
    }

    [Fact]
    public void EstimatedTimeSaved_ComputesCorrectly()
    {
        var stats = new Voxwright.Core.Models.UsageStats
        {
            TotalWordsTranscribed = 400
        };

        stats.EstimatedTimeSavedMinutes.Should().Be(10.0);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips_NewFields()
    {
        var service = CreateService();
        await service.LoadAsync();
        service.RecordTranscription(10.0, 160000, "Local", 50, "Cloud");
        service.RecordTranscription(3.0, 50000, "OpenAI", 15, "Off");
        await service.SaveAsync();

        var service2 = CreateService();
        await service2.LoadAsync();

        var stats = service2.GetStats();
        stats.TotalWordsTranscribed.Should().Be(65);
        stats.LongestRecordingSeconds.Should().Be(10.0);
        stats.ShortestRecordingSeconds.Should().Be(3.0);
        stats.CorrectionsByProvider.Should().ContainKey("Cloud").WhoseValue.Should().Be(1);
        stats.CorrectionsByProvider.Should().ContainKey("Off").WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task Reset_ClearsNewFields()
    {
        var service = CreateService();
        await service.LoadAsync();
        service.RecordTranscription(5.0, 80000, "OpenAI", 25, "Cloud");

        service.Reset();

        var stats = service.GetStats();
        stats.TotalWordsTranscribed.Should().Be(0);
        stats.LongestRecordingSeconds.Should().Be(0);
        stats.ShortestRecordingSeconds.Should().BeNull();
        stats.CorrectionsByProvider.Should().BeEmpty();
    }
}
