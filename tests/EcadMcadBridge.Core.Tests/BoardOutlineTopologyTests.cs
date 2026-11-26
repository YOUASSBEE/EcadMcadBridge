using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using Xunit;

namespace EcadMcadBridge.Core.Tests;

public sealed class BoardOutlineTopologyTests
{
    [Fact]
    public void ClassifyRegionContour_DistinguishesBoardShapeHoleAndUnrelated()
    {
        var outer = Rectangle(0, 0, 100, 50);

        Assert.Equal(
            BoardCutoutRole.BoardShapeFragment,
            BoardOutlineTopology.ClassifyRegionContour(OnOuterEdge(10, 40), new[] { outer }));
        Assert.Equal(
            BoardCutoutRole.Hole,
            BoardOutlineTopology.ClassifyRegionContour(Rectangle(20, 20, 30, 30), new[] { outer }, isHole: true));
        Assert.Equal(
            BoardCutoutRole.Unrelated,
            BoardOutlineTopology.ClassifyRegionContour(Rectangle(120, 20, 130, 30), new[] { outer }));
    }

    [Fact]
    public void ClassifyRegionContour_ExactOuterLineAndArc_AreBoardShape()
    {
        var lineOuter = Rectangle(0, 0, 100, 50);
        Assert.Equal(
            BoardCutoutRole.BoardShape,
            BoardOutlineTopology.ClassifyRegionContour(Rectangle(0, 0, 100, 50), new[] { lineOuter }));

        var arcOuter = QuarterDisc("arc-outer");
        Assert.Equal(
            BoardCutoutRole.BoardShape,
            BoardOutlineTopology.ClassifyRegionContour(QuarterDisc("arc-source"), new[] { arcOuter }));
    }

    [Fact]
    public void ClassifyRegionContour_WithinToleranceOffset_IsStillBoardShape()
    {
        var outer = QuarterDisc("arc-outer");
        var offset = QuarterDisc("arc-offset", dx: 0.005, dy: 0.005);

        Assert.Equal(
            BoardCutoutRole.BoardShape,
            BoardOutlineTopology.ClassifyRegionContour(offset, new[] { outer }));
    }

    [Fact]
    public void ClassifyRegionContour_CoarseChordReplacementOfArc_IsNotBoardShape()
    {
        var outer = QuarterDisc("arc-outer");
        var chord = Polygon("chord", ContourRole.Hole, Point(0, 100), Point(100, 0), Point(0, 0));

        Assert.NotEqual(
            BoardCutoutRole.BoardShape,
            BoardOutlineTopology.ClassifyRegionContour(chord, new[] { outer }, isHole: true));
    }

    [Fact]
    public void ClassifyRegionContour_ConcaveOuterWithOutsideVertexAverage_IsHole()
    {
        var concaveOuter = Polygon(
            "concave-outer",
            ContourRole.Outer,
            Point(0, 0),
            Point(100, 0),
            Point(100, 30),
            Point(30, 30),
            Point(30, 100),
            Point(0, 100));

        // The vertex average of this L-shape is outside the material.
        Assert.False(BoardOutlineTopology.IsPointInside(
            new BoardOutlineTopology.PlanarPoint(260.0 / 6, 260.0 / 6),
            BoardOutlineTopology.Sample(concaveOuter)));

        Assert.Equal(
            BoardCutoutRole.Hole,
            BoardOutlineTopology.ClassifyRegionContour(
                Rectangle(10, 5, 20, 15), new[] { concaveOuter }, isHole: true));
    }

    [Fact]
    public void ClassifyRegionContour_HoleCrossingOuterBoundary_IsNotHole()
    {
        var outer = Rectangle(0, 0, 100, 50);
        var crossing = Polygon(
            "crossing",
            ContourRole.Hole,
            Point(90, 20),
            Point(110, 20),
            Point(110, 30),
            Point(90, 30));

        Assert.NotEqual(
            BoardCutoutRole.Hole,
            BoardOutlineTopology.ClassifyRegionContour(crossing, new[] { outer }, isHole: true));
    }

    [Fact]
    public void ClassifyRegionContour_HoleExitingAndReenteringOuter_IsNotHole()
    {
        var outer = Rectangle(0, 0, 100, 50);
        var spiked = Polygon(
            "spiked",
            ContourRole.Hole,
            Point(20, 20),
            Point(80, 20),
            Point(130, 25),
            Point(80, 30),
            Point(20, 30));

        Assert.NotEqual(
            BoardCutoutRole.Hole,
            BoardOutlineTopology.ClassifyRegionContour(spiked, new[] { outer }, isHole: true));
    }

    [Fact]
    public void ClassifyRegionContour_HoleFlagOnOuter_IsUnsupported()
    {
        var outer = Rectangle(0, 0, 100, 50);

        Assert.Equal(
            BoardCutoutRole.Unsupported,
            BoardOutlineTopology.ClassifyRegionContour(Rectangle(0, 0, 100, 50), new[] { outer }, isHole: true));
    }

