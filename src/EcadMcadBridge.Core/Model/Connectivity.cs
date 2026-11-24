namespace EcadMcadBridge.Core.Model;

public sealed class Net
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? NetClass { get; init; }
}

public sealed class Track
{
    public string Id { get; init; } = string.Empty;

    public string? NetId { get; init; }

    public string LayerId { get; init; } = string.Empty;

    public Point2D Start { get; init; }

    public Point2D End { get; init; }

    public LengthValue Width { get; init; } = LengthValue.FromIu(null);
}

public sealed class Via
{
    public string Id { get; init; } = string.Empty;

    public string? NetId { get; init; }

    public Point2D Position { get; init; }

    public LengthValue Diameter { get; init; } = LengthValue.FromIu(null);

    public LengthValue HoleDiameter { get; init; } = LengthValue.FromIu(null);

    public string StartLayerId { get; init; } = string.Empty;

    public string EndLayerId { get; init; } = string.Empty;
}

public sealed class Region
{
    public string Id { get; init; } = string.Empty;

    public string? NetId { get; init; }

    public string LayerId { get; init; } = string.Empty;

    public RegionKind? Kind { get; init; }

    public PolygonGeometry Polygon { get; init; } = new();
}

public sealed class TextItem
{
    public string Id { get; init; } = string.Empty;

    public string? ComponentId { get; init; }

    public string LayerId { get; init; } = string.Empty;

    public Point2D Position { get; init; }

    public string Text { get; init; } = string.Empty;

    public LengthValue Height { get; init; } = LengthValue.FromIu(null);

    public LengthValue StrokeWidth { get; init; } = LengthValue.FromIu(null);

    public double? RotationDeg { get; init; }
}
