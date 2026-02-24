using FluentAssertions;
using WhisperShow.App.Views;

namespace WhisperShow.Tests.Views;

public class OverlayWindowTests
{
    // --- Waveform Interpolation ---

    [Fact]
    public void InterpolateWaveformLevels_AllZeros_ReturnsMinimumHeights()
    {
        var levels = new float[20];

        var heights = OverlayWindow.InterpolateWaveformLevels(levels, 16);

        heights.Should().HaveCount(16);
        heights.Should().AllSatisfy(h => h.Should().Be(2.0));
    }

    [Fact]
    public void InterpolateWaveformLevels_AllMax_ReturnsMaxHeights()
    {
        var levels = Enumerable.Repeat(1.0f, 20).ToArray();

        var heights = OverlayWindow.InterpolateWaveformLevels(levels, 16);

        heights.Should().HaveCount(16);
        // sqrt(1.0 * 5) = sqrt(5) ≈ 2.24 → clamped to 1.0 → 1.0 * 28 = 28
        heights.Should().AllSatisfy(h => h.Should().Be(28.0));
    }

    [Fact]
    public void InterpolateWaveformLevels_GradualRamp_ProducesIncreasingHeights()
    {
        var levels = new float[20];
        for (int i = 0; i < 20; i++)
            levels[i] = i / 19.0f; // 0.0 to 1.0

        var heights = OverlayWindow.InterpolateWaveformLevels(levels, 16);

        // Heights should be monotonically non-decreasing
        for (int i = 1; i < heights.Length; i++)
            heights[i].Should().BeGreaterThanOrEqualTo(heights[i - 1]);
    }

    [Fact]
    public void InterpolateWaveformLevels_SingleSpike_InterpolatesNeighbors()
    {
        var levels = new float[20];
        levels[10] = 0.5f; // spike in the middle

        var heights = OverlayWindow.InterpolateWaveformLevels(levels, 16);

        // Bars near the spike should have some height > minimum
        var hasElevatedBar = heights.Any(h => h > 2.0);
        hasElevatedBar.Should().BeTrue();

        // Bars far from the spike should be at minimum
        heights[0].Should().Be(2.0);
        heights[15].Should().Be(2.0);
    }

    [Fact]
    public void InterpolateWaveformLevels_AmplificationClamps()
    {
        // sqrt(0.3 * 5) = sqrt(1.5) ≈ 1.22 → clamped to 1.0
        var levels = Enumerable.Repeat(0.3f, 20).ToArray();

        var heights = OverlayWindow.InterpolateWaveformLevels(levels, 16);

        // level = min(sqrt(1.5), 1.0) = 1.0 → height = max(2, 1.0 * 28) = 28
        heights.Should().AllSatisfy(h => h.Should().Be(28.0));
    }

    [Fact]
    public void InterpolateWaveformLevels_LowLevel_AmplifiedButNotClamped()
    {
        // sqrt(0.1 * 5) = sqrt(0.5) ≈ 0.707 → height = 0.707 * 28 ≈ 19.8
        var levels = Enumerable.Repeat(0.1f, 20).ToArray();

        var heights = OverlayWindow.InterpolateWaveformLevels(levels, 16);

        heights.Should().AllSatisfy(h => h.Should().BeApproximately(19.8, 0.1));
    }

    [Fact]
    public void InterpolateWaveformLevels_AllEqual_AllBarsEqual()
    {
        var levels = Enumerable.Repeat(0.2f, 20).ToArray();

        var heights = OverlayWindow.InterpolateWaveformLevels(levels, 16);

        var firstHeight = heights[0];
        heights.Should().AllSatisfy(h => h.Should().Be(firstHeight));
    }

    [Fact]
    public void InterpolateWaveformLevels_FirstAndLastMatch()
    {
        // Use low values so amplification (3.5x) doesn't clamp both to 1.0
        var levels = new float[20];
        levels[0] = 0.05f;
        levels[19] = 0.2f;

        var heights = OverlayWindow.InterpolateWaveformLevels(levels, 16);

        // First bar uses levels[0]: sqrt(0.05 * 5) = sqrt(0.25) = 0.5 → height = 14
        // Last bar uses levels[19]: sqrt(0.2 * 5) = sqrt(1.0) = 1.0 → height = 28
        heights[0].Should().BeGreaterThan(2.0);
        heights[15].Should().BeGreaterThan(heights[0]);
    }

    // --- Scale Clamping (Theory-based) ---

    [Theory]
    [InlineData(0.5, 0.75)]
    [InlineData(0.0, 0.75)]
    [InlineData(-1.0, 0.75)]
    [InlineData(3.0, 2.0)]
    [InlineData(10.0, 2.0)]
    [InlineData(0.75, 0.75)]
    [InlineData(2.0, 2.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.5)]
    public void ClampOverlayScale_ReturnsExpected(double input, double expected)
    {
        OverlayWindow.ClampOverlayScale(input).Should().Be(expected);
    }
}
