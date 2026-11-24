using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Altium.Adapter;

internal sealed record OutlineSegment(
    Point2D Start,
    Point2D End,
    Point2D Center,
    int Radius,
    ArcSweepDirection? SweepDirection);

/// <summary>
/// Exact contour matching for analytic loops. A single shared vertex is not evidence;
/// all analytic vertices must be present and every geometric vertex must lie on the
/// analytic line/arc set.
/// </summary>
internal static class OutlineContourMatcher
{
    private const double ToleranceIu = 2.0;

    public static bool TryExactMatch(
        IReadOnlyList<Point2D> contour,
        IReadOnlyList<OutlineSegment> loop)
    {
        if (loop.Count < 2 || !IsClosed(loop) || !IsContinuous(loop))
            return false;

        var contourPoints = UniquePoints(contour);
        var loopPoints = UniquePoints(loop.SelectMany(segment => new[] { segment.Start, segment.End }));
        if (loopPoints.Count < 3 || contourPoints.Count < 3)
            return false;

        foreach (var point in loopPoints)
        {
            if (DistanceToPolyline(point, contour) > ToleranceIu)
                return false;
        }

        foreach (var point in contourPoints)
        {
            if (!IsPointOnLoop(point, loop))
                return false;
        }

        // Check the interior of each geometric segment, not only its endpoints.
        for (var i = 0; i < contour.Count; i++)
        {
            var next = (i + 1) % contour.Count;
            if (!TryMidpoint(contour[i], contour[next], out var midpoint))
                return false;
            if (!IsPointOnLoop(midpoint, loop))
                return false;
        }

        return true;
    }

    public static IReadOnlyList<int> FindExactMatches(
        IReadOnlyList<Point2D> contour,
        IReadOnlyList<IReadOnlyList<OutlineSegment>> loops,
        IReadOnlyList<bool>? used = null)
    {
        var matches = new List<int>();
        for (var i = 0; i < loops.Count; i++)
        {
            if (used is not null && i < used.Count && used[i])
                continue;
            if (TryExactMatch(contour, loops[i]))
                matches.Add(i);
        }

        return matches;
    }

    public static bool HasAnySharedPoint(
        IReadOnlyList<Point2D> contour,
        IReadOnlyList<OutlineSegment> loop)
    {
        var contourPoints = UniquePoints(contour);
        return UniquePoints(loop.SelectMany(segment => new[] { segment.Start, segment.End }))
            .Any(contourPoints.Contains);
    }

    public static int CountUniqueSharedPoints(
        IReadOnlyList<Point2D> contour,
        IReadOnlyList<Point2D> loop)
    {
        var contourPoints = UniquePoints(contour);
        return UniquePoints(loop).Count(contourPoints.Contains);
    }

    public static string ExplainExactMatch(
        IReadOnlyList<Point2D> contour,
        IReadOnlyList<OutlineSegment> loop)
    {
        if (loop.Count < 2)
            return "loop has fewer than two segments";
        if (!IsClosed(loop))
            return "loop is not closed";
        if (!IsContinuous(loop))
            return "loop is not continuous";

        var contourPoints = UniquePoints(contour);
        var loopPoints = UniquePoints(loop.SelectMany(segment => new[] { segment.Start, segment.End }));
        var missingLoop = loopPoints.Where(point => !contourPoints.Contains(point)).Take(3).ToArray();
        var notOnLoop = contourPoints.Where(point => !IsPointOnLoop(point, loop)).Take(3).ToArray();
        var loopToContour = loopPoints.Select(point => DistanceToPolyline(point, contour)).DefaultIfEmpty(0).Max();
        var contourToLoop = contourPoints.Select(point => DistanceToLoop(point, loop)).DefaultIfEmpty(0).Max();
        return $"uniqueLoop={loopPoints.Count} uniqueContour={contourPoints.Count} " +
            $"loopVerticesNotInContour={missingLoop.Length} contourPointsNotOnLoop={notOnLoop.Length} " +
            $"maxLoopToContour={loopToContour:F2} maxContourToLoop={contourToLoop:F2}" +
            (missingLoop.Length > 0 ? " firstMissingLoop=" + Format(missingLoop[0]) : string.Empty) +
            (notOnLoop.Length > 0 ? " firstNotOnLoop=" + Format(notOnLoop[0]) : string.Empty);
    }

    private static string Format(Point2D point) =>
        $"({point.XIu},{point.YIu})";

    public static bool IsClosed(IReadOnlyList<OutlineSegment> loop) =>
        loop.Count >= 2 && PointsEqual(loop[0].Start, loop[^1].End);

    public static bool IsContinuous(IReadOnlyList<OutlineSegment> loop)
    {
        for (var i = 1; i < loop.Count; i++)
        {
            if (!PointsEqual(loop[i - 1].End, loop[i].Start))
                return false;
        }

        return true;
    }

    public static bool IsClosed(IReadOnlyList<Point2D> points) =>
        points.Count >= 2 && PointsEqual(points[0], points[^1]);

    internal static bool IsPointOnLoop(Point2D point, IReadOnlyList<OutlineSegment> loop) =>
        DistanceToLoop(point, loop) <= ToleranceIu;

