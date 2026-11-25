using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.SolidWorks.Placement;
using Xunit;

namespace EcadMcadBridge.SolidWorks.Tests;

public sealed class BoardOutlineBoundsTests
{
    [Fact]
    public void LineRectangleKeepsExistingBounds()
    {
        var outline = Outline(Outer(
            Line(0, 0, 100, 0),
            Line(100, 0, 100, 50),
            Line(100, 50, 0, 50),
            Line(0, 50, 0, 0)));

        Assert.True(BoardOutlineBounds.TryCompute(outline, out var bounds, out var error), error);
        AssertClose(bounds.MinX, 0);
        AssertClose(bounds.MinY, 0);
        AssertClose(bounds.MaxX, 100);
        AssertClose(bounds.MaxY, 50);
    }

    [Fact]
    public void CounterClockwiseArcDoesNotUseCircleCenterAsBoundary()
    {
        // Center is at y=10; the actual CCW lower half reaches y=5.
        var outline = Outline(Outer(
            Arc(10, 10, 5, 180, 360, ArcSweepDirection.CounterClockwise)));

        Assert.True(BoardOutlineBounds.TryCompute(outline, out var bounds, out var error), error);
        AssertClose(bounds.MinX, 5);
        AssertClose(bounds.MinY, 5);
        AssertClose(bounds.MaxX, 15);
        AssertClose(bounds.MaxY, 10);
    }

    [Fact]
    public void ClockwiseArcDoesNotUseCircleCenterAsBoundary()
    {
        // CW from 0 to 180 goes through the lower cardinal point (270).
        var outline = Outline(Outer(
            Arc(10, 10, 5, 0, 180, ArcSweepDirection.Clockwise)));

        Assert.True(BoardOutlineBounds.TryCompute(outline, out var bounds, out var error), error);
        AssertClose(bounds.MinX, 5);
        AssertClose(bounds.MinY, 5);
        AssertClose(bounds.MaxX, 15);
        AssertClose(bounds.MaxY, 10);
    }

    [Fact]
    public void ShortArcBoundsComeFromActualArcOnly()
    {
        var outline = Outline(Outer(
            Arc(10, 10, 5, 10, 20, ArcSweepDirection.CounterClockwise)));

        Assert.True(BoardOutlineBounds.TryCompute(outline, out var bounds, out var error), error);
        AssertClose(bounds.MinX, 10 + 5 * Math.Cos(20 * Math.PI / 180));
        AssertClose(bounds.MinY, 10 + 5 * Math.Sin(10 * Math.PI / 180));
        AssertClose(bounds.MaxX, 10 + 5 * Math.Cos(10 * Math.PI / 180));
        AssertClose(bounds.MaxY, 10 + 5 * Math.Sin(20 * Math.PI / 180));
        Assert.True(bounds.MinX > 14.6);
        Assert.True(bounds.MinY > 10.8);
    }

    [Fact]
    public void ArcContainingCardinalPointIncludesIt()
    {
        var outline = Outline(Outer(
            Arc(10, 10, 5, 10, 100, ArcSweepDirection.CounterClockwise)));

        Assert.True(BoardOutlineBounds.TryCompute(outline, out var bounds, out var error), error);
        AssertClose(bounds.MaxY, 15);
    }

    [Fact]
    public void ArcWithoutCardinalPointDoesNotIncludeCardinal()
    {
        var outline = Outline(Outer(
            Arc(10, 10, 5, 10, 20, ArcSweepDirection.CounterClockwise)));

        Assert.True(BoardOutlineBounds.TryCompute(outline, out var bounds, out var error), error);
        Assert.True(bounds.MaxY < 15);
        Assert.True(bounds.MinX > 5);
    }

    [Fact]
    public void InnerContourDoesNotChangeOuterBounds()
    {
        var outline = Outline(
            Outer(
                Line(0, 0, 10, 0),
                Line(10, 0, 10, 10),
                Line(10, 10, 0, 10),
                Line(0, 10, 0, 0)),
            new Contour
            {
                Id = "cutout",
                Role = ContourRole.Hole,
                Segments = new ContourSegment[]
                {
                    Line(-100, -100, -90, -100),
                    Line(-90, -100, -90, -90),
                    Line(-90, -90, -100, -90),
                    Line(-100, -90, -100, -100),
                },
            });

        Assert.True(BoardOutlineBounds.TryCompute(outline, out var bounds, out var error), error);
        AssertClose(bounds.MinX, 0);
        AssertClose(bounds.MinY, 0);
    }

    [Fact]
    public void MissingOuterContourIsAnError()
    {
        var outline = Outline(new Contour
        {
            Id = "hole-only",
            Role = ContourRole.Hole,
            Segments = new ContourSegment[] { Line(0, 0, 1, 0) },
        });

        Assert.False(BoardOutlineBounds.TryCompute(outline, out _, out var error));
        Assert.Contains("Outer", error, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidArcMetadataIsAnError()
    {
        var outline = Outline(Outer(new ArcSegment
        {
            Start = P(15, 10),
            End = P(5, 10),
            Center = P(10, 10),
            Radius = LengthValue.FromIu(null),
            SweepDirection = ArcSweepDirection.CounterClockwise,
        }));

        Assert.False(BoardOutlineBounds.TryCompute(outline, out _, out var error));
        Assert.Contains("radius", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArcStartOffRadiusIsAnError()
    {
        var outline = Outline(Outer(new ArcSegment
        {
            Start = P(14, 10),
            End = P(5, 10),
            Center = P(10, 10),
            Radius = LengthValue.FromIu(AltiumUnits.MmToIu(5)),
            SweepDirection = ArcSweepDirection.CounterClockwise,
        }));

        Assert.False(BoardOutlineBounds.TryCompute(outline, out _, out var error));
        Assert.Contains("radius", error, StringComparison.OrdinalIgnoreCase);
    }

    private static BoardOutline Outline(params Contour[] contours) =>
        new() { Id = "outline-test", Contours = contours };

    private static Contour Outer(params ContourSegment[] segments) =>
        new() { Id = "outer", Role = ContourRole.Outer, Segments = segments };

    private static LineSegment Line(double x1, double y1, double x2, double y2) =>
        new() { Start = P(x1, y1), End = P(x2, y2) };

    private static ArcSegment Arc(
        double cx,
        double cy,
        double radius,
        double startDeg,
        double endDeg,
        ArcSweepDirection direction)
    {
        var start = startDeg * Math.PI / 180.0;
        var end = endDeg * Math.PI / 180.0;
        return new ArcSegment
        {
            Center = P(cx, cy),
            Start = P(cx + radius * Math.Cos(start), cy + radius * Math.Sin(start)),
            End = P(cx + radius * Math.Cos(end), cy + radius * Math.Sin(end)),
            Radius = LengthValue.FromIu(AltiumUnits.MmToIu(radius)),
            SweepDirection = direction,
        };
    }

    private static Point2D P(double x, double y) =>
        Point2D.FromIu(AltiumUnits.MmToIu(x), AltiumUnits.MmToIu(y));

    private static void AssertClose(double actual, double expected) =>
        Assert.InRange(Math.Abs(actual - expected), 0, 2e-4);
}
