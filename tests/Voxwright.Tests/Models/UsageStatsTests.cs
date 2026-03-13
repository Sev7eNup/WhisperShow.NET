using FluentAssertions;
using Voxwright.Core.Models;

namespace Voxwright.Tests.Models;

public class UsageStatsTests
{
    [Fact]
    public void CostPerMinuteUsd_HasExpectedValue()
    {
        UsageStats.CostPerMinuteUsd.Should().Be(0.006);
    }

    [Fact]
    public void EstimatedApiCost_UsesCostPerMinuteConstant()
    {
        var stats = new UsageStats { TotalRecordingSeconds = 120 };

        // 120 seconds = 2 minutes, 2 * 0.006 = 0.012
        stats.EstimatedApiCost.Should().BeApproximately(0.012, 0.0001);
    }

    [Fact]
    public void EstimatedApiCost_ZeroRecording_ReturnsZero()
    {
        var stats = new UsageStats { TotalRecordingSeconds = 0 };

        stats.EstimatedApiCost.Should().Be(0);
    }

    [Fact]
    public void AverageRecordingSeconds_NoTranscriptions_ReturnsZero()
    {
        var stats = new UsageStats { TotalTranscriptions = 0, TotalRecordingSeconds = 100 };

        stats.AverageRecordingSeconds.Should().Be(0);
    }

    [Fact]
    public void SuccessRatePercent_AllSuccessful_Returns100()
    {
        var stats = new UsageStats { TotalTranscriptions = 10, ErrorCount = 0 };

        stats.SuccessRatePercent.Should().Be(100);
    }

    [Fact]
    public void SuccessRatePercent_HalfErrors_Returns50()
    {
        var stats = new UsageStats { TotalTranscriptions = 5, ErrorCount = 5 };

        stats.SuccessRatePercent.Should().Be(50);
    }

    [Fact]
    public void TotalRecordingDisplay_UnderOneHour_ShowsMinutesSeconds()
    {
        var stats = new UsageStats { TotalRecordingSeconds = 125 };

        stats.TotalRecordingDisplay.Should().Be("2:05");
    }

    [Fact]
    public void TotalRecordingDisplay_OverOneHour_ShowsHoursMinutesSeconds()
    {
        var stats = new UsageStats { TotalRecordingSeconds = 3725 };

        stats.TotalRecordingDisplay.Should().Be("1:02:05");
    }
}
