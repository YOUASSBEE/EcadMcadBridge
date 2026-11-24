using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using PCB;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class ArcExactMatchTests
{
    [Fact]
    public void C1_ExactMatch_AcceptsAnalyticArcWithDensePolyline()
    {
        var loop = new[]
        {
            Arc((100, 0), (0, 100), (0, 0), 100, ArcSweepDirection.CounterClockwise),
            Segment((0, 100), (0, 0)),
            Segment((0, 0), (100, 0)),
        };
        var contour = new List<Point2D> { Point(100, 0) };
        contour.AddRange(ArcDegrees(0, 0, 100, 5, 90, 5));
        contour.Add(Point(0, 0));

        Assert.True(OutlineContourMatcher.TryExactMatch(contour, loop));
    }

    [Fact]
    public void C2_ExactMatch_RejectsCoarseArcPolyline()
    {
        var loop = new[]
        {
            Arc((100, 0), (0, 100), (0, 0), 100, ArcSweepDirection.CounterClockwise),
            Segment((0, 100), (0, 0)),
            Segment((0, 0), (100, 0)),
        };
        var contour = new[] { Point(100, 0), Point(0, 100), Point(0, 0) };

        Assert.False(OutlineContourMatcher.TryExactMatch(contour, loop));
    }

    [Fact]
    public void C3_ExactMatch_RejectsOppositeArcDirection()
    {
        var loop = new[]
        {
            Arc((100, 0), (0, 100), (0, 0), 100, ArcSweepDirection.Clockwise),
            Segment((0, 100), (0, 0)),
            Segment((0, 0), (100, 0)),
        };
        var contour = new List<Point2D> { Point(100, 0) };
        contour.AddRange(ArcDegrees(0, 0, 100, 5, 90, 5));
        contour.Add(Point(0, 0));

        Assert.False(OutlineContourMatcher.TryExactMatch(contour, loop));
    }

    [Fact]
    public void ArcAnglesCrossingZero_UseShortCounterClockwiseArc()
    {
        var raw = new[]
        {
            new AltiumOutlineReader.RawSegment(
                TPolySegmentType.ePolySegmentArc,
                609742449,
                402521330,
                609742449,
                402796921,
                275591,
                270,
                0),
        };

        var segments = AltiumOutlineReader.BuildOutlineSegments(raw);

        var segment = Assert.Single(segments);
        Assert.Equal(ArcSweepDirection.CounterClockwise, segment.SweepDirection);
        Assert.Equal(Point(609742449, 402521330), segment.Start);
        Assert.Equal(Point(610018040, 402796921), segment.End);
    }

    [Fact]
    public void C4_ExactMatch_HandlesTwoArcsWithDifferentSweepDirections()
    {
        var loop = new[]
        {
            Segment((0, 0), (40, 0)),
            Arc((40, 0), (60, 0), (50, 0), 10, ArcSweepDirection.CounterClockwise),
            Segment((60, 0), (100, 0)),
            Segment((100, 0), (100, 100)),
            Segment((100, 100), (60, 100)),
            Arc((60, 100), (40, 100), (50, 100), 10, ArcSweepDirection.Clockwise),
            Segment((40, 100), (0, 100)),
            Segment((0, 100), (0, 0)),
        };
        var contour = new List<Point2D> { Point(0, 0), Point(40, 0) };
        contour.AddRange(ArcDegrees(50, 0, 10, 190, 360, 10));
        contour.Add(Point(100, 0));
        contour.Add(Point(100, 100));
        contour.Add(Point(60, 100));
        contour.AddRange(ArcDegrees(50, 100, 10, -10, -170, -10));
        contour.Add(Point(40, 100));
        contour.Add(Point(0, 100));

        Assert.True(OutlineContourMatcher.TryExactMatch(contour, loop));

        var coarse = new[]
        {
            Point(0, 0),
            Point(40, 0),
            Point(60, 0),
            Point(100, 0),
            Point(100, 100),
            Point(60, 100),
            Point(40, 100),
            Point(0, 100),
        };
        Assert.False(OutlineContourMatcher.TryExactMatch(coarse, loop));
    }

    private static Point2D Point(int x, int y) => Point2D.FromIu(x, y);

    private static IEnumerable<Point2D> ArcDegrees(
        int centreX,
        int centreY,
        int radius,
        int fromDegrees,
        int toDegrees,
        int stepDegrees)
    {
        var step = stepDegrees == 0 ? 1 : stepDegrees;
        var end = toDegrees;
        for (var angle = fromDegrees;
             step > 0 ? angle <= end : angle >= end;
             angle += step)
        {
            var radians = angle * Math.PI / 180.0;
            yield return Point(
                (int)Math.Round(centreX + (radius * Math.Cos(radians)), MidpointRounding.AwayFromZero),
                (int)Math.Round(centreY + (radius * Math.Sin(radians)), MidpointRounding.AwayFromZero));
        }
    }

    private static OutlineSegment Segment((int X, int Y) start, (int X, int Y) end) =>
        new(Point(start.X, start.Y), Point(end.X, end.Y), default, 0, null);

    private static OutlineSegment Arc(
        (int X, int Y) start,
        (int X, int Y) end,
        (int X, int Y) centre,
        int radius,
        ArcSweepDirection direction) =>
        new(Point(start.X, start.Y), Point(end.X, end.Y), Point(centre.X, centre.Y), radius, direction);
}
