using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Altium.Adapter;

internal readonly record struct CutoutSourceKey(int RegionIndex, int ContourIndex);

internal sealed record CutoutSourceContour(
    int RegionIndex,
    string? RegionUniqueId,
    string? RegionName,
    int ContourIndex,
    bool IsHole,
    IReadOnlyList<Point2D> Points,
    long RegionArea,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY)
{
    public CutoutSourceKey Key => new(RegionIndex, ContourIndex);
}

internal enum CutoutRegionMatchStatus
{
    Matched,
    Unmatched,
    Ambiguous,
}

internal sealed record CutoutRegionMatch(
    CutoutRegionMatchStatus Status,
    CutoutSourceContour? Source,
    IReadOnlyList<CutoutSourceContour> Candidates,
    string Reason);

internal sealed record CutoutExistingHole(int Index, IReadOnlyList<Point2D> Points);

internal enum CutoutDuplicateStatus
{
    NotDuplicate,
    Duplicate,
    Partial,
}

internal sealed record CutoutDuplicateDecision(
    CutoutDuplicateStatus Status,
    int ExistingHoleIndex,
    string Reason)
{
    public bool ShouldExport => Status != CutoutDuplicateStatus.Duplicate;
}

internal static class CutoutDeduplicator
{
    /// <summary>Exact matcher tolerance for genuinely identical line-work.</summary>
    public const double ExactToleranceIu = 2.0;

    /// <summary>
    /// Separate dedup approximation tolerance for a tessellated geometric Hole versus
    /// an analytic clip loop. Derived from the measured Altium contour deviation
    /// (3096.57 IU); it is intentionally not reused as the Region matching tolerance.
    /// 3500 IU is 8.89 um.
    /// </summary>
    public const int ApproximationToleranceIu = 3500;

    /// <summary>Area guard for approximate duplicate detection.</summary>
    public const double ApproximationAreaRelativeTolerance = 0.05;

    /// <summary>
    /// Centroid guard for approximate duplicate detection. A duplicate must share the
    /// same physical location; 100 IU is 0.254 um.
    /// </summary>
    public const int ApproximationCentroidToleranceIu = 100;

    private const int DedupArcSamples = 32;

    public static CutoutDuplicateDecision Evaluate(
        IReadOnlyList<OutlineSegment> clipLoop,
        IReadOnlyList<CutoutExistingHole> existingHoles)
    {
        for (var i = 0; i < existingHoles.Count; i++)
        {
            var hole = existingHoles[i];
            if (hole.Points.Count < 3)
                continue;

            var closed = EnsureClosed(hole.Points);
            if (OutlineContourMatcher.TryExactMatch(closed, clipLoop, ExactToleranceIu))
                return new CutoutDuplicateDecision(
                    CutoutDuplicateStatus.Duplicate,
                    hole.Index,
                    "clip loop exactly matches the existing geometric Hole");
        }

        for (var i = 0; i < existingHoles.Count; i++)
        {
            var hole = existingHoles[i];
            if (hole.Points.Count < 3)
                continue;

            var closed = EnsureClosed(hole.Points);
            if (TryApproximateDuplicate(closed, clipLoop, out var approximation))
                return new CutoutDuplicateDecision(
                    CutoutDuplicateStatus.Duplicate,
                    hole.Index,
                    "clip loop matches the tessellated geometric Hole within dedup tolerance: " +
                    approximation);
        }

        for (var i = 0; i < existingHoles.Count; i++)
        {
            var hole = existingHoles[i];
            if (hole.Points.Count < 3)
                continue;

            var closed = EnsureClosed(hole.Points);
            if (OutlineContourMatcher.HasAnySharedPoint(closed, clipLoop))
            {
                return new CutoutDuplicateDecision(
                    CutoutDuplicateStatus.Partial,
                    hole.Index,
                    "partial overlap only: " + OutlineContourMatcher.ExplainExactMatch(closed, clipLoop));
            }
        }

        return new CutoutDuplicateDecision(
            CutoutDuplicateStatus.NotDuplicate,
            -1,
            "no existing geometric Hole matches the clip loop");
    }

