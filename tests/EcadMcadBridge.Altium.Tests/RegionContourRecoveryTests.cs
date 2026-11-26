using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class RegionContourRecoveryTests
{
    private const int RegionIndex = 7;

    [Fact]
    public void ContourPointsEqual_IgnoresClosureStartPointAndDirection()
    {
        Assert.True(AltiumBoardSnapshotAdapter.ContourPointsEqual(
            Rectangle(20, 20, 30, 30),
            ReverseRing(20, 20, 30, 30)));
        Assert.True(AltiumBoardSnapshotAdapter.ContourPointsEqual(
            ReverseRing(20, 20, 30, 30),
            Rectangle(20, 20, 30, 30)));
        Assert.False(AltiumBoardSnapshotAdapter.ContourPointsEqual(
            Rectangle(20, 20, 30, 30),
            Rectangle(40, 40, 50, 50)));
    }

    [Fact]
    public void SkippedContour_ExactFallbackRecovery_IsForwardedAndAccounted()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.ReconcileFallbackContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: new[] { Contour(Rectangle(0, 0, 10, 10), "RegionGeometricPolygon", 0) },
            skipped: new[] { new SkippedRegionContour(1, Rectangle(20, 20, 30, 30)) },
            fallback: new[]
            {
                Contour(ReverseRing(20, 20, 30, 30), "MainContour", 0, isHole: false),
            },
            warnings,
            diagnostics);

        Assert.Equal(2, result.Count);
        Assert.Equal("MainContour", result[1].Source);
        Assert.Equal(0, result[1].SourceIndex);
        Assert.False(result[1].IsHole);
        Assert.Equal(5, result[1].Points.Count);
        Assert.DoesNotContain(skippedKey, diagnostics.UnresolvedCutoutSources);
        Assert.Equal(1, diagnostics.RegionContourRecoveredCount);
        Assert.Equal(0, diagnostics.RegionContourRecoveryDuplicateCount);
        Assert.Contains(
            diagnostics.ReadIssues,
            issue => issue.Kind == AltiumReadIssueKind.RegionContourRecovered);
        Assert.Contains(warnings, warning => warning.Contains("forwarded for geometry processing"));
    }

    [Fact]
    public void FallbackExactDuplicateOfExistingContour_IsAccountedWithoutSecondExport()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.ReconcileFallbackContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: new[] { Contour(Rectangle(0, 0, 10, 10), "RegionGeometricPolygon", 0) },
            skipped: new[] { new SkippedRegionContour(1, Rectangle(0, 0, 10, 10)) },
            fallback: new[]
            {
                Contour(ReverseRing(0, 0, 10, 10), "MainContour", 0, isHole: false),
            },
            warnings,
            diagnostics);

        Assert.Single(result);
        Assert.Contains(skippedKey, diagnostics.DuplicateCutoutSources);
        Assert.DoesNotContain(skippedKey, diagnostics.UnresolvedCutoutSources);
        Assert.Equal(0, diagnostics.RegionContourRecoveredCount);
        Assert.Equal(1, diagnostics.RegionContourRecoveryDuplicateCount);
        Assert.Contains(
            diagnostics.ReadIssues,
            issue => issue.Kind == AltiumReadIssueKind.RegionContourRecoveryDuplicate);
        Assert.Contains(warnings, warning => warning.Contains("no second contour was exported"));
    }

    [Fact]
    public void FallbackDoesNotReproduceSkippedContour_RemainsIncomplete()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        diagnostics.BoardCutoutContours = 1;
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.ReconcileFallbackContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: new[] { Contour(Rectangle(0, 0, 10, 10), "RegionGeometricPolygon", 0) },
            skipped: new[] { new SkippedRegionContour(1, Rectangle(20, 20, 30, 30)) },
            fallback: new[]
            {
                Contour(Rectangle(40, 40, 50, 50), "MainContour", 0, isHole: false),
            },
            warnings,
            diagnostics);

        Assert.Single(result);
        Assert.Contains(skippedKey, diagnostics.UnresolvedCutoutSources);
        Assert.Equal(1, diagnostics.BoardCutoutUnprovenSkippedContours);
        Assert.Contains(
            diagnostics.ReadIssues,
            issue => issue.Kind == AltiumReadIssueKind.RegionContourUnproven);
        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void FallbackReadFailure_KeepsWholeReadSourceUnresolved()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var wholeReadKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, -1);
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        diagnostics.UnresolvedCutoutSources.Add(wholeReadKey);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        diagnostics.BoardCutoutContourReadErrors = 1;
        diagnostics.BoardCutoutContours = 1;
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.ReconcileFallbackContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: new[] { Contour(Rectangle(0, 0, 10, 10), "RegionGeometricPolygon", 0) },
            skipped: new[] { new SkippedRegionContour(1, Rectangle(20, 20, 30, 30)) },
            fallback: Array.Empty<AltiumRegionContour>(),
            warnings,
            diagnostics);

        Assert.Single(result);
        Assert.Contains(wholeReadKey, diagnostics.UnresolvedCutoutSources);
        Assert.Contains(skippedKey, diagnostics.UnresolvedCutoutSources);
        Assert.Equal(1, diagnostics.BoardCutoutUnprovenSkippedContours);
        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void RecoveredHoleContour_PreservesHoleFlagAndSourceIndex()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 0);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.ReconcileFallbackContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: Array.Empty<AltiumRegionContour>(),
            skipped: new[] { new SkippedRegionContour(0, Rectangle(20, 20, 30, 30)) },
            fallback: new[]
            {
                Contour(ReverseRing(20, 20, 30, 30), "HoleContour", 2, isHole: true),
            },
            warnings,
            diagnostics);

        Assert.Single(result);
        Assert.True(result[0].IsHole);
        Assert.Equal("HoleContour", result[0].Source);
        Assert.Equal(2, result[0].SourceIndex);
        Assert.DoesNotContain(skippedKey, diagnostics.UnresolvedCutoutSources);
    }

    [Fact]
    public void MixedSkippedContours_RecoveredForwardedAndUnresolvedFailClosed()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var recoveredKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        var unresolvedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 2);
        diagnostics.UnresolvedCutoutSources.Add(recoveredKey);
        diagnostics.UnresolvedCutoutSources.Add(unresolvedKey);
        diagnostics.BoardCutoutContours = 2;
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.ReconcileFallbackContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: new[] { Contour(Rectangle(0, 0, 10, 10), "RegionGeometricPolygon", 0) },
            skipped: new[]
            {
                new SkippedRegionContour(1, Rectangle(20, 20, 30, 30)),
                new SkippedRegionContour(2, Rectangle(40, 40, 50, 50)),
            },
            fallback: new[]
            {
                Contour(ReverseRing(20, 20, 30, 30), "MainContour", 0, isHole: false),
            },
            warnings,
            diagnostics);

        Assert.Equal(2, result.Count);
        Assert.Equal("MainContour", result[1].Source);
        Assert.DoesNotContain(recoveredKey, diagnostics.UnresolvedCutoutSources);
        Assert.Contains(unresolvedKey, diagnostics.UnresolvedCutoutSources);
        Assert.Equal(1, diagnostics.RegionContourRecoveredCount);
        Assert.Equal(1, diagnostics.BoardCutoutUnprovenSkippedContours);
        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    private static AltiumRegionContour Contour(
        IReadOnlyList<Point2D> points,
        string source,
        int sourceIndex,
        bool isHole = false) =>
        new(isHole, points, source, sourceIndex);

    private static Point2D[] Rectangle(int x0, int y0, int x1, int y1) =>
        new[]
        {
            Point2D.FromIu(x0, y0),
            Point2D.FromIu(x1, y0),
            Point2D.FromIu(x1, y1),
            Point2D.FromIu(x0, y1),
        };

    private static Point2D[] ReverseRing(int x0, int y0, int x1, int y1) =>
        new[]
        {
            Point2D.FromIu(x1, y1),
            Point2D.FromIu(x0, y1),
            Point2D.FromIu(x0, y0),
            Point2D.FromIu(x1, y0),
            Point2D.FromIu(x1, y1),
        };
}
