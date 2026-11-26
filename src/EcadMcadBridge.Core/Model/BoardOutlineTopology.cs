namespace EcadMcadBridge.Core.Model;

/// <summary>
/// Geometry-only topology evidence shared by the Altium adapter and the SOLIDWORKS inspector.
/// It samples the package's own coordinates and never invents or matches by bounds centres.
/// </summary>
public static class BoardOutlineTopology
{
    public const double DefaultPointToleranceMm = 0.01;
    private const int ArcSamples = 24;

    public readonly record struct PlanarPoint(double X, double Y);

    public static IReadOnlyList<PlanarPoint> Sample(Contour contour)
    {
        ArgumentNullException.ThrowIfNull(contour);
        var points = new List<PlanarPoint>();
        foreach (var segment in contour.Segments ?? Array.Empty<ContourSegment>())
        {
            if (segment is LineSegment line)
            {
                Add(points, line.Start);
                Add(points, line.End);
                continue;
            }

            if (segment is ArcSegment arc
                && TryArc(arc, out var centerX, out var centerY, out var radius))
            {
                Add(points, arc.Start);
                var start = Math.Atan2(arc.Start.YMm!.Value - centerY, arc.Start.XMm!.Value - centerX);
                var end = Math.Atan2(arc.End.YMm!.Value - centerY, arc.End.XMm!.Value - centerX);
                var delta = arc.SweepDirection == ArcSweepDirection.Clockwise
                    ? -PositiveModulo(start - end, 2 * Math.PI)
                    : PositiveModulo(end - start, 2 * Math.PI);
                for (var i = 1; i < ArcSamples; i++)
                {
                    var angle = start + (delta * i / ArcSamples);
                    points.Add(new PlanarPoint(
                        centerX + (radius * Math.Cos(angle)),
                        centerY + (radius * Math.Sin(angle))));
                }
                Add(points, arc.End);
            }
        }
        return points;
    }

    public static bool TryBounds(
        IEnumerable<PlanarPoint> points,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        minX = minY = maxX = maxY = 0;
        var any = false;
        foreach (var point in points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                continue;
            if (!any)
            {
                minX = maxX = point.X;
                minY = maxY = point.Y;
                any = true;
                continue;
            }
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }
        return any;
    }

    /// <summary>Clips a sampled contour to an axis-aligned box using existing coordinates only.</summary>
    public static IReadOnlyList<PlanarPoint> ClipToBox(
        Contour contour,
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        ArgumentNullException.ThrowIfNull(contour);
        var points = Sample(contour);
        if (points.Count < 3)
            return Array.Empty<PlanarPoint>();

        points = Clip(points, point => point.X >= minX, (a, b) => IntersectX(a, b, minX));
        points = Clip(points, point => point.X <= maxX, (a, b) => IntersectX(a, b, maxX));
        points = Clip(points, point => point.Y >= minY, (a, b) => IntersectY(a, b, minY));
        points = Clip(points, point => point.Y <= maxY, (a, b) => IntersectY(a, b, maxY));
        return points;
    }

    private static IReadOnlyList<PlanarPoint> Clip(
        IReadOnlyList<PlanarPoint> points,
        Func<PlanarPoint, bool> inside,
        Func<PlanarPoint, PlanarPoint, PlanarPoint> intersect)
    {
        if (points.Count == 0)
            return Array.Empty<PlanarPoint>();

        var result = new List<PlanarPoint>(points.Count + 1);
        for (var i = 0; i < points.Count; i++)
        {
            var current = points[i];
            var previous = points[(i + points.Count - 1) % points.Count];
            var currentInside = inside(current);
            var previousInside = inside(previous);
            if (currentInside != previousInside)
                result.Add(intersect(previous, current));
            if (currentInside)
                result.Add(current);
        }

        return result;
    }

    private static PlanarPoint IntersectX(PlanarPoint a, PlanarPoint b, double x)
    {
        var t = (x - a.X) / (b.X - a.X);
        return new PlanarPoint(x, a.Y + ((b.Y - a.Y) * t));
    }