    private static bool TryApproximateDuplicate(
        IReadOnlyList<Point2D> hole,
        IReadOnlyList<OutlineSegment> clipLoop,
        out string reason)
    {
        var clipBounds = AnalyticBounds.GetAnalyticBounds(clipLoop);
        if (!clipBounds.IsExact)
        {
            reason = "clip loop has no exact analytic bounds: " + clipBounds.Error;
            return false;
        }

        var holeBounds = CutoutRegionMatcher.GetBounds(hole);
        if (!BoundsClose(holeBounds, clipBounds, ApproximationToleranceIu))
        {
            reason = "bounds differ by more than the dedup tolerance";
            return false;
        }

        foreach (var point in hole)
        {
            var distance = OutlineContourMatcher.DistanceToLoop(point, clipLoop);
            if (distance > ApproximationToleranceIu)
            {
                reason =
                    $"hole vertex {Format(point)} is {distance:F2} IU from the clip loop";
                return false;
            }
        }

        var clipSamples = CutoutRegionMatcher.SampleLoop(clipLoop, DedupArcSamples);
        foreach (var point in clipSamples)
        {
            var distance = OutlineContourMatcher.DistanceToPolyline(point, hole);
            if (distance > ApproximationToleranceIu)
            {
                reason =
                    $"clip sample {Format(point)} is {distance:F2} IU from the hole polyline";
                return false;
            }
        }

        if (!CutoutRegionMatcher.TryGetPolygonMetrics(hole, out var holeArea, out var holeX, out var holeY)
            || !CutoutRegionMatcher.TryGetPolygonMetrics(clipSamples, out var clipArea, out var clipX, out var clipY))
        {
            reason = "zero area";
            return false;
        }

        var areaRatio = Math.Abs(holeArea - clipArea) / Math.Max(holeArea, clipArea);
        if (areaRatio > ApproximationAreaRelativeTolerance)
        {
            reason = $"area ratio {areaRatio:P2} exceeds {ApproximationAreaRelativeTolerance:P0}";
            return false;
        }

        var centroidDistance = Math.Sqrt(
            Math.Pow(holeX - clipX, 2) + Math.Pow(holeY - clipY, 2));
        if (centroidDistance > ApproximationCentroidToleranceIu)
        {
            reason =
                $"centroid distance {centroidDistance:F2} IU exceeds " +
                $"{ApproximationCentroidToleranceIu} IU";
            return false;
        }

        reason =
            $"areaRatio={areaRatio:P2} centroidDistance={centroidDistance:F2} " +
            $"tolerance={ApproximationToleranceIu} IU";
        return true;
    }

    private static bool BoundsClose(
        (int MinX, int MinY, int MaxX, int MaxY) hole,
        AnalyticBoundsResult clip,
        int tolerance) =>
        Math.Abs(hole.MinX - clip.MinX) <= tolerance
        && Math.Abs(hole.MinY - clip.MinY) <= tolerance
        && Math.Abs(hole.MaxX - clip.MaxX) <= tolerance
        && Math.Abs(hole.MaxY - clip.MaxY) <= tolerance;

    private static string Format(Point2D point) =>
        $"({point.XIu},{point.YIu})";

    public static IReadOnlyList<int> SelectExportableLoops(
        IReadOnlyList<IReadOnlyList<OutlineSegment>> clipLoops,
        IReadOnlyList<CutoutExistingHole> existingHoles)
    {
        var result = new List<int>(clipLoops.Count);
        for (var i = 0; i < clipLoops.Count; i++)
        {
            if (Evaluate(clipLoops[i], existingHoles).ShouldExport)
                result.Add(i);
        }

        return result;
    }

