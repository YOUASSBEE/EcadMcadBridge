using EcadMcadBridge.Core.Model;
using PCB;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>
/// Reads the board outline. The geometric polygon supplies contour roles; the polygon segment
/// API is used when available so arcs remain analytic instead of being expanded into lines.
/// </summary>
internal static class AltiumOutlineReader
{
    private const string OutlineId = "altium:board-outline";

    public static BoardOutline Read(IPCB_Board board, List<string> warnings)
    {
        try
        {
            var outline = board.GetState_BoardOutline();
            if (outline is null)
            {
                warnings.Add("Board outline API returned null.");
                return new BoardOutline { Id = OutlineId };
            }

            var fromSegments = TryReadFromSegments(outline, warnings);
            if (fromSegments is not null)
                return fromSegments;

            return ReadGeometric(outline, warnings);
        }
        catch (Exception ex)
        {
            warnings.Add("Board outline read failed: " + ex.GetType().Name + ".");
            return new BoardOutline { Id = OutlineId };
        }
    }

    private static BoardOutline? TryReadFromSegments(IPCB_BoardOutline outline, List<string> warnings)
    {
        var polygon = outline as IPCB_Polygon;
        if (polygon is null)
            return null;

        var count = polygon.GetState_PointCount();
        if (count < 2)
            return null;

        var raw = new List<RawSegment>(count);
        for (var i = 0; i < count; i++)
        {
            var segment = polygon.GetState_Segments(i);
            if (segment is null)
                return null;

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

        var segments = new List<OutlineSegment>(count);
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

        var loops = BuildLoops(segments);
        if (loops is null)
        {
            warnings.Add("Board outline segment loops could not be closed; geometric polygon fallback was used.");
            return null;
        }

        var roles = ReadContourRoles(outline, loops, warnings);
        var contours = new List<Contour>(loops.Count);
        for (var i = 0; i < loops.Count; i++)
        {
            var loop = loops[i];
            contours.Add(new Contour
            {
                Id = "altium:contour:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Role = roles[i],
                Segments = loop.Select(ToCoreSegment).ToArray(),
            });
        }

        return new BoardOutline { Id = OutlineId, Contours = contours };
    }

    private static BoardOutline ReadGeometric(IPCB_BoardOutline outline, List<string> warnings)
    {
        var contours = new List<Contour>();
        var geometric = outline.BoardOutline_GeometricPolygon();
        if (geometric is null)
        {
            warnings.Add("Board outline geometric polygon is unavailable.");
            return new BoardOutline { Id = OutlineId };
        }

        var count = geometric.GetState_Count();
        for (var i = 0; i < count; i++)
        {
            var contour = geometric.GetState_Contour(i);
            if (contour is null)
                continue;

            var points = new List<Point2D>();
            var pointCount = contour.GetState_Count();
            for (var p = 0; p < pointCount; p++)
                points.Add(Point2D.FromIu(contour.GetState_PointX(p), contour.GetState_PointY(p)));

            if (points.Count < 2)
                continue;

            var segments = new List<ContourSegment>(points.Count);
            for (var p = 0; p < points.Count; p++)
            {
                segments.Add(new LineSegment
                {
                    Start = points[p],
                    End = points[(p + 1) % points.Count],
                });
            }

            contours.Add(new Contour
            {
                Id = "altium:contour:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Role = geometric.GetState_IsHole(i) ? ContourRole.Hole : ContourRole.Outer,
                Segments = segments,
            });
        }

        if (contours.Count == 0)
            warnings.Add("Board outline contains no usable contours.");

        return new BoardOutline { Id = OutlineId, Contours = contours };
    }

    private static List<List<OutlineSegment>>? BuildLoops(List<OutlineSegment> segments)
    {
        var loops = new List<List<OutlineSegment>>();
        var current = new List<OutlineSegment>();
        foreach (var segment in segments)
        {
            if (current.Count > 0 && !PointsEqual(current[^1].End, segment.Start))
            {
                if (!PointsEqual(current[^1].End, current[0].Start))
                    return null;
                loops.Add(current);
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
            if (!PointsEqual(current[^1].End, current[0].Start))
                return null;
            loops.Add(current);
        }

        return loops.Count == 0 ? null : loops;
    }

    private static IReadOnlyList<ContourRole> ReadContourRoles(
        IPCB_BoardOutline outline,
        IReadOnlyList<List<OutlineSegment>> loops,
        List<string> warnings)
    {
        var roles = new ContourRole[loops.Count];
        var geometric = outline.BoardOutline_GeometricPolygon();
        if (geometric is null)
        {
            for (var i = 0; i < roles.Length; i++)
                roles[i] = i == 0 ? ContourRole.Outer : ContourRole.Hole;
            warnings.Add("Board outline hole roles are unavailable; contour order was used.");
            return roles;
        }

        var contourCount = geometric.GetState_Count();
        for (var i = 0; i < loops.Count; i++)
        {
            var matched = false;
            for (var c = 0; c < contourCount; c++)
            {
                if (!ContourContains(geometric.GetState_Contour(c), loops[i][0].Start))
                    continue;

                roles[i] = geometric.GetState_IsHole(c) ? ContourRole.Hole : ContourRole.Outer;
                matched = true;
                break;
            }

            if (!matched)
            {
                roles[i] = i == 0 ? ContourRole.Outer : ContourRole.Hole;
                warnings.Add("A board outline contour could not be matched to a geometric-polygon role.");
            }
        }

        return roles;
    }

    private static bool ContourContains(IPCB_Contour? contour, Point2D point)
    {
        if (contour is null)
            return false;

        var count = contour.GetState_Count();
        for (var i = 0; i < count; i++)
        {
            if (contour.GetState_PointX(i) == point.XIu && contour.GetState_PointY(i) == point.YIu)
                return true;
        }

        return false;
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
