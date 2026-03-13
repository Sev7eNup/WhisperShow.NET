using System.Reflection;
using FluentAssertions;
using NSubstitute;
using Voxwright.App.ViewModels.Settings;
using Voxwright.Core.Models;
using Voxwright.Core.Services.Statistics;

namespace Voxwright.Tests.ViewModels;

public class StatisticsViewModelTests
{
    private readonly IUsageStatsService _statsService;
    private readonly StatisticsViewModel _vm;

    public StatisticsViewModelTests()
    {
        _statsService = Substitute.For<IUsageStatsService>();
        _vm = new StatisticsViewModel(_statsService);
    }

    [Fact]
    public void Refresh_PopulatesProperties()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 42,
            TotalRecordingSeconds = 3661, // 1h 1m 1s
            ErrorCount = 3,
            TranscriptionsByProvider = new Dictionary<string, int>
            {
                ["OpenAI"] = 30,
                ["Local"] = 12
            }
        });

        _vm.RefreshCommand.Execute(null);

        var stats = _statsService.GetStats();

        _vm.TotalTranscriptions.Should().Be(42);
        _vm.ErrorCount.Should().Be(3);
        _vm.TotalRecordingTimeDisplay.Should().Be("1h 1m");
        // AverageRecordingSeconds = 3661 / 42 ≈ 87.2 — format is culture-dependent
        _vm.AverageDurationDisplay.Should().Be($"{stats.AverageRecordingSeconds:F1}s");
        // EstimatedApiCost = (3661 / 60.0) * 0.006 ≈ 0.3661 — format is culture-dependent
        _vm.EstimatedCostDisplay.Should().Be($"${stats.EstimatedApiCost:F4}");
    }

    [Fact]
    public void Refresh_FormatsProviderBreakdown_WithData()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 5,
            TotalRecordingSeconds = 60,
            TranscriptionsByProvider = new Dictionary<string, int>
            {
                ["OpenAI"] = 3,
                ["Local"] = 2
            }
        });

        _vm.RefreshCommand.Execute(null);

        _vm.ProviderBreakdownDisplay.Should().Contain("OpenAI: 3");
        _vm.ProviderBreakdownDisplay.Should().Contain("Local: 2");
    }

    [Fact]
    public void Refresh_ShowsNoDataYet_WhenEmpty()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 0,
            TotalRecordingSeconds = 0,
            TranscriptionsByProvider = new Dictionary<string, int>()
        });

        _vm.RefreshCommand.Execute(null);

        _vm.ProviderBreakdownDisplay.Should().Be("No data yet");
    }

    [Fact]
    public void Reset_CallsServiceReset_AndRefreshes()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 0,
            TotalRecordingSeconds = 0,
            TranscriptionsByProvider = new Dictionary<string, int>()
        });

        _vm.ResetCommand.Execute(null);

        _statsService.Received(1).Reset();
        _statsService.Received(1).GetStats(); // Refresh calls GetStats
        _vm.TotalTranscriptions.Should().Be(0);
    }

    [Fact]
    public void FormatDuration_ReturnsMinutesSeconds_WhenUnderOneHour()
    {
        var method = typeof(StatisticsViewModel)
            .GetMethod("FormatDuration", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, [150.0])!; // 2m 30s

        result.Should().Be("2m 30s");
    }

    [Fact]
    public void FormatDuration_ReturnsHoursMinutes_WhenOverOneHour()
    {
        var method = typeof(StatisticsViewModel)
            .GetMethod("FormatDuration", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, [7380.0])!; // 2h 3m

        result.Should().Be("2h 3m");
    }

    [Fact]
    public void Refresh_PopulatesWordsTranscribedDisplay()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 10,
            TotalWordsTranscribed = 1234
        });

        _vm.RefreshCommand.Execute(null);

        _vm.WordsTranscribedDisplay.Should().Be(1234.ToString("N0"));
    }

    [Fact]
    public void Refresh_PopulatesTimeSavedDisplay()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 10,
            TotalWordsTranscribed = 1200 // 1200/40 = 30 min
        });

        _vm.RefreshCommand.Execute(null);

        _vm.TimeSavedDisplay.Should().Be("30 min");
    }

    [Fact]
    public void Refresh_PopulatesSuccessRateDisplay()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 9,
            ErrorCount = 1 // 90%
        });

        _vm.RefreshCommand.Execute(null);

        _vm.SuccessRateDisplay.Should().Be($"{90.0:F1}%");
    }

    [Fact]
    public void Refresh_ShowsDash_WhenNoDataForSuccessRate()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 0,
            ErrorCount = 0
        });

        _vm.RefreshCommand.Execute(null);

        _vm.SuccessRateDisplay.Should().Be("—");
    }

    [Fact]
    public void Refresh_PopulatesLongestRecordingDisplay()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 5,
            LongestRecordingSeconds = 45.2
        });

        _vm.RefreshCommand.Execute(null);

        _vm.LongestRecordingDisplay.Should().Be($"{45.2:F1}s");
    }

    [Fact]
    public void Refresh_PopulatesShortestRecordingDisplay()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 5,
            ShortestRecordingSeconds = 3.1
        });

        _vm.RefreshCommand.Execute(null);

        _vm.ShortestRecordingDisplay.Should().Be($"{3.1:F1}s");
    }

    [Fact]
    public void Refresh_ShowsDash_WhenNoShortestRecording()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 0,
            ShortestRecordingSeconds = null
        });

        _vm.RefreshCommand.Execute(null);

        _vm.ShortestRecordingDisplay.Should().Be("—");
    }

    [Fact]
    public void Refresh_ShowsDash_ForLongestRecording_WhenNoTranscriptions()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 0
        });

        _vm.RefreshCommand.Execute(null);

        _vm.LongestRecordingDisplay.Should().Be("—");
    }

    [Fact]
    public void Refresh_PopulatesCorrectionBreakdownDisplay()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 10,
            CorrectionsByProvider = new Dictionary<string, int>
            {
                ["Cloud"] = 7,
                ["Off"] = 3
            }
        });

        _vm.RefreshCommand.Execute(null);

        _vm.CorrectionBreakdownDisplay.Should().Contain("Cloud: 7");
        _vm.CorrectionBreakdownDisplay.Should().Contain("Off: 3");
    }

    [Fact]
    public void Refresh_ShowsNoDataYet_WhenNoCorrectionData()
    {
        _statsService.GetStats().Returns(new UsageStats
        {
            TotalTranscriptions = 0,
            CorrectionsByProvider = new Dictionary<string, int>()
        });

        _vm.RefreshCommand.Execute(null);

        _vm.CorrectionBreakdownDisplay.Should().Be("No data yet");
    }

    [Fact]
    public void FormatTimeSaved_ReturnsMinutes_WhenUnderOneHour()
    {
        StatisticsViewModel.FormatTimeSaved(30.85).Should().Be("31 min");
    }

    [Fact]
    public void FormatTimeSaved_ReturnsHours_WhenOverOneHour()
    {
        StatisticsViewModel.FormatTimeSaved(90.0).Should().Be($"{1.5:F1}h");
    }

    [Fact]
    public void FormatTimeSaved_ReturnsZero_WhenNoWords()
    {
        StatisticsViewModel.FormatTimeSaved(0).Should().Be("0 min");
    }
}
