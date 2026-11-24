using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class CutoutDeduplicationTests
{
    [Fact]
    public void A1_ExactHole_IsDuplicateAndIsNotExportedTwice()
    {
        var clipLoop = Loop((0, 0), (100, 0), (100, 100), (0, 100));
        var holes = new[] { Hole(0, (0, 0), (100, 0), (100, 100), (0, 100)) };

        var decision = CutoutDeduplicator.Evaluate(clipLoop, holes);
        var exportable = CutoutDeduplicator.SelectExportableLoops(
            new[] { clipLoop, clipLoop },
            holes);

        Assert.Equal(CutoutDuplicateStatus.Duplicate, decision.Status);
        Assert.False(decision.ShouldExport);
        Assert.Empty(exportable);
    }

    [Fact]
    public void A2_DifferentHole_DoesNotSuppressExport()
    {
        var clipLoop = Loop((0, 0), (100, 0), (100, 100), (0, 100));
        var holes = new[] { Hole(0, (500, 500), (600, 500), (600, 600), (500, 600)) };

        var decision = CutoutDeduplicator.Evaluate(clipLoop, holes);

        Assert.Equal(CutoutDuplicateStatus.NotDuplicate, decision.Status);
        Assert.True(decision.ShouldExport);
    }

    [Fact]
    public void A3_UnrelatedHole_DoesNotSuppressValidCutout()
    {
        var clipLoop = Loop((0, 0), (100, 0), (100, 100), (0, 100));
        var holes = new[] { Hole(0, (1000, 1000), (1100, 1000), (1100, 1100), (1000, 1100)) };

        var exportable = CutoutDeduplicator.SelectExportableLoops(new[] { clipLoop }, holes);

        Assert.Equal(new[] { 0 }, exportable);
    }

    [Fact]
    public void A4_PartialOverlap_IsNotDuplicate()
    {
        var clipLoop = Loop((0, 0), (100, 0), (100, 100), (0, 100));
        var holes = new[] { Hole(0, (0, 0), (100, 0), (100, 50), (0, 50)) };

        var decision = CutoutDeduplicator.Evaluate(clipLoop, holes);

        Assert.Equal(CutoutDuplicateStatus.Partial, decision.Status);
        Assert.True(decision.ShouldExport);
    }

    [Fact]
    public void A5_OnlyMatchingHoleIsReported()
    {
        var clipLoop = Loop((200, 0), (300, 0), (300, 100), (200, 100));
        var holes = new[]
        {
            Hole(0, (0, 0), (100, 0), (100, 100), (0, 100)),
            Hole(1, (200, 0), (300, 0), (300, 100), (200, 100)),
            Hole(2, (400, 0), (500, 0), (500, 100), (400, 100)),
        };

        var decision = CutoutDeduplicator.Evaluate(clipLoop, holes);

        Assert.Equal(CutoutDuplicateStatus.Duplicate, decision.Status);
        Assert.Equal(1, decision.ExistingHoleIndex);
        Assert.False(decision.ShouldExport);
    }

    [Fact]
    public void A6_TessellatedArcHole_IsDuplicateWithinDedupTolerance()
    {
        var clip = QuarterLoop(0);
        var hole = new CutoutExistingHole(0, QuarterPoints(0, 30));

        var decision = CutoutDeduplicator.Evaluate(clip, new[] { hole });

        Assert.Equal(CutoutDuplicateStatus.Duplicate, decision.Status);
        Assert.Contains("tessellated", decision.Reason, StringComparison.Ordinal);
        Assert.False(decision.ShouldExport);
    }

    [Fact]
    public void A7_ArcHoleBeyondDedupTolerance_IsNotDuplicate()
    {
        var clip = QuarterLoop(0);
        var hole = new CutoutExistingHole(0, QuarterPoints(0, 30, radius: 105));

        var decision = CutoutDeduplicator.Evaluate(clip, new[] { hole });

        Assert.NotEqual(CutoutDuplicateStatus.Duplicate, decision.Status);
        Assert.True(decision.ShouldExport);
    }

    [Fact]
    public void A8_AdjacentArcCutouts_AreNotDeduplicatedTogether()
    {
        var first = QuarterLoop(0);
        var second = QuarterLoop(3000);
        var existing = new[] { new CutoutExistingHole(0, QuarterPoints(0, 5)) };

        var exportable = CutoutDeduplicator.SelectExportableLoops(
            new[] { first, second },
            existing);

        Assert.Equal(new[] { 1 }, exportable);
    }

    [Fact]
    public void A9_OppositeArcSweep_IsNotDuplicate()
    {
        var clip = QuarterLoop(0);
        var hole = new CutoutExistingHole(0, OppositeArcPoints());

        var decision = CutoutDeduplicator.Evaluate(clip, new[] { hole });

        Assert.NotEqual(CutoutDuplicateStatus.Duplicate, decision.Status);
        Assert.True(decision.ShouldExport);
    }

    [Fact]
    public void A10_MultipleArcsInOneHole_IsDuplicate()
    {
        var clip = NotchedLoop();
        var hole = new CutoutExistingHole(0, NotchedPoints());

        var decision = CutoutDeduplicator.Evaluate(clip, new[] { hole });

        Assert.Equal(CutoutDuplicateStatus.Duplicate, decision.Status);
        Assert.False(decision.ShouldExport);
    }

    private static CutoutExistingHole Hole(int index, params (int X, int Y)[] points) =>
        new(index, Points(points));

    private static Point2D[] Points(params (int X, int Y)[] points) =>
        points.Select(point => Point2D.FromIu(point.X, point.Y)).ToArray();

    private static OutlineSegment[] QuarterLoop(int centreX) =>
        new[]
        {
            Arc((centreX + 100, 0), (centreX, 100), (centreX, 0), 100, ArcSweepDirection.CounterClockwise),
            Segment((centreX, 100), (centreX, 0)),
            Segment((centreX, 0), (centreX + 100, 0)),
        };

    private static List<Point2D> QuarterPoints(int centreX, int stepDegrees, int radius = 100)
    {
        var result = new List<Point2D> { Point(centreX + radius, 0) };
        result.AddRange(ArcDegrees(centreX, 0, radius, stepDegrees, 90, stepDegrees));
        result.Add(Point(centreX, 0));
        return result;
    }

    private static List<Point2D> OppositeArcPoints()
    {
        var result = ArcDegrees(0, 0, 100, 0, -270, -5).ToList();
        result.Add(Point(0, 0));
        return result;
    }

    private static OutlineSegment[] NotchedLoop() =>
        new[]
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

    private static List<Point2D> NotchedPoints()
    {
        var result = new List<Point2D> { Point(0, 0), Point(40, 0) };
        result.AddRange(ArcDegrees(50, 0, 10, 190, 360, 10));
        result.Add(Point(100, 0));
        result.Add(Point(100, 100));
        result.Add(Point(60, 100));
        result.AddRange(ArcDegrees(50, 100, 10, -10, -170, -10));
        result.Add(Point(40, 100));
        result.Add(Point(0, 100));
        return result;
    }

    private static Point2D Point(int x, int y) => Point2D.FromIu(x, y);

    private static OutlineSegment Segment((int X, int Y) start, (int X, int Y) end) =>
        new(Point(start.X, start.Y), Point(end.X, end.Y), default, 0, null);

    private static OutlineSegment Arc(
        (int X, int Y) start,
        (int X, int Y) end,
        (int X, int Y) centre,
        int radius,
        ArcSweepDirection direction) =>
        new(Point(start.X, start.Y), Point(end.X, end.Y), Point(centre.X, centre.Y), radius, direction);

    private static IEnumerable<Point2D> ArcDegrees(
        int centreX,
        int centreY,
        int radius,
        int fromDegrees,
        int toDegrees,
        int stepDegrees)
    {
        for (var angle = fromDegrees;
             stepDegrees > 0 ? angle <= toDegrees : angle >= toDegrees;
             angle += stepDegrees)
        {
            var radians = angle * Math.PI / 180.0;
            yield return Point(
                (int)Math.Round(centreX + (radius * Math.Cos(radians)), MidpointRounding.AwayFromZero),
                (int)Math.Round(centreY + (radius * Math.Sin(radians)), MidpointRounding.AwayFromZero));
        }
    }

    private static OutlineSegment[] Loop(params (int X, int Y)[] points)
    {
        var result = new OutlineSegment[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var next = points[(i + 1) % points.Length];
            result[i] = new OutlineSegment(
                Point2D.FromIu(points[i].X, points[i].Y),
                Point2D.FromIu(next.X, next.Y),
                default,
                0,
                null);
        }

        return result;
    }
}
