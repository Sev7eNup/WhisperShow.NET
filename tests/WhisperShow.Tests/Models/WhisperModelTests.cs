using FluentAssertions;
using WhisperShow.Core.Models;

namespace WhisperShow.Tests.Models;

public class WhisperModelTests
{
    [Fact]
    public void SizeDisplay_UnderOneMB_ShowsKB()
    {
        var model = CreateModel(sizeBytes: 500_000);
        model.SizeDisplay.Should().Be("488 KB");
    }

    [Fact]
    public void SizeDisplay_SmallValue_ShowsKB()
    {
        var model = CreateModel(sizeBytes: 1024);
        model.SizeDisplay.Should().Be("1 KB");
    }

    [Fact]
    public void SizeDisplay_UnderOneGB_ShowsMB()
    {
        var model = CreateModel(sizeBytes: 466_000_000);
        model.SizeDisplay.Should().Be("444 MB");
    }

    [Fact]
    public void SizeDisplay_ExactlyOneMB_ShowsMB()
    {
        var model = CreateModel(sizeBytes: 1_048_576);
        model.SizeDisplay.Should().Be("1 MB");
    }

    [Fact]
    public void SizeDisplay_OneGBOrMore_ShowsGB()
    {
        var model = CreateModel(sizeBytes: 3_000_000_000);
        // Locale-dependent decimal separator (dot or comma)
        model.SizeDisplay.Should().MatchRegex(@"2[.,]8 GB");
    }

    [Fact]
    public void IsDownloaded_NullFilePath_ReturnsFalse()
    {
        var model = CreateModel(filePath: null);
        model.IsDownloaded.Should().BeFalse();
    }

    [Fact]
    public void IsDownloaded_NonExistentPath_ReturnsFalse()
    {
        var model = CreateModel(filePath: @"C:\nonexistent\path\model.bin");
        model.IsDownloaded.Should().BeFalse();
    }

    private static WhisperModel CreateModel(long sizeBytes = 100, string? filePath = null)
    {
        return new WhisperModel
        {
            Name = "Test",
            FileName = "test.bin",
            SizeBytes = sizeBytes,
            FilePath = filePath
        };
    }
}