    internal static IReadOnlyList<Point2D> EnsureClosed(IReadOnlyList<Point2D> points)
    {
        if (points.Count == 0)
            return Array.Empty<Point2D>();
        if (points.Count >= 2 && PointsEqual(points[0], points[^1]))
            return points;

        var closed = new List<Point2D>(points.Count + 1);
        closed.AddRange(points);
        closed.Add(points[0]);
        return closed;
    }

    private static bool PointsEqual(Point2D left, Point2D right) =>
        left.XIu == right.XIu && left.YIu == right.YIu;
}

internal sealed record AnalyticBoundsResult(
    bool IsExact,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY,
    string? Error);

internal static class AnalyticBounds
{
    public static AnalyticBoundsResult GetAnalyticBounds(IReadOnlyList<OutlineSegment> loop)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        var errors = new List<string>();

        void Include(Point2D point)
        {
            if (point.XIu is not int x || point.YIu is not int y)
                return;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        for (var i = 0; i < loop.Count; i++)
        {
            var segment = loop[i];
            Include(segment.Start);
            Include(segment.End);

            if (segment.SweepDirection is null)
                continue;

            if (segment.SweepDirection is not (
                ArcSweepDirection.CounterClockwise or ArcSweepDirection.Clockwise))
            {
                errors.Add($"segment {i} has an unknown sweep direction");
                continue;
            }

            if (segment.Radius <= 0)
            {
                errors.Add($"arc segment {i} has non-positive radius {segment.Radius}");
                continue;
            }

            if (segment.Center.XIu is not int cx || segment.Center.YIu is not int cy
                || segment.Start.XIu is not int sx || segment.Start.YIu is not int sy
                || segment.End.XIu is not int ex || segment.End.YIu is not int ey)
            {
                errors.Add($"arc segment {i} has incomplete center/start/end coordinates");
                continue;
            }

            var startAngle = Math.Atan2(sy - cy, sx - cx);
            var endAngle = Math.Atan2(ey - cy, ex - cx);
            var clockwise = segment.SweepDirection == ArcSweepDirection.Clockwise;
            var sweep = clockwise
                ? NormalizeAngle(startAngle - endAngle)
                : NormalizeAngle(endAngle - startAngle);
            if (sweep < 1e-12)
                sweep = Math.PI * 2.0;

            foreach (var cardinalDegrees in new[] { 0, 90, 180, 270 })
            {
                var cardinal = cardinalDegrees * Math.PI / 180.0;
                var offset = clockwise
                    ? NormalizeAngle(startAngle - cardinal)
                    : NormalizeAngle(cardinal - startAngle);
                if (offset <= sweep + 1e-9)
                    Include(Polar(Point2D.FromIu(cx, cy), segment.Radius, cardinal));
            }
        }

        if (minX == int.MaxValue)
            return new AnalyticBoundsResult(false, 0, 0, 0, 0, "no usable segment points");

        return new AnalyticBoundsResult(
            errors.Count == 0,
            minX,
            minY,
            maxX,
            maxY,
            errors.Count == 0 ? null : string.Join("; ", errors));
    }

    private static Point2D Polar(Point2D centre, int radius, double angle)
    {
        var cx = centre.XIu ?? 0;
        var cy = centre.YIu ?? 0;
        return Point2D.FromIu(
            (int)Math.Round(cx + (radius * Math.Cos(angle)), MidpointRounding.AwayFromZero),
            (int)Math.Round(cy + (radius * Math.Sin(angle)), MidpointRounding.AwayFromZero));
    }

    private static double NormalizeAngle(double angle)
    {
        const double twoPi = Math.PI * 2.0;
        var result = angle % twoPi;
        return result < 0 ? result + twoPi : result;
    }
}

internal static class CutoutRegionMatcher
{
    /// <summary>
    /// Bounding-box comparison used for diagnostics/ranking. Region tessellation may
    /// omit an arc extremum, so bounds never reject a source before geometry checking.
    /// 10 IU is 25.4 nm.
    /// </summary>
    public const int BoundsToleranceIu = 10;

