using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.SolidWorks.Placement;

/// <summary>Axis-aligned bounds of the outer board contour in the Altium board frame.</summary>
internal readonly record struct BoardBounds(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>
/// Computes exact line/arc bounds for the Outer contour. Arc centers are never bounds points;
/// only start/end and cardinal points inside the actual CW/CCW sweep are included.
/// </summary>
internal static class BoardOutlineBounds
{
    private const double ToleranceMm = 1e-5;
    private const double TwoPi = 2.0 * Math.PI;

    public static bool TryCompute(
        BoardOutline? outline,
        out BoardBounds bounds,
        out string error)
    {
        bounds = default;
        error = string.Empty;

        var outer = (outline?.Contours ?? Array.Empty<Contour>())
            .Where(contour => contour.Role == ContourRole.Outer)
            .ToArray();
        if (outer.Length == 0)
        {
            error = "The board snapshot has no Outer contour.";
            return false;
        }

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var points = 0;

        foreach (var contour in outer)
        {
            if (contour.Segments.Count == 0)
            {
                error = "The Outer contour has no segments.";
                return false;
            }

            foreach (var segment in contour.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        if (!AddPoint(line.Start, "line start", out error) ||
                            !AddPoint(line.End, "line end", out error))
                            return false;
                        break;

                    case ArcSegment arc:
                        if (!AddArcPoints(arc, ref minX, ref minY, ref maxX, ref maxY, ref points, out error))
                            return false;
                        break;

                    default:
                        error = "The Outer contour contains an unsupported segment type.";
                        return false;
                }
            }
        }

        if (points == 0)
        {
            error = "The Outer contour has no finite geometry.";
            return false;
        }

        bounds = new BoardBounds(minX, minY, maxX, maxY);
        return true;

        bool AddPoint(Point2D point, string label, out string pointError)
        {
            if (point.XMm is not { } x || point.YMm is not { } y ||
                !double.IsFinite(x) || !double.IsFinite(y))
            {
                pointError = "The Outer contour " + label + " is not finite.";
                return false;
            }

            Include(x, y, ref minX, ref minY, ref maxX, ref maxY);
            points++;
            pointError = string.Empty;
            return true;
        }
    }

    private static bool AddArcPoints(
        ArcSegment arc,
        ref double minX,
        ref double minY,
        ref double maxX,
        ref double maxY,
        ref int points,
        out string error)
    {
        error = string.Empty;
        if (arc.Start.XMm is not { } startX ||
            arc.Start.YMm is not { } startY ||
            arc.End.XMm is not { } endX ||
            arc.End.YMm is not { } endY ||
            arc.Center.XMm is not { } centerX ||
            arc.Center.YMm is not { } centerY ||
            !double.IsFinite(startX) || !double.IsFinite(startY) ||
            !double.IsFinite(endX) || !double.IsFinite(endY) ||
            !double.IsFinite(centerX) || !double.IsFinite(centerY))
        {
            error = "Arc start, end or center is not finite.";
            return false;
        }

        if (arc.Radius.Mm is not { } radius || !double.IsFinite(radius) || radius <= 0)
        {
            error = "Arc radius is missing or not positive.";
            return false;
        }

        if (arc.SweepDirection is null)
        {
            error = "Arc sweep direction is missing.";
            return false;
        }

        var startRadius = Math.Sqrt(
            (startX - centerX) * (startX - centerX) +
            (startY - centerY) * (startY - centerY));
        var endRadius = Math.Sqrt(
            (endX - centerX) * (endX - centerX) +
            (endY - centerY) * (endY - centerY));
        if (Math.Abs(startRadius - radius) > ToleranceMm ||
            Math.Abs(endRadius - radius) > ToleranceMm)
        {
            error = "Arc start or end point does not lie on the declared radius.";
            return false;
        }

        if (Math.Abs(startX - endX) <= ToleranceMm &&
            Math.Abs(startY - endY) <= ToleranceMm)
        {
            error = "Arc start and end points are equal; full circles are not supported.";
            return false;
        }

        Include(startX, startY, ref minX, ref minY, ref maxX, ref maxY);
        Include(endX, endY, ref minX, ref minY, ref maxX, ref maxY);
        points += 2;

        var startAngle = Normalize(Math.Atan2(startY - centerY, startX - centerX));
        var endAngle = Normalize(Math.Atan2(endY - centerY, endX - centerX));
        var counterClockwise = arc.SweepDirection == ArcSweepDirection.CounterClockwise;
        var sweep = counterClockwise
            ? Normalize(endAngle - startAngle)
            : Normalize(startAngle - endAngle);
        if (sweep <= ToleranceMm / Math.Max(radius, 1.0))
        {
            error = "Arc has a zero sweep.";
            return false;
        }

        foreach (var (angle, x, y) in new[]
                 {
                     (0.0, centerX + radius, centerY),
                     (Math.PI / 2.0, centerX, centerY + radius),
                     (Math.PI, centerX - radius, centerY),
                     (3.0 * Math.PI / 2.0, centerX, centerY - radius),
                 })
        {
            var offset = counterClockwise
                ? Normalize(angle - startAngle)
                : Normalize(startAngle - angle);
            if (offset <= sweep + 1e-12)
                Include(x, y, ref minX, ref minY, ref maxX, ref maxY);
        }

        return true;
    }

    private static void Include(
        double x,
        double y,
        ref double minX,
        ref double minY,
        ref double maxX,
        ref double maxY)
    {
        minX = Math.Min(minX, x);
        minY = Math.Min(minY, y);
        maxX = Math.Max(maxX, x);
        maxY = Math.Max(maxY, y);
    }

    private static double Normalize(double angle)
    {
        var normalized = angle % TwoPi;
        return normalized < 0 ? normalized + TwoPi : normalized;
    }
}
