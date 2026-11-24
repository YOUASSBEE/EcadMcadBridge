namespace EcadMcadBridge.Core.Model;

/// <summary>Placement in the neutral coordinate system. Unknown fields stay null.</summary>
public sealed class Transform
{
    public Point2D Position { get; init; }

    public double? RotationDeg { get; init; }

    public BoardSide? Side { get; init; }
}

public sealed class ComponentInstance
{
    public string Id { get; init; } = string.Empty;

    public string Designator { get; init; } = string.Empty;

    public string? Comment { get; init; }

    public string? Footprint { get; init; }

    /// <summary>Original CAD identifier for provenance only; its stability is not assumed (D-010).</summary>
    public string? SourceUniqueId { get; init; }

    public string LayerId { get; init; } = string.Empty;

    public Transform Placement { get; init; } = new();

    public LengthValue Height { get; init; } = LengthValue.FromIu(null);

    public bool? Fitted { get; init; }
}

public sealed class PadShape
{
    public string Id { get; init; } = string.Empty;

    public PadShapeKind? Kind { get; init; }

    public string? LayerId { get; init; }

    public LengthValue Width { get; init; } = LengthValue.FromIu(null);

    public LengthValue Height { get; init; } = LengthValue.FromIu(null);

    public LengthValue CornerRadius { get; init; } = LengthValue.FromIu(null);

    public IReadOnlyList<Point2D> Vertices { get; init; } = Array.Empty<Point2D>();
}

public sealed class PadStack
{
    public string Id { get; init; } = string.Empty;

    public IReadOnlyList<PadShape> Shapes { get; init; } = Array.Empty<PadShape>();
}

public sealed class Pad
{
    public string Id { get; init; } = string.Empty;

    public string ComponentId { get; init; } = string.Empty;

    public string? NetId { get; init; }

    public string LayerId { get; init; } = string.Empty;

    public string? PadStackId { get; init; }

    public Point2D Position { get; init; }

    public double? RotationDeg { get; init; }
}
