using EcadMcadBridge.Core.Model;
using PCB;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>
/// Reads every contour exposed by the board-outline geometric polygon. Analytic line/arc
/// segments are matched to those contours; unmatched contours get an explicitly diagnosed
/// geometric fallback instead of being dropped.
/// </summary>
internal static class AltiumOutlineReader
{
    private const string OutlineId = "altium:board-outline";

    public static BoardOutline Read(
        IPCB_Board board,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics,
        IReadOnlyList<AltiumRegionInfo> regions)
    {
        try
        {
            var outline = board.GetState_BoardOutline();
            if (outline is null)
            {
                warnings.Add("Board outline API returned null.");
                return new BoardOutline { Id = OutlineId };
            }

            var geometricContours = ReadGeometricContours(outline, warnings, diagnostics);
            if (geometricContours.Count == 0)
            {
                warnings.Add("Board outline geometric polygon returned no usable contours.");
                return new BoardOutline { Id = OutlineId };
            }

            var analyticLoops = ReadAnalyticLoops(outline, warnings, diagnostics);
            var usedLoops = new bool[analyticLoops.Count];
            var contours = new List<Contour>(geometricContours.Count);

            foreach (var geometric in geometricContours)
            {
                var loopIndex = FindMatchingLoop(geometric, analyticLoops, usedLoops, warnings, diagnostics);
                if (loopIndex >= 0)
                {
                    usedLoops[loopIndex] = true;
                    contours.Add(MakeAnalyticContour(geometric, analyticLoops[loopIndex]));
                    diagnostics.ExactOutlineContours++;
                }
                else if (geometric.Points.Count >= 2)
                {
                    contours.Add(MakeGeometricContour(geometric));
                    diagnostics.FallbackOutlineContours++;
                    warnings.Add(
                        $"Board outline contour {geometric.Index} ({geometric.Role}) has no matched analytic loop; " +
                        "geometric fallback uses line segments and may approximate/lose arcs.");
                }
                else
                {
                    diagnostics.OmittedOutlineContours++;
                    warnings.Add(
                        $"Board outline contour {geometric.Index} ({geometric.Role}) has fewer than two points and was omitted.");
                }
            }

            var unmatched = usedLoops.Count(used => !used);
            if (unmatched > 0)
            {
                diagnostics.UnmatchedAnalyticLoops += unmatched;
                warnings.Add($"{unmatched} analytic board-outline loop(s) could not be matched to geometric contours and were not exported separately.");
            }

            contours.AddRange(ReadBoardCutoutContours(outline, regions, geometricContours, warnings, diagnostics));

            return new BoardOutline { Id = OutlineId, Contours = contours };
        }
        catch (Exception ex)
        {
            diagnostics.OutlineLoopErrors++;
            warnings.Add("Board outline read failed: " + ex.GetType().Name + ".");
            return new BoardOutline { Id = OutlineId };
        }
    }

