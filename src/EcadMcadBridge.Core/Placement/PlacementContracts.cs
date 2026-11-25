namespace EcadMcadBridge.Core.Placement;

/// <summary>The side on which the Altium component is placed.</summary>
public enum ComponentSide
{
    /// <summary>Top component layer.</summary>
    Top,

    /// <summary>Bottom component layer.</summary>
    Bottom,
}

/// <summary>The board side selected by the 3D body projection property.</summary>
public enum BodyProjection
{
    /// <summary>The body is projected above the board.</summary>
    Top,

    /// <summary>The body is projected below the board.</summary>
    Bottom,
}

/// <summary>How the 3D body is represented by Altium.</summary>
public enum ModelRepresentation
{
    /// <summary>A generic embedded body.</summary>
    GenericEmbedded,

    /// <summary>A native/extruded Altium body; not supported by the first placement version.</summary>
    Native,

    /// <summary>The representation is not known.</summary>
    Unknown,
}

/// <summary>Where the 3D model asset originated.</summary>
public enum ModelSourceOrigin
{
    /// <summary>The model is embedded in the PCB document.</summary>
    Embedded,

    /// <summary>The model is an external file.</summary>
    External,

    /// <summary>The origin is not known.</summary>
    Unknown,
}

/// <summary>Describes whether <see cref="ModelStateInput.Rz"/> already contains component rotation.</summary>
public enum ModelStateSource
{
    /// <summary>The state comes from the footprint and does not contain component rotation.</summary>
    SourceFootprint,

    /// <summary>The state was read from a placed body and may contain component rotation.</summary>
    PlacedBodyEffective,
}

/// <summary>The result category returned by <see cref="PlacementTransformBuilder"/>.</summary>
public enum PlacementCapability
{
    /// <summary>The requested combination is supported.</summary>
    Supported,

    /// <summary>The requested combination is explicitly unsupported.</summary>
    Unsupported,
}

/// <summary>Machine-readable reason for an unsupported placement request.</summary>
public enum PlacementDiagnosticCode
{
    /// <summary>The request is malformed or contains non-finite values.</summary>
    InvalidRequest,

    /// <summary>A required footprint origin calibration is missing.</summary>
    MissingFootprintCalibration,

    /// <summary>The combination of side, projection, rotation or model state is not supported.</summary>
    UnsupportedCombination,

    /// <summary>The model representation is not supported.</summary>
    UnsupportedModelRepresentation,

    /// <summary>The model source is not supported.</summary>
    UnsupportedModelSource,

    /// <summary>The resulting matrix is not a finite rigid transform.</summary>
    DegenerateTransform,
}

/// <summary>A placement diagnostic with a stable code and a human-readable message.</summary>
public sealed record PlacementDiagnostic(PlacementDiagnosticCode Code, string Message);

/// <summary>An explicit model state; angles are in degrees and <see cref="DzMm"/> is in millimetres.</summary>
public readonly struct ModelStateInput : IEquatable<ModelStateInput>
{
    /// <summary>Creates a model state and rejects non-finite values.</summary>
    public ModelStateInput(double rx, double ry, double rz, double dzMm)
    {
        if (!double.IsFinite(rx) || !double.IsFinite(ry) || !double.IsFinite(rz) || !double.IsFinite(dzMm))
            throw new ArgumentOutOfRangeException(nameof(rx), "Model state values must be finite.");

        Rx = rx;
        Ry = ry;
        Rz = rz;
        DzMm = dzMm;
    }

    /// <summary>Rotation about X in degrees.</summary>
    public double Rx { get; }

    /// <summary>Rotation about Y in degrees.</summary>
    public double Ry { get; }

    /// <summary>Rotation about Z in degrees.</summary>
    public double Rz { get; }

    /// <summary>Model Z offset in millimetres.</summary>
    public double DzMm { get; }

    /// <summary>Returns the zero state.</summary>
    public static ModelStateInput Zero => new(0, 0, 0, 0);

    /// <summary>Returns true when all four values are within the supplied tolerance of zero.</summary>
    public bool IsZero(double tolerance = 1e-9) =>
        Math.Abs(Rx) <= tolerance &&
        Math.Abs(Ry) <= tolerance &&
        Math.Abs(Rz) <= tolerance &&
        Math.Abs(DzMm) <= tolerance;

    /// <summary>Compares all state values with the supplied angular and linear tolerances.</summary>
    public bool NearlyEquals(ModelStateInput other, double angleTolerance = 1e-9, double lengthTolerance = 1e-9) =>
        Math.Abs(Rx - other.Rx) <= angleTolerance &&
        Math.Abs(Ry - other.Ry) <= angleTolerance &&
        Math.Abs(Rz - other.Rz) <= angleTolerance &&
        Math.Abs(DzMm - other.DzMm) <= lengthTolerance;

    /// <inheritdoc />
    public bool Equals(ModelStateInput other) =>
        Rx.Equals(other.Rx) && Ry.Equals(other.Ry) && Rz.Equals(other.Rz) && DzMm.Equals(other.DzMm);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ModelStateInput other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Rx, Ry, Rz, DzMm);

    /// <inheritdoc />
    public override string ToString() => $"rx={Rx}, ry={Ry}, rz={Rz}, dz={DzMm} mm";
}

