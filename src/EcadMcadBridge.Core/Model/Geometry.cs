namespace EcadMcadBridge.Core.Model;

public abstract class ContourSegment
{
    public Point2D Start { get; init; }

    public Point2D End { get; init; }
}

/// <summary>A straight segment. Endpoints are exact IU points, not an approximation of an arc.</summary>
public sealed class LineSegment : ContourSegment
{
}

/// <summary>
/// An analytic circular arc. Start, end, centre and radius are all explicit; the arc is never
/// converted into a chain of straight segments.
/// </summary>
public sealed class ArcSegment : ContourSegment
{
    public Point2D Center { get; init; }

    public LengthValue Radius { get; init; } = LengthValue.FromIu(null);

    public ArcSweepDirection? SweepDirection { get; init; }
}

public sealed class Contour
{
    public string Id { get; init; } = string.Empty;

    public ContourRole? Role { get; init; }

    public IReadOnlyList<ContourSegment> Segments { get; init; } = Array.Empty<ContourSegment>();
}

public sealed class PolygonGeometry
{
    public IReadOnlyList<Contour> Contours { get; init; } = Array.Empty<Contour>();
}
