using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class AnalyticBoundsTests
{
    [Fact]
    public void B1_Arc0To90_IncludesStartEndAndCardinals()
    {
        var bounds = AnalyticBounds.GetAnalyticBounds(
            new[] { ArcAt(0, 0, 100, 0, 90, ArcSweepDirection.CounterClockwise) });

        AssertBounds(bounds, 0, 0, 100, 100);
    }

    [Fact]
    public void B2_Arc15To75_DoesNotIncludeFullCircleExtrema()
    {
        var bounds = AnalyticBounds.GetAnalyticBounds(
            new[] { ArcAt(0, 0, 100, 15, 75, ArcSweepDirection.CounterClockwise) });

        AssertBounds(bounds, 26, 26, 97, 97);
    }

    [Fact]
    public void B3_Arc10To170_IncludesInternal90Extremum()
    {
        var bounds = AnalyticBounds.GetAnalyticBounds(
            new[] { ArcAt(0, 0, 100, 10, 170, ArcSweepDirection.CounterClockwise) });

        AssertBounds(bounds, -98, 17, 98, 100);
    }

    [Fact]
    public void B4_Arc200To340_HandlesNegativeExtrema()
    {
        var bounds = AnalyticBounds.GetAnalyticBounds(
            new[] { ArcAt(0, 0, 100, 200, 340, ArcSweepDirection.CounterClockwise) });

        AssertBounds(bounds, -94, -100, 94, -34);
    }

    [Fact]
    public void B5_Arc350To10_WrapsZero()
    {
        var bounds = AnalyticBounds.GetAnalyticBounds(
            new[] { ArcAt(0, 0, 100, 350, 10, ArcSweepDirection.CounterClockwise) });

        AssertBounds(bounds, 98, -17, 100, 17);
    }

    [Fact]
    public void B6_SameShortArcClockwise_KeepsSameBounds()
    {
        var bounds = AnalyticBounds.GetAnalyticBounds(
            new[] { ArcAt(0, 0, 100, 10, 350, ArcSweepDirection.Clockwise) });

        AssertBounds(bounds, 98, -17, 100, 17);
    }

    [Fact]
    public void B7_ArcOver180_IncludesAllCardinalsInSweep()
    {
        var bounds = AnalyticBounds.GetAnalyticBounds(
            new[] { ArcAt(0, 0, 100, 10, 300, ArcSweepDirection.CounterClockwise) });

        AssertBounds(bounds, -100, -100, 98, 100);
    }

    [Fact]
    public void B8_LineAndArc_UnionBounds()
    {
        var arc = ArcAt(0, 0, 100, 0, 90, ArcSweepDirection.CounterClockwise);
        var bounds = AnalyticBounds.GetAnalyticBounds(
            new[]
            {
                Segment((-200, 0), (0, 0)),
                arc,
                Segment((0, 100), (0, 0)),
            });

        AssertBounds(bounds, -200, 0, 100, 100);
    }

    [Fact]
    public void B9_LargeRadius_IsExact()
    {
        var bounds = AnalyticBounds.GetAnalyticBounds(
            new[] { ArcAt(0, 0, 600000, 0, 90, ArcSweepDirection.CounterClockwise) });

        AssertBounds(bounds, 0, 0, 600000, 600000);
    }

    [Fact]
    public void B10_BoundsDoNotDependOnSampleDensity()
    {
        var loop = new[] { ArcAt(0, 0, 100, 5, 175, ArcSweepDirection.CounterClockwise) };
        var analytic = AnalyticBounds.GetAnalyticBounds(loop);
        var sampled = CutoutRegionMatcher.GetBounds(
            CutoutRegionMatcher.SampleLoop(loop, 3));

        Assert.Equal(100, analytic.MaxY);
        Assert.True(sampled.MaxY < analytic.MaxY);
    }

    [Fact]
    public void B11_BroadPhaseUsesAnalyticExtrema()
    {
        var arc = ArcAt(0, 0, 600000, 10, 160, ArcSweepDirection.CounterClockwise);
        var loop = new[]
        {
            arc,
            Segment(arc.End, (-600000, -100000)),
            Segment((-600000, -100000), (600000, -100000)),
            Segment((600000, -100000), arc.Start),
        };
        var points = new List<Point2D> { arc.Start };
        points.AddRange(ArcDegrees(0, 0, 600000, 12, 157, 5));
        points.Add(Point(-600000, -100000));
        points.Add(Point(600000, -100000));

        var source = Source(points);
        var sampledBounds = CutoutRegionMatcher.GetBounds(
            CutoutRegionMatcher.SampleLoop(loop, CutoutRegionMatcher.ArcSamples));
        var match = CutoutRegionMatcher.Match(loop, new[] { source });

        Assert.Equal(CutoutRegionMatchStatus.Matched, match.Status);
        Assert.True(source.MaxY < 599990);
        Assert.Equal(600000, AnalyticBounds.GetAnalyticBounds(loop).MaxY);
        Assert.True(sampledBounds.MaxY < 599990);
    }

    [Fact]
    public void B12_InvalidArc_ReturnsDiagnostic()
    {
        var invalid = new OutlineSegment(
            Point(100, 0),
            Point(0, 100),
            default,
            0,
            ArcSweepDirection.CounterClockwise);

        var bounds = AnalyticBounds.GetAnalyticBounds(new[] { invalid });

        Assert.False(bounds.IsExact);
        Assert.NotNull(bounds.Error);
        Assert.Contains("radius", bounds.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static CutoutSourceContour Source(IReadOnlyList<Point2D> points)
    {
        var bounds = CutoutRegionMatcher.GetBounds(points);
        return new CutoutSourceContour(
            0,
            "uid",
            "region",
            0,
            false,
            points,
            0,
            bounds.MinX,
            bounds.MinY,
            bounds.MaxX,
            bounds.MaxY);
    }

    private static void AssertBounds(
        AnalyticBoundsResult bounds,
        int minX,
        int minY,
        int maxX,
        int maxY)
    {
        Assert.True(bounds.IsExact, bounds.Error);
        Assert.Equal((minX, minY, maxX, maxY), (bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY));
    }

    private static Point2D Point(int x, int y) => Point2D.FromIu(x, y);

    private static OutlineSegment Segment((int X, int Y) start, (int X, int Y) end) =>
        new(Point(start.X, start.Y), Point(end.X, end.Y), default, 0, null);

    private static OutlineSegment Segment(Point2D start, (int X, int Y) end) =>
        new(start, Point(end.X, end.Y), default, 0, null);

    private static OutlineSegment Segment((int X, int Y) start, Point2D end) =>
        new(Point(start.X, start.Y), end, default, 0, null);

    private static OutlineSegment ArcAt(
        int centreX,
        int centreY,
        int radius,
        int fromDegrees,
        int toDegrees,
        ArcSweepDirection direction)
    {
        return new OutlineSegment(
            Polar(centreX, centreY, radius, fromDegrees),
            Polar(centreX, centreY, radius, toDegrees),
            Point(centreX, centreY),
            radius,
            direction);
    }

    private static IEnumerable<Point2D> ArcDegrees(
        int centreX,
        int centreY,
        int radius,
        int fromDegrees,
        int toDegrees,
        int stepDegrees)
    {
        for (var angle = fromDegrees; angle <= toDegrees; angle += stepDegrees)
            yield return Polar(centreX, centreY, radius, angle);
    }

    private static Point2D Polar(int centreX, int centreY, int radius, int degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return Point(
            (int)Math.Round(centreX + (radius * Math.Cos(radians)), MidpointRounding.AwayFromZero),
            (int)Math.Round(centreY + (radius * Math.Sin(radians)), MidpointRounding.AwayFromZero));
    }
}