    private static IReadOnlyList<Contour> ReadBoardCutoutContours(
        IPCB_BoardOutline outline,
        IReadOnlyList<AltiumRegionInfo> regions,
        IReadOnlyList<GeometricContour> geometricContours,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var cutoutRegions = regions
            .Where(region => region.Kind == TRegionKind.eRegionKind_BoardCutout)
            .ToArray();
        var contours = new List<Contour>();
        var clipped = false;

        try
        {
            var clip = outline.ClipWithPolyRegShapes(false, false);
            if (clip is null)
            {
                warnings.Add("ClipWithPolyRegShapes(false,false) returned null; Board Cutout regions cannot be clipped.");
            }
            else
            {
                clipped = true;
                var shapes = clip.GetCutouts();
                diagnostics.ClipCutoutsCount = shapes?.Count() ?? 0;
                diagnostics.ClipNewPrimsCount = clip.GetNewPrims()?.Count() ?? 0;
                diagnostics.ClipInoperablePrimsCount = clip.GetInoperablePrims()?.Count() ?? 0;

                if (shapes is not null)
                {
                    var shapeCount = shapes.Count();
                    for (var i = 0; i < shapeCount; i++)
                    {
                        var shape = shapes.Items(i);
                        if (shape is null)
                        {
                            diagnostics.ClipCutoutErrors++;
                            warnings.Add($"Clipped Board Cutout shape {i} is null.");
                            continue;
                        }

                        var loops = ReadShapeLoops(shape, i, warnings, diagnostics);
                        foreach (var loop in loops)
                        {
                            var owner = FindMatchingCutoutRegion(loop, cutoutRegions);
                            if (owner is null)
                            {
                                diagnostics.ClipUnmatchedCutoutRegions++;
                                warnings.Add(
                                    $"Clipped cutout shape {i} does not match any eRegionKind_BoardCutout region by bounds; " +
                                    "it was not exported.");
                                continue;
                            }

                            diagnostics.ClipMatchedCutoutRegions++;
                            warnings.Add(
                                $"Clipped cutout shape {i} matched Board Cutout region {owner.Index} " +
                                $"(area={owner.Area}).");

                            var points = loop
                                .SelectMany(segment => new[] { segment.Start, segment.End })
                                .ToArray();
                            if (MatchesExistingGeometricHole(points, loop, geometricContours))
                            {
                                diagnostics.DuplicateCutoutContours++;
                                continue;
                            }

                            diagnostics.ClipCutoutContours++;
                            diagnostics.ExportedCutoutContours++;
                            contours.Add(new Contour
                            {
                                Id = "altium:contour:cutout:clip:" +
                                    diagnostics.ExportedCutoutContours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                Role = ContourRole.Hole,
                                Segments = loop.Select(ToCoreSegment).ToArray(),
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            diagnostics.ClipCutoutErrors++;
            warnings.Add(
                "ClipWithPolyRegShapes(false,false) failed: " + ex.GetType().Name +
                "; Board Cutout regions are not exported.");
        }

        if (cutoutRegions.Length > 0 && !clipped)
        {
            warnings.Add(
                $"{cutoutRegions.Length} IPCB_Region object(s) have TRegionKind.eRegionKind_BoardCutout, " +
                "but ClipWithPolyRegShapes(false,false) returned no clip result; " +
                "their contours are left as diagnostics and are not exported as outline holes.");
        }

        if (cutoutRegions.Length == 0 && diagnostics.ClipCutoutsCount == 0)
        {
            warnings.Add(
                "No IPCB_Region with TRegionKind.eRegionKind_BoardCutout was found and the outline clipper returned no cutouts.");
        }
        else if (diagnostics.ExportedCutoutContours == 0)
        {
            warnings.Add(
                $"Board Cutout evidence is present but no cutout contour was exported " +
                $"(regions={cutoutRegions.Length}, clipCutouts={diagnostics.ClipCutoutsCount}).");
        }
        else
        {
            warnings.Add(
                $"Exporting {diagnostics.ExportedCutoutContours} Board Cutout hole contour(s) " +
                $"(regions={cutoutRegions.Length}, clipCutouts={diagnostics.ClipCutoutsCount}, " +
                $"duplicates={diagnostics.DuplicateCutoutContours}).");
        }

        return contours;
    }

    private static IReadOnlyList<List<OutlineSegment>> ReadShapeLoops(
        IPCB_RegionShape shape,
        int shapeIndex,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var count = shape.GetState_ShapeSegmentCount();
        if (count < 2)
        {
            diagnostics.ClipCutoutErrors++;
            warnings.Add($"Clipped Board Cutout shape {shapeIndex} has {count} segment(s); a closed loop needs at least two.");
            return Array.Empty<List<OutlineSegment>>();
        }

        var raw = new List<RawSegment>(count);
        for (var i = 0; i < count; i++)
        {
            var segment = shape.GetState_ShapeSegment(i);
            if (segment is null)
            {
                diagnostics.ClipCutoutErrors++;
                warnings.Add($"Clipped Board Cutout shape {shapeIndex} segment {i} is null.");
                return Array.Empty<List<OutlineSegment>>();
            }

            raw.Add(new RawSegment(
                segment.Kind,
                segment.Vx,
                segment.Vy,
                segment.Cx,
                segment.Cy,
                segment.Radius,
                segment.Angle1,
                segment.Angle2));
        }

        var segments = BuildOutlineSegments(raw);
        var (loops, invalid) = BuildLoops(segments);
        if (invalid > 0)
        {
            diagnostics.ClipCutoutErrors += invalid;
            warnings.Add(
                $"Clipped Board Cutout shape {shapeIndex} has {invalid} open/invalid segment sequence(s); " +
                "only closed loops are exported.");
        }

        diagnostics.ClipCutoutSegments += count;
        return loops;
    }

    private static AltiumRegionInfo? FindMatchingCutoutRegion(
        IReadOnlyList<OutlineSegment> loop,
        IReadOnlyList<AltiumRegionInfo> cutoutRegions)
    {
        const int toleranceIu = 10;
        var points = loop.SelectMany(segment => new[] { segment.Start, segment.End });
        var (minX, minY, maxX, maxY) = GetBounds(points);
        foreach (var region in cutoutRegions)
        {
            if (region.Contours.Count == 0)
                continue;
            if (Math.Abs(region.MinX - minX) <= toleranceIu
                && Math.Abs(region.MinY - minY) <= toleranceIu
                && Math.Abs(region.MaxX - maxX) <= toleranceIu
                && Math.Abs(region.MaxY - maxY) <= toleranceIu)
                return region;
        }

        return null;
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) GetBounds(IEnumerable<Point2D> points)
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

    private static bool MatchesExistingGeometricHole(
        IReadOnlyList<Point2D> points,
        IReadOnlyList<OutlineSegment> loop,
        IReadOnlyList<GeometricContour> geometricContours)
    {
        var closed = EnsureClosed(points);
        foreach (var geometric in geometricContours)
        {
            if (geometric.Role != ContourRole.Hole || geometric.Points.Count < 3)
                continue;
            if (OutlineContourMatcher.TryExactMatch(closed, loop))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<Point2D> EnsureClosed(IReadOnlyList<Point2D> points)
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

    private static IReadOnlyList<GeometricContour> ReadGeometricContours(
        IPCB_BoardOutline outline,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var geometric = outline.BoardOutline_GeometricPolygon();
        if (geometric is null)
        {
            warnings.Add("Board outline geometric polygon is unavailable.");
            return Array.Empty<GeometricContour>();
        }

        var result = new List<GeometricContour>();
        var count = geometric.GetState_Count();
        for (var i = 0; i < count; i++)
        {
            var contour = geometric.GetState_Contour(i);
            if (contour is null)
            {
                diagnostics.OutlineLoopErrors++;
                warnings.Add($"Board outline geometric contour {i} is null.");
                continue;
            }

            var points = new List<Point2D>();
            var pointCount = contour.GetState_Count();
            for (var p = 0; p < pointCount; p++)
                points.Add(Point2D.FromIu(contour.GetState_PointX(p), contour.GetState_PointY(p)));

            var role = geometric.GetState_IsHole(i) ? ContourRole.Hole : ContourRole.Outer;
            result.Add(new GeometricContour(i, role, points));
        }

        diagnostics.GeometricContourCount = result.Count;
        diagnostics.GeometricPointCount = result.Sum(contour => contour.Points.Count);
        diagnostics.GeometricHoleCount = result.Count(contour => contour.Role == ContourRole.Hole);
        return result;
    }

    private static IReadOnlyList<List<OutlineSegment>> ReadAnalyticLoops(
        IPCB_BoardOutline outline,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var polygon = outline as IPCB_Polygon;
        if (polygon is null)
        {
            warnings.Add("Board outline does not expose IPCB_Polygon segments; geometric fallback will be used.");
            return Array.Empty<List<OutlineSegment>>();
        }

        var count = polygon.GetState_PointCount();
        if (count < 2)
            return Array.Empty<List<OutlineSegment>>();

        var raw = new List<RawSegment>(count);
        for (var i = 0; i < count; i++)
        {
            var segment = polygon.GetState_Segments(i);
            if (segment is null)
            {
                diagnostics.OutlineLoopErrors++;
                warnings.Add($"Board outline segment {i} is null; analytic outline is incomplete.");
                return Array.Empty<List<OutlineSegment>>();
            }

            raw.Add(new RawSegment(
                segment.Kind,
                segment.Vx,
                segment.Vy,
                segment.Cx,
                segment.Cy,
                segment.Radius,
                segment.Angle1,
                segment.Angle2));
        }

        var segments = BuildOutlineSegments(raw);
        var (loops, invalid) = BuildLoops(segments);
        if (invalid > 0)
        {
            diagnostics.OutlineLoopErrors += invalid;
            warnings.Add($"{invalid} analytic board-outline loop(s) are not closed/continuous and were not used.");
        }

        return loops;
    }

    private static int FindMatchingLoop(
        GeometricContour geometric,
        IReadOnlyList<List<OutlineSegment>> loops,
        bool[] usedLoops,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var matches = OutlineContourMatcher.FindExactMatches(geometric.Points, loops, usedLoops);
        if (matches.Count == 1)
            return matches[0];

        if (matches.Count > 1)
        {
            diagnostics.AmbiguousOutlineMatches++;
            warnings.Add(
                $"Board outline contour {geometric.Index} ({geometric.Role}) has {matches.Count} exact analytic-loop matches; " +
                "geometric fallback was used rather than choosing arbitrarily.");
            return -1;
        }

        var partial = false;
        for (var i = 0; i < loops.Count; i++)
        {
            if (usedLoops[i])
                continue;
            if (OutlineContourMatcher.HasAnySharedPoint(geometric.Points, loops[i]))
            {
                partial = true;
                break;
            }
        }

        if (partial)
        {
            diagnostics.PartialOutlineMatches++;
            var explanation = string.Empty;
            for (var i = 0; i < loops.Count; i++)
            {
                if (usedLoops[i] || !OutlineContourMatcher.HasAnySharedPoint(geometric.Points, loops[i]))
                    continue;
                explanation = " " + OutlineContourMatcher.ExplainExactMatch(geometric.Points, loops[i]);
                break;
            }

            warnings.Add(
                $"Board outline contour {geometric.Index} ({geometric.Role}) has only a partial analytic-loop match; " +
                "exact matching was rejected and geometric fallback was used." + explanation);
        }

        return -1;
    }

    private static Contour MakeAnalyticContour(GeometricContour geometric, List<OutlineSegment> loop)
    {
        return new Contour
        {
            Id = "altium:contour:" + geometric.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Role = geometric.Role,
            Segments = loop.Select(ToCoreSegment).ToArray(),
        };
    }

    private static Contour MakeGeometricContour(GeometricContour geometric)
    {
        var segments = new List<ContourSegment>(geometric.Points.Count);
        for (var p = 0; p < geometric.Points.Count; p++)
        {
            var next = (p + 1) % geometric.Points.Count;
            if (p == geometric.Points.Count - 1 && PointsEqual(geometric.Points[p], geometric.Points[0]))
                break;

            segments.Add(new LineSegment
            {
                Start = geometric.Points[p],
                End = geometric.Points[next],
            });
        }

        return new Contour
        {
            Id = "altium:contour:" + geometric.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Role = geometric.Role,
            Segments = segments,
        };
    }

    private static (List<List<OutlineSegment>> Loops, int Invalid) BuildLoops(
        IReadOnlyList<OutlineSegment> segments)
    {
        var loops = new List<List<OutlineSegment>>();
        var current = new List<OutlineSegment>();
        var invalid = 0;

        foreach (var segment in segments)
        {
            if (current.Count > 0 && !PointsEqual(current[^1].End, segment.Start))
            {
                if (current.Count >= 2 && PointsEqual(current[^1].End, current[0].Start))
                    loops.Add(current);
                else
                    invalid++;
                current = new List<OutlineSegment>();
            }

            current.Add(segment);
            if (current.Count > 1 && PointsEqual(current[^1].End, current[0].Start))
            {
                loops.Add(current);
                current = new List<OutlineSegment>();
            }
        }

        if (current.Count > 0)
        {
            if (current.Count >= 2 && PointsEqual(current[^1].End, current[0].Start))
                loops.Add(current);
            else
                invalid++;
        }

        return (loops, invalid);
    }

    private static List<OutlineSegment> BuildOutlineSegments(IReadOnlyList<RawSegment> raw)
    {
        var segments = new List<OutlineSegment>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var current = raw[i];
            var next = raw[(i + 1) % raw.Count];
            var start = StartPoint(current);
            var end = current.Kind == TPolySegmentType.ePolySegmentArc
                ? Polar(current.Cx, current.Cy, current.Radius, current.Angle2)
                : next.Kind == TPolySegmentType.ePolySegmentArc
                    ? StartPoint(next)
                    : Point2D.FromIu(next.Vx, next.Vy);

            if (current.Kind == TPolySegmentType.ePolySegmentArc)
            {
                var delta = current.Angle2 - current.Angle1;
                segments.Add(new OutlineSegment(
                    start,
                    end,
                    Point2D.FromIu(current.Cx, current.Cy),
                    current.Radius,
                    delta >= 0 ? ArcSweepDirection.CounterClockwise : ArcSweepDirection.Clockwise));
            }
            else
            {
                segments.Add(new OutlineSegment(start, end, default, 0, null));
            }
        }

        return segments;
    }

    private static Point2D StartPoint(RawSegment segment)
    {
        return segment.Kind == TPolySegmentType.ePolySegmentArc
            ? Polar(segment.Cx, segment.Cy, segment.Radius, segment.Angle1)
            : Point2D.FromIu(segment.Vx, segment.Vy);
    }

    private static Point2D Polar(int centreX, int centreY, int radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var x = (int)Math.Round(centreX + (radius * Math.Cos(radians)), MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(centreY + (radius * Math.Sin(radians)), MidpointRounding.AwayFromZero);
        return Point2D.FromIu(x, y);
    }

    private static bool PointsEqual(Point2D left, Point2D right) =>
        left.XIu == right.XIu && left.YIu == right.YIu;

    private static ContourSegment ToCoreSegment(OutlineSegment segment)
    {
        if (segment.SweepDirection is null)
            return new LineSegment { Start = segment.Start, End = segment.End };

        return new ArcSegment
        {
            Start = segment.Start,
            End = segment.End,
            Center = segment.Center,
            Radius = LengthValue.FromIu(segment.Radius),
            SweepDirection = segment.SweepDirection,
        };
    }

    private sealed record GeometricContour(
        int Index,
        ContourRole Role,
        IReadOnlyList<Point2D> Points);

    private sealed record RawSegment(
        TPolySegmentType Kind,
        int Vx,
        int Vy,
        int Cx,
        int Cy,
        int Radius,
        double Angle1,
        double Angle2);

}
