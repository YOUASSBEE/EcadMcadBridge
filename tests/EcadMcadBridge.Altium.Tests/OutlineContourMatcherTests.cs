using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class OutlineContourMatcherTests
{
    [Fact]
    public void ExactMatch_AcceptsFullContourWithDifferentStart()
    {
        var contour = RectanglePoints(0, 0, 100, 100);
        var loop = Loop((100, 0), (100, 100), (0, 100), (0, 0));

        Assert.True(OutlineContourMatcher.TryExactMatch(contour, loop));
    }

    [Fact]
    public void ExactMatch_RejectsSingleSharedPoint()
    {
        var contour = RectanglePoints(0, 0, 100, 100);
        var loop = Loop((0, 0), (200, 0), (200, 200), (0, 200));

        Assert.False(OutlineContourMatcher.TryExactMatch(contour, loop));
    }

    [Fact]
    public void ExactMatch_RejectsPartialGeometry()
    {
        var contour = RectanglePoints(0, 0, 100, 100);
        var loop = Loop((0, 0), (100, 0), (100, 100));

        Assert.False(OutlineContourMatcher.TryExactMatch(contour, loop));
    }

    [Fact]
    public void FindExactMatches_IdentifiesAmbiguousDuplicateMatches()
    {
        var contour = RectanglePoints(0, 0, 100, 100);
        var loop = Loop((0, 0), (100, 0), (100, 100), (0, 100));

        var matches = OutlineContourMatcher.FindExactMatches(
            contour,
            new IReadOnlyList<OutlineSegment>[] { loop, loop });

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void UniquePoints_DoNotDoubleCountAdjacentStartAndEnd()
    {
        var contour = RectanglePoints(0, 0, 100, 100);
        var loop = Loop((0, 0), (100, 0), (100, 100), (0, 100));
        var points = loop.SelectMany(segment => new[] { segment.Start, segment.End }).ToArray();

        Assert.Equal(8, points.Length);
        Assert.Equal(4, OutlineContourMatcher.CountUniqueSharedPoints(contour, points));
    }

    [Fact]
    public void ExactMatch_RejectsOpenLoop()
    {
        var contour = RectanglePoints(0, 0, 100, 100);
        var loop = new[]
        {
            Segment((0, 0), (100, 0)),
            Segment((100, 0), (100, 100)),
            Segment((100, 100), (0, 100)),
        };

        Assert.False(OutlineContourMatcher.TryExactMatch(contour, loop));
        Assert.False(OutlineContourMatcher.IsClosed(loop));
    }

    [Fact]
    public void ArcPointOutsideAngularRange_IsNotAccepted()
    {
        var loop = new[]
        {
            Arc((100, 0), (0, 100), (0, 0), 100, ArcSweepDirection.CounterClockwise),
            Segment((0, 100), (100, 0)),
        };

        Assert.True(OutlineContourMatcher.IsPointOnLoop(Point2D.FromIu(70, 70), loop));
        Assert.False(OutlineContourMatcher.IsPointOnLoop(Point2D.FromIu(0, -100), loop));
    }

    [Fact]
    public void ArcPointOutsideCWRange_IsNotAccepted()
    {
        var loop = new[]
        {
            Arc((100, 0), (0, -100), (0, 0), 100, ArcSweepDirection.Clockwise),
            Segment((0, -100), (100, 0)),
        };

        Assert.True(OutlineContourMatcher.IsPointOnLoop(Point2D.FromIu(70, -70), loop));
        Assert.False(OutlineContourMatcher.IsPointOnLoop(Point2D.FromIu(0, 100), loop));
    }

    [Fact]
    public void ArcCrossingZeroDegrees_UsesAngularRange()
    {
        var loop = new[]
        {
            Arc((0, -100), (100, 0), (0, 0), 100, ArcSweepDirection.CounterClockwise),
            Segment((100, 0), (0, -100)),
        };

        Assert.True(OutlineContourMatcher.IsPointOnLoop(Point2D.FromIu(70, -70), loop));
        Assert.False(OutlineContourMatcher.IsPointOnLoop(Point2D.FromIu(-70, -70), loop));
    }

    [Fact]
    public void ArcEndpoints_AreOnTheArc()
    {
        var loop = new[]
        {
            Arc((100, 0), (0, 100), (0, 0), 100, ArcSweepDirection.CounterClockwise),
            Segment((0, 100), (100, 0)),
        };

        Assert.True(OutlineContourMatcher.IsPointOnLoop(Point2D.FromIu(100, 0), loop));
        Assert.True(OutlineContourMatcher.IsPointOnLoop(Point2D.FromIu(0, 100), loop));
    }

    [Fact]
    public void ExactMatch_RejectsPolylineDeviationBetweenVertices()
    {
        var contour = new[]
        {
            Point2D.FromIu(100, 0),
            Point2D.FromIu(0, 100),
            Point2D.FromIu(50, 100),
        };
        var loop = new[]
        {
            Arc((100, 0), (0, 100), (0, 0), 100, ArcSweepDirection.CounterClockwise),
            Segment((0, 100), (50, 100)),
            Segment((50, 100), (100, 0)),
        };

        Assert.False(OutlineContourMatcher.TryExactMatch(contour, loop));
    }

    [Fact]
    public void IsClosed_DetectsClosedPointSequence()
    {
        var closed = RectanglePoints(0, 0, 100, 100).Concat(new[] { Point2D.FromIu(0, 0) }).ToArray();
        var open = closed[..^1];

        Assert.True(OutlineContourMatcher.IsClosed(closed));
        Assert.False(OutlineContourMatcher.IsClosed(open));
    }

    private static Point2D[] RectanglePoints(int x1, int y1, int x2, int y2) =>
    [
        Point2D.FromIu(x1, y1),
        Point2D.FromIu(x2, y1),
        Point2D.FromIu(x2, y2),
        Point2D.FromIu(x1, y2),
    ];

    private static OutlineSegment[] Loop(params (int X, int Y)[] points)
    {
        var result = new OutlineSegment[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var next = points[(i + 1) % points.Length];
            result[i] = Segment(points[i], next);
        }

        return result;
    }

    private static OutlineSegment Segment((int X, int Y) start, (int X, int Y) end) =>
        new(
            Point2D.FromIu(start.X, start.Y),
            Point2D.FromIu(end.X, end.Y),
            default,
            0,
            null);

    private static OutlineSegment Arc(
        (int X, int Y) start,
        (int X, int Y) end,
        (int X, int Y) center,
        int radius,
        ArcSweepDirection direction) =>
        new(
            Point2D.FromIu(start.X, start.Y),
            Point2D.FromIu(end.X, end.Y),
            Point2D.FromIu(center.X, center.Y),
            radius,
            direction);
}
