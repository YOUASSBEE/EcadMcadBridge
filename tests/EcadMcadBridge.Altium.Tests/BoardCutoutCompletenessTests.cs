using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class BoardCutoutCompletenessTests
{
    [Fact]
    public void EmptyBoardCutoutRegion_IsIncomplete()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutEmptyRegions = 1;
        diagnostics.AddIssue(
            AltiumReadIssueKind.BoardCutoutRegionEmpty,
            "BoardCutoutRegion",
            0,
            "No usable contour.");

        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void BoardCutoutContourReadError_IsIncomplete()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutContourReadErrors = 1;
        diagnostics.AddIssue(
            AltiumReadIssueKind.RegionContourReadError,
            "RegionGeometricPolygon",
            0,
            "Contour is null.");

        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void UnprovenSkippedContour_IsIncomplete()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutUnprovenSkippedContours = 1;
        diagnostics.UnresolvedCutoutSources.Add(AltiumBoardSnapshotAdapter.SkippedContourKey(0, -1));

        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void InoperableClipperPrimitive_IsIncomplete()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.ClipInoperablePrimsCount = 1;

        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void AmbiguousClippedLoop_IsIncomplete()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.ClipAmbiguousCutoutRegions = 1;
        diagnostics.UnresolvedClipLoops = 1;

        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void UnavailableClipperWithoutProvenSource_IsIncomplete()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutContours = 1;
        diagnostics.ClipResultUnavailableCount = 1;
        diagnostics.UnresolvedCutoutSources.Add(new CutoutSourceKey(0, 0));

        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void UnavailableClipperWithGeometricHoleProof_CanBeComplete()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutContours = 1;
        diagnostics.ClipResultUnavailableCount = 1;
        diagnostics.DuplicateCutoutSources.Add(new CutoutSourceKey(0, 0));

        Assert.Equal(OutlineCompleteness.Complete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void AllCutoutSourcesAccounted_IsComplete()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutContours = 5;
        diagnostics.BoardCutoutShapeContours = 3;
        diagnostics.MatchedCutoutSources.Add(new CutoutSourceKey(1, 0));
        diagnostics.MatchedCutoutSources.Add(new CutoutSourceKey(2, 0));

        Assert.Equal(OutlineCompleteness.Complete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void ClipperReadError_IsIncompleteEvenWhenSourcesAreProven()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutContours = 1;
        diagnostics.ClipCutoutErrors = 1;
        diagnostics.DuplicateCutoutSources.Add(new CutoutSourceKey(0, 0));
        diagnostics.AddIssue(
            AltiumReadIssueKind.ClipSegmentReadError,
            "ClipWithPolyRegShapes",
            -1,
            "Read failed.");

        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void PartialClipMatch_IsIncomplete()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutContours = 1;
        diagnostics.PartialClipMatches = 1;
        diagnostics.MatchedCutoutSources.Add(new CutoutSourceKey(0, 0));

        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void UnmatchedRegionSourceWithClipper_IsIncomplete()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutContours = 1;
        diagnostics.UnresolvedCutoutSources.Add(new CutoutSourceKey(0, 0));
        diagnostics.AddIssue(
            AltiumReadIssueKind.RegionContourUnproven,
            "BoardCutoutSource",
            0,
            "No verified clip loop.");

        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void ExportedCutoutId_DoesNotCollideWithExistingContourId()
    {
        var used = new HashSet<string>(StringComparer.Ordinal)
        {
            "altium:contour:cutout:clip:1",
            "altium:contour:cutout:clip:2",
        };

        Assert.Equal("altium:contour:cutout:clip:3", AltiumOutlineReader.NextCutoutId(used));
        Assert.Contains("altium:contour:cutout:clip:3", used);
    }

    [Fact]
    public void PartialOuterBoundaryFragment_IsAccountedWithoutBlockingCompleteness()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutContours = 3;
        diagnostics.BoardCutoutShapeContours = 2;
        diagnostics.BoardCutoutShapeFragments = 1;

        Assert.Equal(OutlineCompleteness.Complete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void ProvenUnrelatedContours_AreAccountedWithoutBlockingCompleteness()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutContours = 5;
        diagnostics.BoardCutoutShapeContours = 3;
        diagnostics.BoardCutoutUnrelatedContours = 2;

        Assert.Equal(OutlineCompleteness.Complete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void UnaccountedUnrelatedContour_IsIncomplete()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutContours = 1;
        diagnostics.BoardCutoutUnrelatedContours = 0;

        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void SupportedNestedIsland_IsAccountedWithoutBlockingCompleteness()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutContours = 2;
        diagnostics.BoardCutoutNestedIslandContours = 1;
        diagnostics.MatchedCutoutSources.Add(new CutoutSourceKey(0, 0));

        Assert.Equal(OutlineCompleteness.Complete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void UnsupportedNestedSemantics_IsIncomplete()
    {
        var diagnostics = CleanDiagnostics();

        diagnostics.BoardCutoutContours = 3;
        diagnostics.BoardCutoutNestedIslandContours = 1;
        diagnostics.BoardCutoutUnsupportedContours = 1;
        diagnostics.MatchedCutoutSources.Add(new CutoutSourceKey(0, 0));

        Assert.Equal(OutlineCompleteness.Incomplete, AltiumOutlineReader.DeriveCompleteness(diagnostics));
    }

    [Fact]
    public void SkippedContourKey_DoesNotCollideWithRealContourIndex()
    {
        var wholeRead = AltiumBoardSnapshotAdapter.SkippedContourKey(7, -1);
        var skipped = AltiumBoardSnapshotAdapter.SkippedContourKey(7, 0);

        Assert.Equal(7, wholeRead.RegionIndex);
        Assert.True(wholeRead.ContourIndex < 0);
        Assert.True(skipped.ContourIndex < 0);
        Assert.NotEqual(new CutoutSourceKey(7, 0), wholeRead);
        Assert.NotEqual(new CutoutSourceKey(7, 0), skipped);
        Assert.NotEqual(wholeRead, skipped);
    }

    private static AltiumAdapterDiagnostics CleanDiagnostics() => new();
}
