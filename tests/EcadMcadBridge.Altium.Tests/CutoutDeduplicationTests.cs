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

    private static CutoutExistingHole Hole(int index, params (int X, int Y)[] points) =>
        new(index, Points(points));

    private static Point2D[] Points(params (int X, int Y)[] points) =>
        points.Select(point => Point2D.FromIu(point.X, point.Y)).ToArray();

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
