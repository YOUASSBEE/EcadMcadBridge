using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class RegionContourRecoveryTests
{
    private const int RegionIndex = 7;

    [Fact]
    public void NullGeometricContour_ExactRetryAndFallback_IsRecovered()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.RecoverRegionContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: new[] { Contour(Rectangle(0, 0, 10, 10), "RegionGeometricPolygon", 0, contourIndex: 0) },
            skipped: new[] { new SkippedRegionContour(1, false, Array.Empty<Point2D>()) },
            fallback: new[]
            {
                Contour(ReverseRing(20, 20, 30, 30), "MainContour", 0, isHole: false),
            },
            reReadContour: _ => new RegionContourProbe(false, Rectangle(20, 20, 30, 30)),
            warnings,
            diagnostics);

        Assert.Equal(2, result.Count);
        var recovered = result[1];
        Assert.Equal("MainContour", recovered.Source);
        Assert.Equal(0, recovered.SourceIndex);
        Assert.Equal(1, recovered.ContourIndex);
        Assert.False(recovered.IsHole);
        Assert.DoesNotContain(skippedKey, diagnostics.UnresolvedCutoutSources);
        Assert.Equal(1, diagnostics.RegionContourRecoveredCount);
        Assert.Equal(0, diagnostics.RegionContourRecoveryDuplicateCount);
        Assert.Contains(
            diagnostics.ReadIssues,
            issue => issue.Kind == AltiumReadIssueKind.RegionContourRecovered);
    }

    [Fact]
    public void SinglePointGeometricContour_ExactRetryAndFallback_IsRecovered()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 2);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.RecoverRegionContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: Array.Empty<AltiumRegionContour>(),
            skipped: new[]
            {
                new SkippedRegionContour(2, true, new[] { Point2D.FromIu(25, 25) }),
            },
            fallback: new[]
            {
                Contour(ReverseRing(20, 20, 30, 30), "HoleContour", 1, isHole: true),
            },
            reReadContour: _ => new RegionContourProbe(true, Rectangle(20, 20, 30, 30)),
            warnings,
            diagnostics);

        Assert.Single(result);
        Assert.True(result[0].IsHole);
        Assert.Equal("HoleContour", result[0].Source);
        Assert.Equal(1, result[0].SourceIndex);
        Assert.Equal(2, result[0].ContourIndex);
        Assert.DoesNotContain(skippedKey, diagnostics.UnresolvedCutoutSources);
    }

    [Fact]
    public void RetryUnavailable_LeavesSourceUnresolved()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.RecoverRegionContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: new[] { Contour(Rectangle(0, 0, 10, 10), "RegionGeometricPolygon", 0, contourIndex: 0) },
            skipped: new[] { new SkippedRegionContour(1, false, Array.Empty<Point2D>()) },
            fallback: new[]
            {
                Contour(ReverseRing(20, 20, 30, 30), "MainContour", 0, isHole: false),
            },
            reReadContour: _ => null,
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
    public void RetryThrows_LeavesSourceUnresolved()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.RecoverRegionContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: Array.Empty<AltiumRegionContour>(),
            skipped: new[] { new SkippedRegionContour(1, false, Array.Empty<Point2D>()) },
            fallback: new[]
            {
                Contour(ReverseRing(20, 20, 30, 30), "MainContour", 0, isHole: false),
            },
            reReadContour: _ => throw new InvalidOperationException("SDK read failed."),
            warnings,
            diagnostics);

        Assert.Empty(result);
        Assert.Contains(skippedKey, diagnostics.UnresolvedCutoutSources);
        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void RetryStillShort_LeavesSourceUnresolved()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.RecoverRegionContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: Array.Empty<AltiumRegionContour>(),
            skipped: new[] { new SkippedRegionContour(1, false, Array.Empty<Point2D>()) },
            fallback: new[]
            {
                Contour(ReverseRing(20, 20, 30, 30), "MainContour", 0, isHole: false),
            },
            reReadContour: _ => new RegionContourProbe(false, new[] { Point2D.FromIu(25, 25) }),
            warnings,
            diagnostics);

        Assert.Empty(result);
        Assert.Contains(skippedKey, diagnostics.UnresolvedCutoutSources);
        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void FallbackMissing_LeavesSourceUnresolved()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.RecoverRegionContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: Array.Empty<AltiumRegionContour>(),
            skipped: new[] { new SkippedRegionContour(1, false, Array.Empty<Point2D>()) },
            fallback: Array.Empty<AltiumRegionContour>(),
            reReadContour: _ => new RegionContourProbe(false, Rectangle(20, 20, 30, 30)),
            warnings,
            diagnostics);

        Assert.Empty(result);
        Assert.Contains(skippedKey, diagnostics.UnresolvedCutoutSources);
        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void SameCoordinatesDifferentRole_IsNotAcceptedAsFallback()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.RecoverRegionContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: Array.Empty<AltiumRegionContour>(),
            skipped: new[] { new SkippedRegionContour(1, true, Array.Empty<Point2D>()) },
            fallback: new[]
            {
                Contour(ReverseRing(20, 20, 30, 30), "MainContour", 0, isHole: false),
            },
            reReadContour: _ => new RegionContourProbe(true, Rectangle(20, 20, 30, 30)),
            warnings,
            diagnostics);

        Assert.Empty(result);
        Assert.Contains(skippedKey, diagnostics.UnresolvedCutoutSources);
        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void SameCoordinatesDifferentRole_IsNotTreatedAsDuplicate()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.RecoverRegionContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: new[] { Contour(Rectangle(20, 20, 30, 30), "RegionGeometricPolygon", 0, contourIndex: 0) },
            skipped: new[] { new SkippedRegionContour(1, true, Array.Empty<Point2D>()) },
            fallback: new[]
            {
                Contour(ReverseRing(20, 20, 30, 30), "HoleContour", 0, isHole: true),
            },
            reReadContour: _ => new RegionContourProbe(true, Rectangle(20, 20, 30, 30)),
            warnings,
            diagnostics);

        Assert.Equal(2, result.Count);
        Assert.True(result[1].IsHole);
        Assert.Equal(1, result[1].ContourIndex);
        Assert.Equal(1, diagnostics.RegionContourRecoveredCount);
        Assert.Equal(0, diagnostics.RegionContourRecoveryDuplicateCount);
        Assert.DoesNotContain(skippedKey, diagnostics.DuplicateCutoutSources);
    }

    [Fact]
    public void ExactDuplicateWithSameRole_IsAccountedWithoutSecondExport()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var skippedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        diagnostics.UnresolvedCutoutSources.Add(skippedKey);
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.RecoverRegionContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: new[] { Contour(Rectangle(0, 0, 10, 10), "RegionGeometricPolygon", 0, contourIndex: 0) },
            skipped: new[] { new SkippedRegionContour(1, false, Array.Empty<Point2D>()) },
            fallback: new[]
            {
                Contour(ReverseRing(0, 0, 10, 10), "MainContour", 0, isHole: false),
            },
            reReadContour: _ => new RegionContourProbe(false, Rectangle(0, 0, 10, 10)),
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
    }

    [Fact]
    public void MixedSkippedContours_RecoveredAndUnresolvedFailClosed()
    {
        var diagnostics = new AltiumAdapterDiagnostics();
        var recoveredKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 1);
        var unresolvedKey = AltiumBoardSnapshotAdapter.SkippedContourKey(RegionIndex, 2);
        diagnostics.UnresolvedCutoutSources.Add(recoveredKey);
        diagnostics.UnresolvedCutoutSources.Add(unresolvedKey);
        var warnings = new List<string>();

        var result = AltiumBoardSnapshotAdapter.RecoverRegionContours(
            RegionIndex,
            isBoardCutout: true,
            geometricContours: new[] { Contour(Rectangle(0, 0, 10, 10), "RegionGeometricPolygon", 0, contourIndex: 0) },
            skipped: new[]
            {
                new SkippedRegionContour(1, false, Array.Empty<Point2D>()),
                new SkippedRegionContour(2, false, Array.Empty<Point2D>()),
            },
            fallback: new[]
            {
                Contour(ReverseRing(20, 20, 30, 30), "MainContour", 0, isHole: false),
            },
            reReadContour: index => index == 1
                ? new RegionContourProbe(false, Rectangle(20, 20, 30, 30))
                : null,
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

    private static AltiumRegionContour Contour(
        IReadOnlyList<Point2D> points,
        string source,
        int sourceIndex,
        bool isHole = false,
        int contourIndex = -1) =>
        new(isHole, points, source, sourceIndex, contourIndex);

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
