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
            BoardCutoutRole.BoardShape,
            BoardOutlineTopology.ClassifyRegionContour(OnOuterEdge(10, 40), new[] { outer }));
        Assert.Equal(
            BoardCutoutRole.Hole,
            BoardOutlineTopology.ClassifyRegionContour(Rectangle(20, 20, 30, 30), new[] { outer }));
        Assert.Equal(
            BoardCutoutRole.Unrelated,
            BoardOutlineTopology.ClassifyRegionContour(Rectangle(120, 20, 130, 30), new[] { outer }));
    }

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

    private static Contour Rectangle(double x0, double y0, double x1, double y1)
    {
        var p0 = Point(x0, y0);
        var p1 = Point(x1, y0);
        var p2 = Point(x1, y1);
        var p3 = Point(x0, y1);
        return new Contour
        {
            Id = "rect",
            Role = ContourRole.Outer,
            Segments = new ContourSegment[]
            {
                new LineSegment { Start = p0, End = p1 },
                new LineSegment { Start = p1, End = p2 },
                new LineSegment { Start = p2, End = p3 },
                new LineSegment { Start = p3, End = p0 },
            },
        };
    }

    private static Point2D Point(double xMm, double yMm) =>
        Point2D.FromIu(AltiumUnits.MmToIu(xMm), AltiumUnits.MmToIu(yMm));
}
