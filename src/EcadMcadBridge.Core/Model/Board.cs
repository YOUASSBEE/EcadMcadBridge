namespace EcadMcadBridge.Core.Model;

/// <summary>
/// Neutral, CAD-independent snapshot of a PCB. Field order and JSON shape belong to the
/// exchange contract and are written by <c>BoardSnapshotJson</c>.
/// </summary>
public sealed class BoardSnapshot
{
    public const string SchemaName = "ecad-mcad-bridge/board-snapshot";

    public const string SchemaVersionValue = "1.0.0";

    public BoardSnapshotSource Source { get; init; } = new();

    public string SourceUnits { get; init; } = "altium-iu";

    public string Units { get; init; } = "mm";

    public BoardProperties Properties { get; init; } = new();

    public BoardOutline Outline { get; init; } = new();

    public IReadOnlyList<LayerRef> Layers { get; init; } = Array.Empty<LayerRef>();

    public IReadOnlyList<ComponentInstance> Components { get; init; } = Array.Empty<ComponentInstance>();

    public IReadOnlyList<PadStack> PadStacks { get; init; } = Array.Empty<PadStack>();

    public IReadOnlyList<Pad> Pads { get; init; } = Array.Empty<Pad>();

    public IReadOnlyList<Net> Nets { get; init; } = Array.Empty<Net>();

    public IReadOnlyList<Track> Tracks { get; init; } = Array.Empty<Track>();

    public IReadOnlyList<Via> Vias { get; init; } = Array.Empty<Via>();

    public IReadOnlyList<Region> Regions { get; init; } = Array.Empty<Region>();

    public IReadOnlyList<TextItem> Texts { get; init; } = Array.Empty<TextItem>();
}

/// <summary>Where the snapshot came from. No absolute document path is part of the contract.</summary>
public sealed class BoardSnapshotSource
{
    public string Cad { get; init; } = "altium";

    public string? CadVersion { get; init; }

    public string? DocumentFileName { get; init; }

    public DateTimeOffset? ExportedAtUtc { get; init; }

    public string? ToolVersion { get; init; }
}

public sealed class BoardProperties
{
    public string Id { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? Material { get; init; }

    public LengthValue Thickness { get; init; } = LengthValue.FromIu(null);
}

public sealed class BoardOutline
{
    public string Id { get; init; } = string.Empty;

    public IReadOnlyList<Contour> Contours { get; init; } = Array.Empty<Contour>();
}

public sealed class LayerRef
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public LayerKind? Kind { get; init; }

    public BoardSide? Side { get; init; }
}