/// <summary>
/// Explicit top and bottom board surface Z coordinates in the board frame. The surfaces are
/// supplied independently; they are never derived from an assumed board thickness.
/// </summary>
public sealed record BoardSurfaceFrame(double TopSurfaceZMm, double BottomSurfaceZMm);

/// <summary>
/// Measured footprint model-origin offsets used to place a 3D body.
/// The bottom local offset is expressed in the local bottom frame and converted to the board
/// frame as <c>(X, -Y)</c>.
/// </summary>
public sealed class FootprintOriginCalibration
{
    /// <summary>Creates calibration data from optional measured offsets.</summary>
    public FootprintOriginCalibration(Vector2Mm? topModelOriginOffsetBoardMm, Vector2Mm? bottomModelOriginOffsetLocalMm)
    {
        TopModelOriginOffsetBoardMm = topModelOriginOffsetBoardMm;
        BottomModelOriginOffsetLocalMm = bottomModelOriginOffsetLocalMm;
    }

    /// <summary>Top footprint model-origin offset in board coordinates, or null if not measured.</summary>
    public Vector2Mm? TopModelOriginOffsetBoardMm { get; }

    /// <summary>Bottom footprint model-origin offset in the local bottom frame, or null if not measured.</summary>
    public Vector2Mm? BottomModelOriginOffsetLocalMm { get; }

    /// <summary>Bottom model-origin offset converted to board coordinates.</summary>
    public Vector2Mm? BottomModelOriginOffsetBoardMm =>
        BottomModelOriginOffsetLocalMm is { } local ? new Vector2Mm(local.X, -local.Y) : null;

    /// <summary>The measured bottom rotation pivot offset relative to the component position.</summary>
    public Vector2Mm? BottomPivotOffsetMm =>
        BottomModelOriginOffsetBoardMm is { } bottom && TopModelOriginOffsetBoardMm is { } top
            ? bottom - top
            : null;
}

/// <summary>An immutable, fully explicit request to build one body-to-board placement transform.</summary>
public sealed record PlacementTransformRequest
{
    /// <summary>Component position in Altium board coordinates, millimetres.</summary>
    public required Vector2Mm ComponentPositionBoardMm { get; init; }

    /// <summary>Component rotation in degrees.</summary>
    public required double ComponentRotationDeg { get; init; }

    /// <summary>Component side.</summary>
    public required ComponentSide ComponentSide { get; init; }

    /// <summary>3D body projection side.</summary>
    public required BodyProjection BodyProjection { get; init; }

    /// <summary>Measured footprint origin calibration; null if no calibration is available.</summary>
    public FootprintOriginCalibration? FootprintCalibration { get; init; }

    /// <summary>Asset origin in the model's local coordinates, millimetres.</summary>
    public required Vector3Mm AssetOriginLocalMm { get; init; }

    /// <summary>Explicit model Euler state and Z offset.</summary>
    public required ModelStateInput ModelState { get; init; }

    /// <summary>Whether <see cref="ModelState"/> comes from the footprint or a placed body.</summary>
    public required ModelStateSource ModelStateSource { get; init; }

    /// <summary>Explicit top and bottom board surface Z coordinates.</summary>
    public required BoardSurfaceFrame BoardSurfaceFrame { get; init; }

    /// <summary>Body standoff from the projection surface, millimetres.</summary>
    public required double StandoffHeightMm { get; init; }

    /// <summary>Altium model representation.</summary>
    public required ModelRepresentation ModelRepresentation { get; init; }

    /// <summary>Origin of the model asset.</summary>
    public required ModelSourceOrigin ModelSourceOrigin { get; init; }
}

/// <summary>The outcome of a placement request. Unsupported modes never return a transform.</summary>
public sealed class PlacementTransformResult
{
    private PlacementTransformResult(
        PlacementCapability capability,
        Matrix4x4D? transform,
        ModelStateInput normalizedModelState,
        IReadOnlyList<PlacementDiagnostic> diagnostics)
    {
        Capability = capability;
        Transform = transform;
        NormalizedModelState = normalizedModelState;
        Diagnostics = diagnostics;
    }

    /// <summary>Supported or unsupported.</summary>
    public PlacementCapability Capability { get; }

    /// <summary>The rigid body-to-board transform, or null when unsupported.</summary>
    public Matrix4x4D? Transform { get; }

    /// <summary>The model state after component-rotation normalization.</summary>
    public ModelStateInput NormalizedModelState { get; }

    /// <summary>Diagnostics explaining an unsupported request.</summary>
    public IReadOnlyList<PlacementDiagnostic> Diagnostics { get; }

    /// <summary>True when a transform was produced.</summary>
    public bool IsSupported => Capability == PlacementCapability.Supported;

    /// <summary>Creates a supported result.</summary>
    public static PlacementTransformResult Supported(Matrix4x4D transform, ModelStateInput normalizedModelState) =>
        new(PlacementCapability.Supported, transform, normalizedModelState, Array.Empty<PlacementDiagnostic>());

    /// <summary>Creates an unsupported result with one or more diagnostics.</summary>
    public static PlacementTransformResult Unsupported(
        ModelStateInput normalizedModelState,
        params PlacementDiagnostic[] diagnostics) =>
        new(PlacementCapability.Unsupported, null, normalizedModelState, diagnostics);
}
