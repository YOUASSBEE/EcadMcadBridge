namespace EcadMcadBridge.Core.Model;

/// <summary>Coordinate frame contract of the exported board STEP.</summary>
public sealed class BoardModelFrame
{
    public const string OriginValue = "outer-contour-minimum-xy-top-surface-z0";
    public const string XAxisValue = "positive-altium-x";
    public const string YAxisValue = "positive-altium-y";
    public const string ZAxisValue = "positive-outward-from-top";
    public const string SubstrateSideValue = "negative-z";

    /// <summary>XY origin at the analytic Outer-contour minimum; Z=0 at the top surface.</summary>
    public string Origin { get; init; } = OriginValue;

    /// <summary>Positive X follows positive Altium X.</summary>
    public string XAxis { get; init; } = XAxisValue;

    /// <summary>Positive Y follows positive Altium Y.</summary>
    public string YAxis { get; init; } = YAxisValue;

    /// <summary>Positive Z points outward from the Top side.</summary>
    public string ZAxis { get; init; } = ZAxisValue;

    /// <summary>The substrate occupies negative Z.</summary>
    public string SubstrateSide { get; init; } = SubstrateSideValue;

    public static BoardModelFrame Reference => new();

    public bool IsReference =>
        string.Equals(Origin, OriginValue, StringComparison.Ordinal)
        && string.Equals(XAxis, XAxisValue, StringComparison.Ordinal)
        && string.Equals(YAxis, YAxisValue, StringComparison.Ordinal)
        && string.Equals(ZAxis, ZAxisValue, StringComparison.Ordinal)
        && string.Equals(SubstrateSide, SubstrateSideValue, StringComparison.Ordinal);
}

/// <summary>
/// Versioned descriptor of the board-only STEP asset. It is independent of the component
/// model catalog and never describes a component.
/// </summary>
public sealed class BoardModelDescriptor
{
    public const string SchemaName = "ecad-mcad-bridge/board-model";

    public const string SchemaVersionValue = "1.0.0";

    public const string FileRelativePath = "board.model.json";

    public const string AssetDirectory = "assets/board";

    public const string Capability = "board-3d-model";

    public string Schema { get; init; } = SchemaName;

    public string SchemaVersion { get; init; } = SchemaVersionValue;

    /// <summary>Content-addressed relative package path: <c>assets/board/&lt;sha256&gt;.step</c>.</summary>
    public string AssetPath { get; init; } = string.Empty;

    public ComponentModelAssetFormat AssetFormat { get; init; } = ComponentModelAssetFormat.StepAp214;

    public string Sha256 { get; init; } = string.Empty;

    public long Size { get; init; }

    public string Units { get; init; } = "mm";

    public BoardModelFrame Frame { get; init; } = new();

    /// <summary>Nominal Altium thickness from the snapshot; reference only, not measured STEP geometry.</summary>
    public LengthValue NominalThickness { get; init; } = LengthValue.FromIu(null);
}

/// <summary>Caller-supplied board asset source. The absolute path is never serialized.</summary>
public sealed class BoardModelWriteRequest
{
    public string SourcePath { get; init; } = string.Empty;

    public ComponentModelAssetFormat Format { get; init; } = ComponentModelAssetFormat.StepAp214;
}
