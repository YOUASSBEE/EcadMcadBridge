namespace EcadMcadBridge.Core.Model;

/// <summary>
/// Geometry-only topology evidence shared by the Altium adapter and the SOLIDWORKS inspector.
/// It samples the package's own coordinates and never invents or matches by bounds centres.
/// </summary>
public static class BoardOutlineTopology
{
    public const double DefaultPointToleranceMm = 0.01;
    private const int MaxSamplesPerSegment = 1024;
    private const double FullTurn = 2 * Math.PI;

    public readonly record struct PlanarPoint(double X, double Y);

    public static IReadOnlyList<PlanarPoint> Sample(Contour contour) =>
        Sample(contour, DefaultPointToleranceMm);

    public static IReadOnlyList<PlanarPoint> Sample(Contour contour, double toleranceMm) =>
        Sample(contour, toleranceMm, out _);

    /// <summary>
    /// Samples the whole contour: lines get interior points no further apart than the tolerance,
    /// arcs get sagitta-limited interior points plus both ends. The sample count is capped so a
    /// pathological tolerance cannot allocate unbounded points; <paramref name="saturated"/> tells
    /// the caller that the cap was reached and the proof is no longer valid.
    /// </summary>
    public static IReadOnlyList<PlanarPoint> Sample(
        Contour contour,
        double toleranceMm,
        out bool saturated)
    {
        ArgumentNullException.ThrowIfNull(contour);
        var tolerance = NormalizeTolerance(toleranceMm);
        var points = new List<PlanarPoint>();
        saturated = false;
        foreach (var segment in contour.Segments ?? Array.Empty<ContourSegment>())
        {
            if (segment is LineSegment line)
            {
                saturated |= AddLineSamples(points, line, tolerance);
                continue;
            }

            if (segment is ArcSegment arc)
                saturated |= AddArcSamples(points, arc, tolerance);
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

    /// <summary>
    /// Tests one point list against another with the same full sampling and crossing checks used
    /// by contour classification. Used to validate region-local nesting; a saturated sample is
    /// treated as not proven.
    /// </summary>
    public static bool IsFullyInside(
        IReadOnlyList<Point2D> innerPoints,
        IReadOnlyList<Point2D> outerPoints,
        double toleranceMm = DefaultPointToleranceMm)
    {
        if (innerPoints is null || outerPoints is null || innerPoints.Count < 3 || outerPoints.Count < 3)
            return false;

        var tolerance = NormalizeTolerance(toleranceMm);
        var inner = Sample(ToContour(innerPoints), tolerance, out var innerSaturated);
        var outer = Sample(ToContour(outerPoints), tolerance, out var outerSaturated);
        return !innerSaturated && !outerSaturated && IsFullyInside(inner, outer, tolerance);
    }

    private static Contour ToContour(IReadOnlyList<Point2D> points)
    {
        var segments = new List<ContourSegment>(points.Count);
        for (var i = 0; i + 1 < points.Count; i++)
            segments.Add(new LineSegment { Start = points[i], End = points[i + 1] });
        if (points.Count > 2 && !PointsEqual(points[0], points[^1]))
            segments.Add(new LineSegment { Start = points[^1], End = points[0] });
        return new Contour
        {
            Id = "altium:topology",
            Role = ContourRole.Outer,
            Segments = segments,
        };
    }

    private static bool PointsEqual(Point2D left, Point2D right) =>
        left.XIu == right.XIu && left.YIu == right.YIu;

    /// <summary>
    /// Source-compatible overload: a contour without an explicit hole flag is classified by its
    /// geometry relative to the board boundary; region-local hole flags are validated by the caller.
    /// </summary>
    public static BoardCutoutRole ClassifyRegionContour(
        Contour contour,
        IReadOnlyList<Contour> outerContours,
        double toleranceMm = DefaultPointToleranceMm) =>
        ClassifyRegionContour(contour, outerContours, isHole: false, toleranceMm);

    /// <summary>
    /// Classifies a Board Cutout contour geometrically against the Outer contours. The region's
    /// own hole flag is checked only where it contradicts the geometry; region-local nesting is
    /// validated by the caller. A saturated sample fails closed as <see cref="BoardCutoutRole.Unsupported"/>.
    /// </summary>
    public static BoardCutoutRole ClassifyRegionContour(
        Contour contour,
        IReadOnlyList<Contour> outerContours,
        bool isHole,
        double toleranceMm = DefaultPointToleranceMm)
    {
        ArgumentNullException.ThrowIfNull(contour);
        if (outerContours is null || outerContours.Count == 0)
            return BoardCutoutRole.Unrelated;

        var tolerance = NormalizeTolerance(toleranceMm);
        var sampled = Sample(contour, tolerance, out var sourceSaturated);
        if (sourceSaturated)
            return BoardCutoutRole.Unsupported;
        if (sampled.Count < 3)
            return BoardCutoutRole.Unrelated;

        var outerPolylines = new List<IReadOnlyList<PlanarPoint>>();
        foreach (var outerContour in outerContours.Where(item => item.Role == ContourRole.Outer))
        {
            var points = Sample(outerContour, tolerance, out var outerSaturated);
            if (outerSaturated)
                return BoardCutoutRole.Unsupported;
            if (points.Count >= 3)
                outerPolylines.Add(points);
        }
        if (outerPolylines.Count == 0)
            return BoardCutoutRole.Unrelated;

        // BoardShape is bidirectional: every sampled source point and every sampled Outer point
        // lie on the same boundary. A source that only covers part of an Outer is proven
        // board-shape evidence (a fragment), never a hole and never a whole-board proof.
        foreach (var outer in outerPolylines)
        {
            if (!sampled.All(point => DistanceToPolyline(point, outer) <= tolerance))
                continue;

            if (isHole)
                return BoardCutoutRole.Unsupported;
            return outer.All(point => DistanceToPolyline(point, sampled) <= tolerance)
                ? BoardCutoutRole.BoardShape
                : BoardCutoutRole.BoardShapeFragment;
        }

        // Point-in-polygon is evaluated for every sample (concave outers included); a centroid
        // or vertex average is never used as evidence. Crossing the boundary is rejected by
        // the trespass check and by proper segment intersections.
        var insideOuter = outerPolylines.FirstOrDefault(outer => IsFullyInside(sampled, outer, tolerance));
        if (insideOuter is not null)
            return BoardCutoutRole.Hole;

        return BoardCutoutRole.Unrelated;
    }

    private static bool IsFullyInside(
        IReadOnlyList<PlanarPoint> sampled,
        IReadOnlyList<PlanarPoint> outer,
        double toleranceMm)
    {
        foreach (var point in sampled)
        {
            if (!IsPointInside(point, outer) || DistanceToPolyline(point, outer) <= toleranceMm)
                return false;
        }

        for (var i = 0; i < sampled.Count; i++)
        {
            var a = sampled[i];
            var b = sampled[(i + 1) % sampled.Count];
            for (var j = 0; j < outer.Count; j++)
            {
                var c = outer[j];
                var d = outer[(j + 1) % outer.Count];
                if (SegmentsProperlyIntersect(a, b, c, d))
                    return false;
            }
        }
        return true;
    }

    private static bool SegmentsProperlyIntersect(
        PlanarPoint a,
        PlanarPoint b,
        PlanarPoint c,
        PlanarPoint d)
    {
        var d1 = Cross(c, d, a);
        var d2 = Cross(c, d, b);
        var d3 = Cross(a, b, c);
        var d4 = Cross(a, b, d);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double Cross(PlanarPoint a, PlanarPoint b, PlanarPoint point) =>
        ((b.X - a.X) * (point.Y - a.Y)) - ((b.Y - a.Y) * (point.X - a.X));

    private static bool AddLineSamples(List<PlanarPoint> points, LineSegment line, double toleranceMm)
    {
        if (!TryPoint(line.Start, out var start) || !TryPoint(line.End, out var end))
            return false;

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        var segments = LineSegmentCount(length, toleranceMm);
        for (var i = 0; i <= segments; i++)
        {
            var t = i / (double)segments;
            points.Add(new PlanarPoint(start.X + (dx * t), start.Y + (dy * t)));
        }

        // A straight segment keeps the same supporting line however coarsely it is sampled,
        // so the allocation cap does not weaken the geometric proof. Only arcs can lose their
        // curve between samples.
        return false;
    }

    private static int LineSegmentCount(double length, double toleranceMm)
    {
        if (!double.IsFinite(length) || length <= 0)
            return 1;
        var raw = Math.Ceiling(length / toleranceMm);
        return Math.Clamp((int)Math.Min(raw, MaxSamplesPerSegment), 1, MaxSamplesPerSegment);
    }

    private static bool AddArcSamples(List<PlanarPoint> points, ArcSegment arc, double toleranceMm)
    {
        if (!TryArc(arc, out var centerX, out var centerY, out var radius)
            || !TryPoint(arc.Start, out var startPoint)
            || !TryPoint(arc.End, out var endPoint))
            return false;

        var start = Math.Atan2(startPoint.Y - centerY, startPoint.X - centerX);
        var end = Math.Atan2(endPoint.Y - centerY, endPoint.X - centerX);
        if (!double.IsFinite(start) || !double.IsFinite(end))
            return false;

        var delta = arc.SweepDirection == ArcSweepDirection.Clockwise
            ? -PositiveModulo(start - end, FullTurn)
            : PositiveModulo(end - start, FullTurn);
        var magnitude = Math.Abs(delta);
        if (!double.IsFinite(magnitude) || magnitude <= 0)
        {
            points.Add(startPoint);
            points.Add(endPoint);
            return false;
        }

        var (segments, saturated) = ArcSegmentCount(radius, magnitude, toleranceMm);
        points.Add(startPoint);
        for (var i = 1; i < segments; i++)
        {
            var angle = start + (delta * i / segments);
            points.Add(new PlanarPoint(
                centerX + (radius * Math.Cos(angle)),
                centerY + (radius * Math.Sin(angle))));
        }
        points.Add(endPoint);
        return saturated;
    }

    /// <summary>
    /// Splits an arc until the chord sagitta r*(1-cos(delta/(2n))) fits the tolerance.
    /// </summary>
    private static (int Count, bool Saturated) ArcSegmentCount(
        double radius,
        double sweepRadians,
        double toleranceMm)
    {
        var cosine = 1 - (toleranceMm / radius);
        if (!double.IsFinite(cosine))
            cosine = -1;
        cosine = Math.Clamp(cosine, -1.0, 1.0);
        var halfStep = Math.Acos(cosine);
        if (halfStep <= 0)
            return (MaxSamplesPerSegment, true);
        var raw = Math.Ceiling(sweepRadians / (2 * halfStep));
        if (!double.IsFinite(raw) || raw >= MaxSamplesPerSegment)
            return (MaxSamplesPerSegment, true);
        return (Math.Clamp((int)raw, 1, MaxSamplesPerSegment), false);
    }

    private static bool TryPoint(Point2D point, out PlanarPoint result)
    {
        if (point.XMm is double x && point.YMm is double y && double.IsFinite(x) && double.IsFinite(y))
        {
            result = new PlanarPoint(x, y);
            return true;
        }
        result = default;
        return false;
    }

    private static double NormalizeTolerance(double toleranceMm) =>
        double.IsFinite(toleranceMm) && toleranceMm > 0 ? toleranceMm : DefaultPointToleranceMm;

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
        if (arc.Center.XMm is not double cx || !double.IsFinite(cx)
            || arc.Center.YMm is not double cy || !double.IsFinite(cy)
            || arc.Radius.Mm is not double r || !double.IsFinite(r) || r <= 0
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
