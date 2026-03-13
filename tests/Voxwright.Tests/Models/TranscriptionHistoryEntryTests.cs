using FluentAssertions;
using Voxwright.Core.Models;

namespace Voxwright.Tests.Models;

public class TranscriptionHistoryEntryTests
{
    // --- Preview ---

    [Fact]
    public void Preview_ShortText_ReturnsFullText()
    {
        var entry = new TranscriptionHistoryEntry { Text = "Hello world" };

        entry.Preview.Should().Be("Hello world");
    }

    [Fact]
    public void Preview_ExactlyAt80Chars_ReturnsFullText()
    {
        var text = new string('a', 80);
        var entry = new TranscriptionHistoryEntry { Text = text };

        entry.Preview.Should().Be(text);
        entry.Preview.Should().HaveLength(80);
    }

    [Fact]
    public void Preview_LongerThan80Chars_TruncatesWithEllipsis()
    {
        var text = new string('a', 81);
        var entry = new TranscriptionHistoryEntry { Text = text };

        entry.Preview.Should().HaveLength(83); // 80 + "..."
        entry.Preview.Should().EndWith("...");
    }

    [Fact]
    public void Preview_EmptyText_ReturnsEmpty()
    {
        var entry = new TranscriptionHistoryEntry { Text = "" };

        entry.Preview.Should().BeEmpty();
    }

    // --- TimeAgo ---

    [Fact]
    public void TimeAgo_LessThanOneMinute_ReturnsJustNow()
    {
        var entry = new TranscriptionHistoryEntry { TimestampUtc = DateTime.UtcNow.AddSeconds(-30) };

        entry.TimeAgo.Should().Be("just now");
    }

    [Fact]
    public void TimeAgo_FiveMinutes_Returns5mAgo()
    {
        var entry = new TranscriptionHistoryEntry { TimestampUtc = DateTime.UtcNow.AddMinutes(-5) };

        entry.TimeAgo.Should().Be("5m ago");
    }

    [Fact]
    public void TimeAgo_ExactlyOneHour_Returns1hAgo()
    {
        var entry = new TranscriptionHistoryEntry { TimestampUtc = DateTime.UtcNow.AddHours(-1) };

        entry.TimeAgo.Should().Be("1h ago");
    }

    [Fact]
    public void TimeAgo_ThreeDays_Returns3dAgo()
    {
        var entry = new TranscriptionHistoryEntry { TimestampUtc = DateTime.UtcNow.AddDays(-3) };

        entry.TimeAgo.Should().Be("3d ago");
    }

    [Fact]
    public void TimeAgo_59Minutes_Returns59mAgo()
    {
        var entry = new TranscriptionHistoryEntry { TimestampUtc = DateTime.UtcNow.AddMinutes(-59) };

        entry.TimeAgo.Should().Be("59m ago");
    }

    [Fact]
    public void TimeAgo_23Hours_Returns23hAgo()
    {
        var entry = new TranscriptionHistoryEntry { TimestampUtc = DateTime.UtcNow.AddHours(-23) };

        entry.TimeAgo.Should().Be("23h ago");
    }
}