    private static double DistanceToLoop(Point2D point, IReadOnlyList<OutlineSegment> loop)
    {
        if (!TryCoordinates(point, out var px, out var py))
            return double.PositiveInfinity;

        var distance = double.PositiveInfinity;
        foreach (var segment in loop)
        {
            if (segment.SweepDirection is null)
            {
                distance = Math.Min(distance, DistanceToSegment(point, segment.Start, segment.End));
                continue;
            }

            if (!TryCoordinates(segment.Center, out var cx, out var cy)
                || !TryCoordinates(segment.Start, out var sx, out var sy)
                || !TryCoordinates(segment.End, out var ex, out var ey))
            {
                continue;
            }

            var dx = px - cx;
            var dy = py - cy;
            var centreDistance = Math.Sqrt((dx * dx) + (dy * dy));
            var radialDistance = Math.Abs(centreDistance - segment.Radius);
            var pointAngle = NormalizeAngle(Math.Atan2(dy, dx));
            var startAngle = NormalizeAngle(Math.Atan2(sy - cy, sx - cx));
            var endAngle = NormalizeAngle(Math.Atan2(ey - cy, ex - cx));

            if (!IsAngleOnArc(pointAngle, startAngle, endAngle, segment.SweepDirection.Value, segment.Radius))
            {
                distance = Math.Min(distance, Math.Min(
                    Distance(point, segment.Start) ?? double.PositiveInfinity,
                    Distance(point, segment.End) ?? double.PositiveInfinity));
                continue;
            }

            distance = Math.Min(distance, radialDistance);
        }

        return distance;
    }

    private static bool IsAngleOnArc(
        double pointAngle,
        double startAngle,
        double endAngle,
        ArcSweepDirection direction,
        double radius)
    {
        const double twoPi = Math.PI * 2.0;
        var delta = direction == ArcSweepDirection.CounterClockwise
            ? NormalizeAngle(endAngle - startAngle)
            : NormalizeAngle(startAngle - endAngle);
        if (delta < 1e-12)
            delta = twoPi;

        var offset = direction == ArcSweepDirection.CounterClockwise
            ? NormalizeAngle(pointAngle - startAngle)
            : NormalizeAngle(startAngle - pointAngle);

        var angularTolerance = radius > 0 ? Math.Min(0.05, ToleranceIu / radius) : 0.05;
        return offset <= delta + angularTolerance || offset >= twoPi - angularTolerance;
    }

    private static double NormalizeAngle(double angle)
    {
        const double twoPi = Math.PI * 2.0;
        var result = angle % twoPi;
        return result < 0 ? result + twoPi : result;
    }

    private static bool TryMidpoint(Point2D left, Point2D right, out Point2D midpoint)
    {
        if (left.XIu is int lx && left.YIu is int ly
            && right.XIu is int rx && right.YIu is int ry)
        {
            midpoint = Point2D.FromIu(
                (int)Math.Round((lx + rx) / 2.0, MidpointRounding.AwayFromZero),
                (int)Math.Round((ly + ry) / 2.0, MidpointRounding.AwayFromZero));
            return true;
        }

        midpoint = default;
        return false;
    }

    private static double DistanceToPolyline(Point2D point, IReadOnlyList<Point2D> contour)
    {
        var distance = double.PositiveInfinity;
        for (var i = 0; i < contour.Count; i++)
        {
            var next = (i + 1) % contour.Count;
            distance = Math.Min(distance, DistanceToSegment(point, contour[i], contour[next]));
        }

        return distance;
    }

    private static double DistanceToSegment(Point2D point, Point2D start, Point2D end)
    {
        if (!TryCoordinates(point, out var px, out var py)
            || !TryCoordinates(start, out var sx, out var sy)
            || !TryCoordinates(end, out var ex, out var ey))
            return double.PositiveInfinity;

        var dx = ex - sx;
        var dy = ey - sy;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared == 0)
            return Distance(point, start) ?? double.PositiveInfinity;

        var projection = (((px - sx) * dx) + ((py - sy) * dy)) / lengthSquared;
        projection = Math.Clamp(projection, 0.0, 1.0);
        var nearestX = sx + (projection * dx);
        var nearestY = sy + (projection * dy);
        var offsetX = px - nearestX;
        var offsetY = py - nearestY;
        return Math.Sqrt((offsetX * offsetX) + (offsetY * offsetY));
    }

    private static double? Distance(Point2D left, Point2D right)
    {
        if (!TryCoordinates(left, out var lx, out var ly)
            || !TryCoordinates(right, out var rx, out var ry))
            return null;

        var dx = lx - rx;
        var dy = ly - ry;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static HashSet<Point2D> UniquePoints(IEnumerable<Point2D> points) =>
        points.Where(point => point.XIu is not null && point.YIu is not null).ToHashSet();

    private static bool TryCoordinates(Point2D point, out double x, out double y)
    {
        if (point.XIu is int xi && point.YIu is int yi)
        {
            x = xi;
            y = yi;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    private static bool PointsEqual(Point2D left, Point2D right) =>
        left.XIu == right.XIu && left.YIu == right.YIu;
}