    private static PlanarPoint IntersectY(PlanarPoint a, PlanarPoint b, double y)
    {
        var t = (y - a.Y) / (b.Y - a.Y);
        return new PlanarPoint(a.X + ((b.X - a.X) * t), y);
    }

    public static double PolygonArea(IReadOnlyList<PlanarPoint> points)
    {
        if (points.Count < 3)
            return 0;
        var twice = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            twice += (a.X * b.Y) - (b.X * a.Y);
        }
        return twice / 2.0;
    }

    public static bool IsPointInside(PlanarPoint point, IReadOnlyList<PlanarPoint> polygon)
    {
        if (polygon.Count < 3)
            return false;
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (((b.X - a.X) * (point.Y - a.Y)) / (b.Y - a.Y)) + a.X)
                inside = !inside;
        }
        return inside;
    }

    public static BoardCutoutRole ClassifyRegionContour(
        Contour contour,
        IReadOnlyList<Contour> outerContours,
        double toleranceMm = DefaultPointToleranceMm)
    {
        if (outerContours is null || outerContours.Count == 0)
            return BoardCutoutRole.Unrelated;

        var sampled = Sample(contour);
        if (sampled.Count < 3)
            return BoardCutoutRole.Unrelated;

        var outerPolylines = outerContours
            .Where(item => item.Role == ContourRole.Outer)
            .Select(Sample)
            .Where(item => item.Count >= 3)
            .ToArray();
        if (outerPolylines.Length == 0)
            return BoardCutoutRole.Unrelated;

        if (sampled.All(point => outerPolylines.Any(outer => DistanceToPolyline(point, outer) <= toleranceMm)))
            return BoardCutoutRole.BoardShape;

        return outerPolylines.Any(outer => IsPointInside(Centroid(sampled), outer))
            ? BoardCutoutRole.Hole
            : BoardCutoutRole.Unrelated;
    }

    private static void Add(List<PlanarPoint> points, Point2D point)
    {
        if (point.XMm is double x && point.YMm is double y && double.IsFinite(x) && double.IsFinite(y))
            points.Add(new PlanarPoint(x, y));
    }

    private static PlanarPoint Centroid(IReadOnlyList<PlanarPoint> points)
    {
        var x = 0.0;
        var y = 0.0;
        foreach (var point in points)
        {
            x += point.X;
            y += point.Y;
        }
        return points.Count == 0 ? new PlanarPoint(0, 0) : new PlanarPoint(x / points.Count, y / points.Count);
    }

    private static double DistanceToPolyline(PlanarPoint point, IReadOnlyList<PlanarPoint> polyline)
    {
        if (polyline.Count < 2)
            return double.PositiveInfinity;
        var best = double.PositiveInfinity;
        for (var i = 0; i < polyline.Count - 1; i++)
        {
            var a = polyline[i];
            var b = polyline[i + 1];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = (dx * dx) + (dy * dy);
            var t = lengthSquared <= 0
                ? 0
                : Math.Clamp((((point.X - a.X) * dx) + ((point.Y - a.Y) * dy)) / lengthSquared, 0, 1);
            var ex = point.X - (a.X + (t * dx));
            var ey = point.Y - (a.Y + (t * dy));
            var distance = Math.Sqrt((ex * ex) + (ey * ey));
            best = Math.Min(best, distance);
        }
        return best;
    }

    private static bool TryArc(
        ArcSegment arc,
        out double centerX,
        out double centerY,
        out double radius)
    {
        centerX = centerY = radius = 0;
        if (arc.Center.XMm is not double cx
            || arc.Center.YMm is not double cy
            || arc.Radius.Mm is not double r
            || r <= 0
            || arc.Start.XMm is null
            || arc.Start.YMm is null
            || arc.End.XMm is null
            || arc.End.YMm is null)
            return false;
        centerX = cx;
        centerY = cy;
        radius = r;
        return true;
    }

    private static double PositiveModulo(double value, double modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}
