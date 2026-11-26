using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class CutoutRegionMatcherTests
{
    [Fact]
    public void B1_SingleCandidate_IsMatched()
    {
        var match = CutoutRegionMatcher.Match(
            RectangleLoop(0, 0, 100, 100),
            new[] { Source(0, 0, Rectangle(0, 0, 100, 100)) });

        Assert.Equal(CutoutRegionMatchStatus.Matched, match.Status);
        Assert.NotNull(match.Source);
        Assert.Equal(0, match.Source!.RegionIndex);
    }

    [Fact]
    public void B2_NoCandidate_IsUnmatched()
    {
        var match = CutoutRegionMatcher.Match(
            RectangleLoop(0, 0, 100, 100),
            new[] { Source(0, 0, Rectangle(500, 500, 600, 600)) });

        Assert.Equal(CutoutRegionMatchStatus.Unmatched, match.Status);
        Assert.Null(match.Source);
    }

    [Fact]
    public void B3_SameBounds_DifferentGeometry_SelectsGeometricMatch()
    {
        var sources = new[]
        {
            Source(0, 0, Rectangle(0, 0, 100, 100)),
            Source(1, 0, (50, 0), (100, 50), (50, 100), (0, 50)),
        };

        var match = CutoutRegionMatcher.Match(RectangleLoop(0, 0, 100, 100), sources);

        Assert.Equal(CutoutRegionMatchStatus.Matched, match.Status);
        Assert.NotNull(match.Source);
        Assert.Equal(0, match.Source!.RegionIndex);
    }

    [Fact]
    public void B4_GeometricallyIndistinguishableCandidates_AreAmbiguous()
    {
        var sources = new[]
        {
            Source(0, 0, Rectangle(0, 0, 100, 100)),
            Source(1, 0, Rectangle(0, 0, 100, 100)),
        };

        var match = CutoutRegionMatcher.Match(RectangleLoop(0, 0, 100, 100), sources);

        Assert.Equal(CutoutRegionMatchStatus.Ambiguous, match.Status);
        Assert.Null(match.Source);
        Assert.Equal(2, match.Candidates.Count);
    }

    [Fact]
    public void B5_AlreadyAssignedContour_IsNotReused()
    {
        var source = Source(0, 0, Rectangle(0, 0, 100, 100));
        var assigned = new HashSet<CutoutSourceKey> { source.Key };

        var match = CutoutRegionMatcher.Match(
            RectangleLoop(0, 0, 100, 100),
            new[] { source },
            assigned);

        Assert.Equal(CutoutRegionMatchStatus.Reused, match.Status);
        Assert.Null(match.Source);
        Assert.Contains("already assigned", match.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void B5b_ExplicitManyToManyContract_AllowsReusedSource()
    {
        var source = Source(0, 0, Rectangle(0, 0, 100, 100));
        var assigned = new HashSet<CutoutSourceKey> { source.Key };

        var match = CutoutRegionMatcher.Match(
            RectangleLoop(0, 0, 100, 100),
            new[] { source },
            assigned,
            allowManyToMany: true);

        Assert.Equal(CutoutRegionMatchStatus.Matched, match.Status);
        Assert.Same(source, match.Source);
    }

    [Fact]
    public void B6_MultipleContoursOfOneRegion_AreMatchedSeparately()
    {
        var sources = new[]
        {
            Source(0, 0, Rectangle(0, 0, 100, 100)),
            Source(0, 1, Rectangle(200, 0, 300, 100)),
        };
        var assigned = new HashSet<CutoutSourceKey>();

        var first = CutoutRegionMatcher.Match(RectangleLoop(0, 0, 100, 100), sources, assigned);
        Assert.Equal(CutoutRegionMatchStatus.Matched, first.Status);
        assigned.Add(first.Source!.Key);

        var second = CutoutRegionMatcher.Match(RectangleLoop(200, 0, 300, 100), sources, assigned);
        Assert.Equal(CutoutRegionMatchStatus.Matched, second.Status);
        Assert.Equal(1, second.Source!.ContourIndex);
        assigned.Add(second.Source!.Key);
        Assert.Equal(2, assigned.Count);
    }

    [Fact]
    public void B7_RegionVertexTolerance_Is3500Iu()
    {
        const int size = 100000;
        var withinDepth = CutoutRegionMatcher.RegionVertexToleranceIu - 100;
        var outsideDepth = CutoutRegionMatcher.RegionVertexToleranceIu + 100;

        var within = CutoutRegionMatcher.Match(
            RectangleLoop(0, 0, size, size),
            new[] { SymmetricNotches(withinDepth) });
        var outside = CutoutRegionMatcher.Match(
            RectangleLoop(0, 0, size, size),
            new[] { SymmetricNotches(outsideDepth) });

        Assert.Equal(CutoutRegionMatchStatus.Matched, within.Status);
        Assert.Equal(CutoutRegionMatchStatus.Unmatched, outside.Status);
    }

    private static CutoutSourceContour SymmetricNotches(int depth) =>
        Source(
            0,
            0,
            (0, 0),
            (50000, depth),
            (100000, 0),
            (100000, 100000),
            (50000, 100000 - depth),
            (0, 100000));

    private static CutoutSourceContour Source(
        int regionIndex,
        int contourIndex,
        params (int X, int Y)[] points) =>
        Source(regionIndex, contourIndex, Points(points));

    private static CutoutSourceContour Source(
        int regionIndex,
        int contourIndex,
        params Point2D[] points)
    {
        var bounds = CutoutRegionMatcher.GetBounds(
            CutoutDeduplicator.EnsureClosed(points));
        return new CutoutSourceContour(
            regionIndex,
            "uid-" + regionIndex,
            "region-" + regionIndex,
            contourIndex,
            false,
            points,
            0,
            bounds.MinX,
            bounds.MinY,
            bounds.MaxX,
            bounds.MaxY);
    }

    private static Point2D[] Rectangle(int x1, int y1, int x2, int y2) =>
        Points((x1, y1), (x2, y1), (x2, y2), (x1, y2));

    private static Point2D[] Points(params (int X, int Y)[] points) =>
        points.Select(point => Point2D.FromIu(point.X, point.Y)).ToArray();

    private static OutlineSegment[] RectangleLoop(int x1, int y1, int x2, int y2) =>
        Loop((x1, y1), (x2, y1), (x2, y2), (x1, y2));

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
