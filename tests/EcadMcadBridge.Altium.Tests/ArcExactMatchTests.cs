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
    public void ArcSweep_IsInferredFromGeometricTraversal()
    {
        var raw = new[]
        {
            new AltiumOutlineReader.RawSegment(
                TPolySegmentType.ePolySegmentArc,
                0,
                -1000,
                0,
                0,
                1000,
                270,
                0),
        };

        var counterClockwise = AltiumOutlineReader.BuildOutlineSegments(
            raw,
            new[]
            {
                new AltiumOutlineReader.GeometricContour(
                    0,
                    ContourRole.Outer,
                    ArcDegrees(0, 0, 1000, 270, 360, 5).ToArray()),
            });
        var references = new[]
        {
            new AltiumOutlineReader.GeometricContour(
                0,
                ContourRole.Outer,
                ArcDegrees(0, 0, 1000, 270, 0, -5).ToArray()),
        };

        Assert.Equal(
            ArcSweepDirection.CounterClockwise,
            Assert.Single(AltiumOutlineReader.BuildOutlineSegments(raw, new[]
            {
                new AltiumOutlineReader.GeometricContour(
                    0,
                    ContourRole.Outer,
                    ArcDegrees(0, 0, 1000, 270, 360, 5).ToArray()),
            })).SweepDirection);
        Assert.Equal(
            ArcSweepDirection.Clockwise,
            Assert.Single(AltiumOutlineReader.BuildOutlineSegments(raw, references)).SweepDirection);
    }

    [Fact]
    public void LargeRadiusSampledArc_UsesMeasuredSagittaWithoutAcceptingCoarseReplacement()
    {
        const int cx = 606046232;
        const int cy = 388861257;
        const int radius = 5115655;
        var start = PointOnCircle(cx, cy, radius, 112);
        var end = PointOnCircle(cx, cy, radius, 272);
        var centre = Point(cx, cy);
        var loop = new[]
        {
            Arc(AsTuple(start), AsTuple(end), AsTuple(centre), radius, ArcSweepDirection.CounterClockwise),
            Segment(AsTuple(end), AsTuple(centre)),
            Segment(AsTuple(centre), AsTuple(start)),
        };

        var sampled = new List<Point2D>();
        sampled.AddRange(ArcDegrees(cx, cy, radius, 112, 272, 5));
        sampled.Add(centre);

        Assert.True(OutlineContourMatcher.TryExactMatch(sampled, loop));
        Assert.True(OutlineContourMatcher.MatchingTolerance(sampled, loop) > 1000);
        Assert.False(OutlineContourMatcher.TryExactMatch(new[] { start, end, centre }, loop));
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

    private static (int X, int Y) AsTuple(Point2D point) =>
        (point.XIu ?? throw new InvalidOperationException("Missing IU coordinate."),
         point.YIu ?? throw new InvalidOperationException("Missing IU coordinate."));

    private static Point2D PointOnCircle(int cx, int cy, int radius, int degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return Point(
            (int)Math.Round(cx + (radius * Math.Cos(radians)), MidpointRounding.AwayFromZero),
            (int)Math.Round(cy + (radius * Math.Sin(radians)), MidpointRounding.AwayFromZero));
    }

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
