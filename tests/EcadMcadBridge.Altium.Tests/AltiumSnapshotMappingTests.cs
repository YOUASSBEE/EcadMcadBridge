using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using PCB;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class AltiumSnapshotMappingTests
{
    [Fact]
    public void TrackMapping_PreservesUnitsAndReferences()
    {
        var track = AltiumSnapshotMapping.MapTrack(
            "track-1",
            100,
            200,
            300,
            400,
            50,
            "layer-top",
            "net-1");

        Assert.NotNull(track);
        Assert.Equal("track-1", track!.Id);
        Assert.Equal("net-1", track.NetId);
        Assert.Equal("layer-top", track.LayerId);
        Assert.Equal(100, track.Start.XIu);
        Assert.Equal(400, track.End.YIu);
        Assert.Equal(50, track.Width.Iu);
        Assert.Equal(AltiumUnits.IuToMm(50), track.Width.Mm!.Value, 9);
    }

    [Fact]
    public void TrackMapping_RejectsZeroLengthAndBadWidth()
    {
        Assert.Null(AltiumSnapshotMapping.MapTrack("a", 1, 1, 1, 1, 10, "layer", null));
        Assert.Null(AltiumSnapshotMapping.MapTrack("b", 1, 1, 2, 2, 0, "layer", null));
    }

    [Fact]
    public void ViaMapping_PreservesSizesAndLayerReferences()
    {
        var via = AltiumSnapshotMapping.MapVia("via-1", 10, 20, 100, 50, "top", "bottom", "net-1");

        Assert.NotNull(via);
        Assert.Equal("via-1", via!.Id);
        Assert.Equal("net-1", via.NetId);
        Assert.Equal("top", via.StartLayerId);
        Assert.Equal("bottom", via.EndLayerId);
        Assert.Equal(100, via.Diameter.Iu);
        Assert.Equal(50, via.HoleDiameter.Iu);
    }

    [Fact]
    public void ViaMapping_RejectsInvalidHoleAndSize()
    {
        Assert.Null(AltiumSnapshotMapping.MapVia("a", 0, 0, 0, 0, "top", "bottom", null));
        Assert.Null(AltiumSnapshotMapping.MapVia("b", 0, 0, 100, 101, "top", "bottom", null));
    }

    [Theory]
    [InlineData(TRegionKind.eRegionKind_Copper, false, RegionKind.Copper, false)]
    [InlineData(TRegionKind.eRegionKind_Copper, true, RegionKind.KeepOut, false)]
    [InlineData(TRegionKind.eRegionKind_Cutout, false, RegionKind.Other, false)]
    [InlineData(TRegionKind.eRegionKind_Cavity, false, RegionKind.Other, false)]
    [InlineData((TRegionKind)999, false, RegionKind.Other, true)]
    public void RegionKindMapping_IsExplicit(
        TRegionKind source,
        bool keepout,
        RegionKind expected,
        bool expectedUnknown)
    {
        var actual = AltiumSnapshotMapping.MapRegionKind(source, keepout, out var unknown);

        Assert.Equal(expected, actual);
        Assert.Equal(expectedUnknown, unknown);
    }

    [Fact]
    public void RegionContours_PreserveOuterAndHoleTopology()
    {
        var contours = new[]
        {
            new AltiumRegionContour(false, Rectangle(0, 0, 100, 100)),
            new AltiumRegionContour(true, Rectangle(20, 20, 40, 40)),
        };

        var mapped = AltiumSnapshotMapping.MapRegionContours(7, contours, out var errors);

        Assert.Equal(2, mapped.Count);
        Assert.Equal(0, errors);
        Assert.Equal(ContourRole.Outer, mapped[0].Role);
        Assert.Equal(ContourRole.Hole, mapped[1].Role);
        Assert.Equal(4, mapped[0].Segments.Count);
        Assert.Equal(4, mapped[1].Segments.Count);
        Assert.Equal("altium:region:7:contour:0", mapped[0].Id);
        Assert.Equal("altium:region:7:contour:1", mapped[1].Id);
        Assert.Equal(mapped[0].Segments[^1].End, mapped[0].Segments[0].Start);
        Assert.Equal(mapped[1].Segments[^1].End, mapped[1].Segments[0].Start);
    }

    [Fact]
    public void RegionContours_RejectDegenerateContourAndKeepValidOne()
    {
        var contours = new[]
        {
            new AltiumRegionContour(false, Rectangle(0, 0, 100, 100)),
            new AltiumRegionContour(true, new[] { Point(20, 20), Point(30, 30) }),
        };

        var mapped = AltiumSnapshotMapping.MapRegionContours(3, contours, out var errors);

        Assert.Single(mapped);
        Assert.Equal(1, errors);
    }

    [Fact]
    public void TextMapping_PreservesPositionLayerAndRotation()
    {
        var text = AltiumSnapshotMapping.MapText(
            "text-1",
            "component-1",
            "layer-silk",
            100,
            200,
            "R1",
            50,
            10,
            450,
            out var rotationKnown);

        Assert.True(rotationKnown);
        Assert.Equal("text-1", text.Id);
        Assert.Equal("component-1", text.ComponentId);
        Assert.Equal("layer-silk", text.LayerId);
        Assert.Equal(100, text.Position.XIu);
        Assert.Equal(200, text.Position.YIu);
        Assert.Equal(50, text.Height.Iu);
        Assert.Equal(10, text.StrokeWidth.Iu);
        Assert.Equal(AltiumUnits.IuToMm(50), text.Height.Mm!.Value, 9);
        Assert.Equal(90, text.RotationDeg);
    }

    [Fact]
    public void TextMapping_DoesNotInventRotation()
    {
        var text = AltiumSnapshotMapping.MapText(
            "text-1",
            null,
            "layer-silk",
            0,
            0,
            "X",
            10,
            1,
            double.PositiveInfinity,
            out var rotationKnown);

        Assert.False(rotationKnown);
        Assert.Null(text.RotationDeg);
    }

    [Fact]
    public void MakeId_UsesUniqueIdAndDeterministicFallback()
    {
        var warnings = new List<string>();
        var first = AltiumBoardSnapshotAdapter.MakeId("track", "uid-1", "index:0", warnings);
        var second = AltiumBoardSnapshotAdapter.MakeId("track", null, "index:1", warnings);

        Assert.Equal("altium:track:uid-1", first);
        Assert.Equal("altium:track:fallback:index:1", second);
        Assert.Single(warnings);
    }

    [Fact]
    public void EnsureUniqueId_DisambiguatesDuplicatesDeterministically()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var first = AltiumBoardSnapshotAdapter.EnsureUniqueId("altium:track:x", ids, 0);
        var second = AltiumBoardSnapshotAdapter.EnsureUniqueId("altium:track:x", ids, 1);
        var third = AltiumBoardSnapshotAdapter.EnsureUniqueId("altium:track:x", ids, 2);

        Assert.Equal("altium:track:x", first);
        Assert.Equal("altium:track:x:dup:1", second);
        Assert.Equal("altium:track:x:dup:2", third);
        Assert.Equal(3, ids.Count);
    }

    private static Point2D[] Rectangle(int x1, int y1, int x2, int y2) =>
        new[] { Point(x1, y1), Point(x2, y1), Point(x2, y2), Point(x1, y2) };

    private static Point2D Point(int x, int y) => Point2D.FromIu(x, y);
}