    [Fact]
    public void ClassifyRegionContour_MainContourInsideOuter_IsHole()
    {
        var outer = Rectangle(0, 0, 100, 50);

        // In Altium a Board Cutout region's main contour has IsHole=false even though it is a
        // hole in the board; the region's own IsHole flag describes nesting inside that region.
        Assert.Equal(
            BoardCutoutRole.Hole,
            BoardOutlineTopology.ClassifyRegionContour(Rectangle(20, 20, 30, 30), new[] { outer }, isHole: false));
    }

    [Fact]
    public void ClassifyRegionContour_PartialOuterCoverage_IsFragmentNotBoardShape()
    {
        var outer = Rectangle(0, 0, 100, 50);

        Assert.Equal(
            BoardCutoutRole.BoardShapeFragment,
            BoardOutlineTopology.ClassifyRegionContour(OnOuterEdge(10, 40), new[] { outer }));
        Assert.NotEqual(
            BoardCutoutRole.BoardShape,
            BoardOutlineTopology.ClassifyRegionContour(OnOuterEdge(10, 40), new[] { outer }));
    }

    [Fact]
    public void ClassifyRegionContour_SaturatedArcSampling_IsUnsupported()
    {
        var outer = QuarterDisc("arc-outer");
        var fine = QuarterDisc("arc-fine");

        var sampled = BoardOutlineTopology.Sample(fine, 1e-6, out var saturated);
        Assert.True(saturated);
        Assert.NotEmpty(sampled);
        Assert.Equal(
            BoardCutoutRole.Unsupported,
            BoardOutlineTopology.ClassifyRegionContour(fine, new[] { outer }, toleranceMm: 1e-6));
    }

    [Fact]
    public void IsFullyInside_UsesInteriorSamplesAndCrossingChecks()
    {
        var outerPoints = ToPoints(Rectangle(0, 0, 100, 50));
        var innerPoints = ToPoints(Rectangle(20, 20, 30, 30));
        var crossingPoints = ToPoints(Polygon(
            "crossing",
            ContourRole.Hole,
            Point(90, 20),
            Point(110, 20),
            Point(110, 30),
            Point(90, 30)));

        Assert.True(BoardOutlineTopology.IsFullyInside(innerPoints, outerPoints));
        Assert.False(BoardOutlineTopology.IsFullyInside(crossingPoints, outerPoints));
    }

    private static Point2D[] ToPoints(Contour contour) =>
        BoardOutlineTopology.Sample(contour)
            .Select(point => Point2D.FromIu(
                AltiumUnits.MmToIu(point.X),
                AltiumUnits.MmToIu(point.Y)))
            .ToArray();

    [Fact]
    public void ClipToBox_UsesOnlyExistingCoordinates()
    {
        var clipped = BoardOutlineTopology.ClipToBox(Rectangle(0, 0, 100, 50), 25, 10, 75, 40);

        Assert.True(BoardOutlineTopology.TryBounds(clipped, out var minX, out var minY, out var maxX, out var maxY));
        Assert.Equal(25, minX, 10);
        Assert.Equal(10, minY, 10);
        Assert.Equal(75, maxX, 10);
        Assert.Equal(40, maxY, 10);
        Assert.Equal(1500, Math.Abs(BoardOutlineTopology.PolygonArea(clipped)), 10);
    }

    private static Contour OnOuterEdge(double x0, double x1)
    {
        var points = new[]
        {
            Point(x0, 0),
            Point((x0 + x1) / 2.0, 0),
            Point(x1, 0),
            Point(x0, 0),
        };
        return new Contour
        {
            Id = "on-outer-edge",
            Role = ContourRole.Outer,
            Segments = Enumerable.Range(0, points.Length - 1)
                .Select(i => (ContourSegment)new LineSegment { Start = points[i], End = points[i + 1] })
                .ToArray(),
        };
    }

    private static Contour Rectangle(double x0, double y0, double x1, double y1) =>
        Polygon("rect", ContourRole.Outer, Point(x0, y0), Point(x1, y0), Point(x1, y1), Point(x0, y1));

    private static Contour Polygon(string id, ContourRole role, params Point2D[] points)
    {
        var segments = new ContourSegment[points.Length];
        for (var i = 0; i < points.Length; i++)
            segments[i] = new LineSegment { Start = points[i], End = points[(i + 1) % points.Length] };
        return new Contour { Id = id, Role = role, Segments = segments };
    }

    /// <summary>Quarter disc: a clockwise analytic arc plus the two radius lines.</summary>
    private static Contour QuarterDisc(string id, double dx = 0, double dy = 0) =>
        new()
        {
            Id = id,
            Role = ContourRole.Outer,
            Segments = new ContourSegment[]
            {
                new ArcSegment
                {
                    Start = Point(0 + dx, 100 + dy),
                    End = Point(100 + dx, 0 + dy),
                    Center = Point(0 + dx, 0 + dy),
                    Radius = LengthValue.FromIu(AltiumUnits.MmToIu(100)),
                    SweepDirection = ArcSweepDirection.Clockwise,
                },
                new LineSegment { Start = Point(100 + dx, 0 + dy), End = Point(0 + dx, 0 + dy) },
                new LineSegment { Start = Point(0 + dx, 0 + dy), End = Point(0 + dx, 100 + dy) },
            },
        };

    private static Point2D Point(double xMm, double yMm) =>
        Point2D.FromIu(AltiumUnits.MmToIu(xMm), AltiumUnits.MmToIu(yMm));
}
