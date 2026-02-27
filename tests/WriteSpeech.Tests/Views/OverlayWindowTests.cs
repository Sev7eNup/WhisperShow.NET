using System.Runtime.InteropServices;
using FluentAssertions;
using WriteSpeech.App;
using WriteSpeech.App.Views;
using WriteSpeech.Tests.TestHelpers;

namespace WriteSpeech.Tests.Views;

public class OverlayWindowTests
{
    // --- Waveform Point Generation (Glassmorphism sine-wave) ---

    [Fact]
    public void ComputeWaveformPoints_AllZeros_AllAtCenterY()
    {
        var levels = new float[20];

        var points = OverlayWindow.ComputeWaveformPoints(levels, 80, 30);

        points.Should().HaveCount(20);
        points.Should().AllSatisfy(p => p.y.Should().Be(15.0));
    }

    [Fact]
    public void ComputeWaveformPoints_PointCount_MatchesLevelsLength()
    {
        var levels = new float[20];

        var points = OverlayWindow.ComputeWaveformPoints(levels, 80, 30);

        points.Should().HaveCount(20);
    }

    [Fact]
    public void ComputeWaveformPoints_XValues_EvenlySpaced()
    {
        var levels = new float[20];

        var points = OverlayWindow.ComputeWaveformPoints(levels, 80, 30);

        points[0].x.Should().Be(0);
        points[19].x.Should().BeApproximately(80, 0.001);

        double expectedSpacing = 80.0 / 19.0;
        for (int i = 1; i < points.Length; i++)
            (points[i].x - points[i - 1].x).Should().BeApproximately(expectedSpacing, 0.001);
    }

    [Fact]
    public void ComputeWaveformPoints_AllMax_AlternateUpDown()
    {
        var levels = Enumerable.Repeat(1.0f, 20).ToArray();

        var points = OverlayWindow.ComputeWaveformPoints(levels, 80, 30);

        double centerY = 15.0;
        for (int i = 0; i < points.Length; i++)
        {
            if (i % 2 == 0)
                points[i].y.Should().BeLessThan(centerY);
            else
                points[i].y.Should().BeGreaterThan(centerY);
        }
    }

    [Fact]
    public void ComputeWaveformPoints_YValues_WithinBounds()
    {
        var levels = Enumerable.Repeat(1.0f, 20).ToArray();

        var points = OverlayWindow.ComputeWaveformPoints(levels, 80, 30);

        points.Should().AllSatisfy(p =>
        {
            p.y.Should().BeGreaterThanOrEqualTo(1.0);
            p.y.Should().BeLessThanOrEqualTo(29.0);
        });
    }

    [Fact]
    public void ComputeWaveformPoints_SingleSpike_OnlyAffectsOnePoint()
    {
        var levels = new float[20];
        levels[10] = 0.5f;

        var points = OverlayWindow.ComputeWaveformPoints(levels, 80, 30);

        points[10].y.Should().NotBe(15.0);
        points[0].y.Should().Be(15.0);
        points[19].y.Should().Be(15.0);
    }

    [Fact]
    public void ComputeWaveformPoints_AmplitudeScaling_SqrtCompression()
    {
        // sqrt(0.1 * 5) = sqrt(0.5) ~ 0.707, amplitude = 0.707 * 14 ~ 9.9
        var levels = Enumerable.Repeat(0.1f, 20).ToArray();

        var points = OverlayWindow.ComputeWaveformPoints(levels, 80, 30);

        double centerY = 15.0;
        double expectedAmplitude = MathF.Sqrt(0.1f * 5.0f) * 14.0;

        // Even index: centerY - amplitude (above center)
        points[0].y.Should().BeApproximately(centerY - expectedAmplitude, 0.1);
        // Odd index: centerY + amplitude (below center)
        points[1].y.Should().BeApproximately(centerY + expectedAmplitude, 0.1);
    }

    [Fact]
    public void GenerateWaveformPaths_AllZeros_ReturnsNonNullFrozenGeometries()
    {
        WpfTestHelper.EnsureApplication();
        var levels = new float[20];

        var (line, fill) = OverlayWindow.GenerateWaveformPaths(levels, 80, 30);

        line.Should().NotBeNull();
        fill.Should().NotBeNull();
        line.IsFrozen.Should().BeTrue();
        fill.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void GenerateWaveformPaths_WithData_ReturnsNonEmptyGeometries()
    {
        WpfTestHelper.EnsureApplication();
        var levels = Enumerable.Range(0, 20).Select(i => i / 19.0f).ToArray();

        var (line, fill) = OverlayWindow.GenerateWaveformPaths(levels, 80, 30);

        line.Should().NotBeNull();
        fill.Should().NotBeNull();
        fill.Bounds.Width.Should().BeGreaterThan(0);
        fill.Bounds.Height.Should().BeGreaterThan(0);
    }

    // --- Waveform Interpolation (legacy, kept for backward compatibility) ---

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

    // --- Void Overload (pre-allocated array) ---

    [Fact]
    public void InterpolateWaveformLevels_VoidOverload_WritesToExistingArray()
    {
        var levels = Enumerable.Repeat(0.1f, 20).ToArray();
        var heights = new double[16];

        OverlayWindow.InterpolateWaveformLevels(levels, heights);

        heights.Should().AllSatisfy(h => h.Should().BeGreaterThan(2.0));
    }

    [Fact]
    public void InterpolateWaveformLevels_VoidOverload_MatchesReturningVersion()
    {
        var levels = new float[20];
        for (int i = 0; i < 20; i++) levels[i] = i / 19.0f;

        var fromReturning = OverlayWindow.InterpolateWaveformLevels(levels, 16);
        var fromVoid = new double[16];
        OverlayWindow.InterpolateWaveformLevels(levels, fromVoid);

        fromVoid.Should().BeEquivalentTo(fromReturning);
    }

    // --- DWM MARGINS struct validation ---

    [Fact]
    public void DwmMargins_StructLayout_CorrectSize()
    {
        // MARGINS is 4 x int32 = 16 bytes, must match native layout for P/Invoke
        Marshal.SizeOf<NativeMethods.MARGINS>().Should().Be(16);
    }

    [Fact]
    public void DwmMargins_AllNegativeOne_FieldsCorrect()
    {
        var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };

        margins.Left.Should().Be(-1);
        margins.Right.Should().Be(-1);
        margins.Top.Should().Be(-1);
        margins.Bottom.Should().Be(-1);
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