    /// <summary>
    /// Region contours are tessellated; a vertex may be this far from the analytic clip
    /// loop. 3500 IU is 8.89 um and covers the measured deviation (3096.57 IU on
    /// board with cutout.PcbDoc) with a small margin.
    /// </summary>
    public const int RegionVertexToleranceIu = 3500;

    /// <summary>
    /// Area difference allowed for a tessellated Region contour versus an analytic clip
    /// loop. Measured value is 0.14%; 5% is a guard against a different shape, not a
    /// substitute for vertex/centroid checks.
    /// </summary>
    public const double AreaRelativeTolerance = 0.05;

    /// <summary>
    /// Relative centroid tolerance for the same shape. A small absolute floor keeps
    /// small features testable; the relative part scales with the contour, so a
    /// translation of a whole small rectangle is not accepted as tessellation.
    /// </summary>
    public const double CentroidRelativeTolerance = 0.01;

    /// <summary>Absolute centroid floor; 100 IU is 0.254 um.</summary>
    public const int CentroidFloorIu = 100;

    internal const int ArcSamples = 16;

    public static CutoutRegionMatch Match(
        IReadOnlyList<OutlineSegment> clipLoop,
        IReadOnlyList<CutoutSourceContour> sources,
        IReadOnlySet<CutoutSourceKey>? assigned = null)
    {
        var clipSamples = SampleLoop(clipLoop, ArcSamples);
        var clipBoundsResult = AnalyticBounds.GetAnalyticBounds(clipLoop);
        var clipBounds = (
            clipBoundsResult.MinX,
            clipBoundsResult.MinY,
            clipBoundsResult.MaxX,
            clipBoundsResult.MaxY);
        var boundsCandidates = clipBoundsResult.IsExact
            ? sources.Where(source => BoundsMatch(source, clipBounds)).ToArray()
            : sources.ToArray();
        var boundsNote = clipBoundsResult.IsExact
            ? string.Empty
            : "; analyticBoundsError=" + clipBoundsResult.Error;
        var verified = new List<(CutoutSourceContour Source, string Reason)>(sources.Count);
        var rejected = new List<string>();

        // Geometry-first: a tessellated Region contour may not contain a cardinal
        // extreme, so bounds are diagnostic/ranking only and never reject a source.
        foreach (var source in sources)
        {
            if (TryGeometryMatch(source, clipLoop, clipSamples, out var reason))
            {
                verified.Add((source, reason));
            }
            else
            {
                rejected.Add($"region {source.RegionIndex}[{source.ContourIndex}]: {reason}");
            }
        }

        var unused = verified
            .Where(item => assigned is null || !assigned.Contains(item.Source.Key))
            .ToArray();

        if (unused.Length == 1)
        {
            return new CutoutRegionMatch(
                CutoutRegionMatchStatus.Matched,
                unused[0].Source,
                new[] { unused[0].Source },
                "single geometric candidate: " + unused[0].Reason + boundsNote);
        }

        if (unused.Length > 1)
        {
            return new CutoutRegionMatch(
                CutoutRegionMatchStatus.Ambiguous,
                null,
                unused.Select(item => item.Source).ToArray(),
                $"{unused.Length} geometric candidates match the same clip loop: " +
                string.Join(
                    "; ",
                    unused.Select(item =>
                        $"region {item.Source.RegionIndex}[{item.Source.ContourIndex}] {item.Reason}"))
                + boundsNote);
        }

        if (verified.Count > 0)
        {
            return new CutoutRegionMatch(
                CutoutRegionMatchStatus.Unmatched,
                null,
                verified.Select(item => item.Source).ToArray(),
                "all geometric candidates are already assigned to other clip loops" + boundsNote);
        }

        var rejection = rejected.Count == 0 ? "no source contours" : string.Join("; ", rejected);
        return new CutoutRegionMatch(
            CutoutRegionMatchStatus.Unmatched,
            null,
            Array.Empty<CutoutSourceContour>(),
            $"sources={sources.Count}, boundsCandidates={boundsCandidates.Length}, " +
            $"geometryRejected={rejected.Count}: {rejection}" + boundsNote);
    }

