using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class OutlineContourMatcherTests
{
    [Fact]
    public void CountSharedPoints_MatchesTheCorrectContour()
    {
        var outer = Rectangle(0, 0, 100, 100);
        var hole = Rectangle(20, 20, 40, 40);
        var analyticHole = new[]
        {
            Point2D.FromIu(20, 20),
            Point2D.FromIu(40, 20),
            Point2D.FromIu(40, 40),
            Point2D.FromIu(20, 40),
            Point2D.FromIu(20, 20),
        };

        Assert.Equal(5, OutlineContourMatcher.CountSharedPoints(hole, analyticHole));
        Assert.Equal(0, OutlineContourMatcher.CountSharedPoints(outer, analyticHole));
    }

    [Fact]
    public void CountSharedPoints_ReturnsZeroWhenNoPointMatches()
    {
        var contour = Rectangle(0, 0, 100, 100);
        var shifted = new[]
        {
            Point2D.FromIu(1000, 1000),
            Point2D.FromIu(1100, 1000),
        };

        Assert.Equal(0, OutlineContourMatcher.CountSharedPoints(contour, shifted));
    }

    [Fact]
    public void IsClosed_DetectsClosedAndOpenPointSequences()
    {
        var closed = new[]
        {
            Point2D.FromIu(0, 0),
            Point2D.FromIu(10, 0),
            Point2D.FromIu(10, 10),
            Point2D.FromIu(0, 0),
        };
        var open = closed[..^1];

        Assert.True(OutlineContourMatcher.IsClosed(closed));
        Assert.False(OutlineContourMatcher.IsClosed(open));
    }

    private static Point2D[] Rectangle(int x1, int y1, int x2, int y2) =>
    [
        Point2D.FromIu(x1, y1),
        Point2D.FromIu(x2, y1),
        Point2D.FromIu(x2, y2),
        Point2D.FromIu(x1, y2),
        Point2D.FromIu(x1, y1),
    ];
}
