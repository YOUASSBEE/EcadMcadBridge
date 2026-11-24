using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>Pure helpers for comparing analytic loops with geometric outline points.</summary>
internal static class OutlineContourMatcher
{
    public static int CountSharedPoints(
        IReadOnlyList<Point2D> contour,
        IReadOnlyList<Point2D> loop)
    {
        var contourPoints = new HashSet<(int X, int Y)>();
        foreach (var point in contour)
        {
            if (point.XIu is int x && point.YIu is int y)
                contourPoints.Add((x, y));
        }

        var count = 0;
        foreach (var point in loop)
        {
            if (point.XIu is int x && point.YIu is int y && contourPoints.Contains((x, y)))
                count++;
        }

        return count;
    }

    public static bool IsClosed(IReadOnlyList<Point2D> points)
    {
        return points.Count >= 2 && PointsEqual(points[0], points[^1]);
    }

    private static bool PointsEqual(Point2D left, Point2D right) =>
        left.XIu == right.XIu && left.YIu == right.YIu;
}