    private static bool BoundsMatch(CutoutSourceContour source, (int MinX, int MinY, int MaxX, int MaxY) clip)
    {
        return Math.Abs(source.MinX - clip.MinX) <= BoundsToleranceIu
            && Math.Abs(source.MinY - clip.MinY) <= BoundsToleranceIu
            && Math.Abs(source.MaxX - clip.MaxX) <= BoundsToleranceIu
            && Math.Abs(source.MaxY - clip.MaxY) <= BoundsToleranceIu;
    }

    private static bool TryGeometryMatch(
        CutoutSourceContour source,
        IReadOnlyList<OutlineSegment> clipLoop,
        IReadOnlyList<Point2D> clipSamples,
        out string reason)
    {
        if (source.Points.Count < 3)
        {
            reason = "fewer than three points";
            return false;
        }

        var closed = CutoutDeduplicator.EnsureClosed(source.Points);
        if (closed.Count < 4)
        {
            reason = "contour is not closed";
            return false;
        }

        var maxVertexDistance = 0.0;
        foreach (var point in closed)
        {
            var distance = OutlineContourMatcher.DistanceToLoop(point, clipLoop);
            maxVertexDistance = Math.Max(maxVertexDistance, distance);
            if (distance > RegionVertexToleranceIu)
            {
                reason =
                    $"vertex {Format(point)} is {distance:F2} IU from the clip loop " +
                    $"(limit {RegionVertexToleranceIu} IU)";
                return false;
            }
        }

        var sourceSignedArea = PolygonArea(closed);
        var clipSignedArea = PolygonArea(clipSamples);
        var sourceArea = Math.Abs(sourceSignedArea);
        var clipArea = Math.Abs(clipSignedArea);
        if (sourceArea <= 0 || clipArea <= 0)
        {
            reason = "zero area";
            return false;
        }

        var areaRatio = Math.Abs(sourceArea - clipArea) / Math.Max(sourceArea, clipArea);
        if (areaRatio > AreaRelativeTolerance)
        {
            reason =
                $"area ratio {areaRatio:P2} exceeds {AreaRelativeTolerance:P0} " +
                $"(sourceArea={sourceArea:F0}, clipArea={clipArea:F0}, clipSamples={clipSamples.Count})";
            return false;
        }

        var sourceCentroid = PolygonCentroid(closed, sourceSignedArea);
        var clipCentroid = PolygonCentroid(clipSamples, clipSignedArea);
        var centroidDistance = Math.Sqrt(
            Math.Pow(sourceCentroid.X - clipCentroid.X, 2)
            + Math.Pow(sourceCentroid.Y - clipCentroid.Y, 2));
        var clipBounds = GetBounds(clipSamples);
        var clipWidth = clipBounds.MaxX - clipBounds.MinX;
        var clipHeight = clipBounds.MaxY - clipBounds.MinY;
        var centroidLimit = Math.Max(
            CentroidFloorIu,
            CentroidRelativeTolerance * Math.Sqrt(
                ((double)clipWidth * clipWidth) + ((double)clipHeight * clipHeight)));
        if (centroidDistance > centroidLimit)
        {
            reason =
                $"centroid distance {centroidDistance:F2} IU exceeds {centroidLimit:F2} IU";
            return false;
        }

        reason =
            $"areaRatio={areaRatio:P2} centroidDistance={centroidDistance:F2} " +
            $"maxVertexDistance={maxVertexDistance:F2} IU";
        return true;
    }

