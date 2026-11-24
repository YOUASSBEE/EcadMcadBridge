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
        AltiumAdapterDiagnostics diagnostics)
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

            return new BoardOutline { Id = OutlineId, Contours = contours };
        }
        catch (Exception ex)
        {
            diagnostics.OutlineLoopErrors++;
            warnings.Add("Board outline read failed: " + ex.GetType().Name + ".");
            return new BoardOutline { Id = OutlineId };
        }
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
        var candidates = new List<(int Index, int Score)>();
        for (var i = 0; i < loops.Count; i++)
        {
            if (usedLoops[i])
                continue;

            var loopPoints = new List<Point2D>();
            foreach (var segment in loops[i])
            {
                loopPoints.Add(segment.Start);
                loopPoints.Add(segment.End);
            }

            var score = OutlineContourMatcher.CountSharedPoints(geometric.Points, loopPoints);
            if (score > 0)
                candidates.Add((i, score));
        }

        if (candidates.Count == 0)
            return -1;

        var maxScore = candidates.Max(item => item.Score);
        var best = candidates.Where(item => item.Score == maxScore).ToArray();
        if (best.Length > 1)
        {
            diagnostics.OutlineLoopErrors++;
            warnings.Add(
                $"Board outline contour {geometric.Index} ({geometric.Role}) has an ambiguous analytic-loop match; " +
                "geometric fallback was used rather than guessing the contour.");
            return -1;
        }

        return best[0].Index;
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

    private sealed record OutlineSegment(
        Point2D Start,
        Point2D End,
        Point2D Center,
        int Radius,
        ArcSweepDirection? SweepDirection);
}