    internal static IReadOnlyList<Point2D> SampleLoop(
        IReadOnlyList<OutlineSegment> loop,
        int arcSamples)
    {
        var result = new List<Point2D>();
        foreach (var segment in loop)
        {
            result.Add(segment.Start);
            if (segment.SweepDirection is null || segment.Radius <= 0 || arcSamples < 2)
                continue;

            var startAngle = Math.Atan2(
                (segment.Start.YIu ?? 0) - (segment.Center.YIu ?? 0),
                (segment.Start.XIu ?? 0) - (segment.Center.XIu ?? 0));
            var endAngle = Math.Atan2(
                (segment.End.YIu ?? 0) - (segment.Center.YIu ?? 0),
                (segment.End.XIu ?? 0) - (segment.Center.XIu ?? 0));
            var delta = segment.SweepDirection == ArcSweepDirection.CounterClockwise
                ? NormalizeAngle(endAngle - startAngle)
                : NormalizeAngle(startAngle - endAngle);
            if (delta < 1e-12)
                delta = Math.PI * 2.0;

            var direction = segment.SweepDirection == ArcSweepDirection.CounterClockwise ? 1.0 : -1.0;
            for (var i = 1; i < arcSamples; i++)
            {
                var angle = startAngle + (direction * delta * i / arcSamples);
                result.Add(Polar(segment.Center, segment.Radius, angle));
            }
        }

        return result;
    }

    internal static (int MinX, int MinY, int MaxX, int MaxY) GetBounds(IEnumerable<Point2D> points)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var point in points)
        {
            if (point.XIu is not int x || point.YIu is not int y)
                continue;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        return minX == int.MaxValue ? (0, 0, 0, 0) : (minX, minY, maxX, maxY);
    }

    private static Point2D Polar(Point2D centre, int radius, double angle)
    {
        var cx = centre.XIu ?? 0;
        var cy = centre.YIu ?? 0;
        return Point2D.FromIu(
            (int)Math.Round(cx + (radius * Math.Cos(angle)), MidpointRounding.AwayFromZero),
            (int)Math.Round(cy + (radius * Math.Sin(angle)), MidpointRounding.AwayFromZero));
    }

    private static double NormalizeAngle(double angle)
    {
        const double twoPi = Math.PI * 2.0;
        var result = angle % twoPi;
        return result < 0 ? result + twoPi : result;
    }

    internal static bool TryGetPolygonMetrics(
        IReadOnlyList<Point2D> points,
        out double area,
        out double centroidX,
        out double centroidY)
    {
        var signedArea = PolygonArea(points);
        area = Math.Abs(signedArea);
        if (points.Count < 3 || area <= 0)
        {
            centroidX = 0;
            centroidY = 0;
            return false;
        }

        var centroid = PolygonCentroid(points, signedArea);
        centroidX = centroid.X;
        centroidY = centroid.Y;
        return true;
    }

    private static double PolygonArea(IReadOnlyList<Point2D> points)
    {
        if (points.Count < 3)
            return 0;

        var sum = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var next = (i + 1) % points.Count;
            sum += ((double)(points[i].XIu ?? 0) * (points[next].YIu ?? 0))
                - ((double)(points[next].XIu ?? 0) * (points[i].YIu ?? 0));
        }

        return sum / 2.0;
    }

    private static (double X, double Y) PolygonCentroid(
        IReadOnlyList<Point2D> points,
        double area)
    {
        if (points.Count < 3 || Math.Abs(area) < double.Epsilon)
            return (0, 0);

        var cx = 0.0;
        var cy = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var next = (i + 1) % points.Count;
            var cross = ((double)(points[i].XIu ?? 0) * (points[next].YIu ?? 0))
                - ((double)(points[next].XIu ?? 0) * (points[i].YIu ?? 0));
            cx += ((double)(points[i].XIu ?? 0) + (points[next].XIu ?? 0)) * cross;
            cy += ((double)(points[i].YIu ?? 0) + (points[next].YIu ?? 0)) * cross;
        }

        var divisor = 6.0 * area;
        return (cx / divisor, cy / divisor);
    }

    private static string Format(Point2D point) =>
        $"({point.XIu},{point.YIu})";
}
