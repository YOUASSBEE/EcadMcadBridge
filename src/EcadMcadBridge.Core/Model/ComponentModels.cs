namespace EcadMcadBridge.Core.Model;

/// <summary>How a component body was represented by the source CAD system.</summary>
public enum ComponentModelRepresentation
{
    Unknown,
    Generic,
    Native,
}

/// <summary>Where the geometry of a component body originated.</summary>
public enum ComponentModelSourceOrigin
{
    Unknown,
    Embedded,
    Native,
    Linked,
}

/// <summary>The board side selected by the 3D body projection property.</summary>
public enum ComponentBodyProjection
{
    Unknown,
    Top,
    Bottom,
}

/// <summary>Whether the stored model state already contains component rotation.</summary>
public enum ComponentModelStateSource
{
    Unknown,
    SourceFootprint,
    PlacedBodyEffective,
}

/// <summary>How an asset file in the package was obtained.</summary>
public enum ComponentModelAssetAcquisition
{
    Unknown,
    SdkExport,
    SourceCopyVerified,
    NotAcquired,
}

/// <summary>Serialized geometry format. The enum value is not the file extension.</summary>
public enum ComponentModelAssetFormat
{
    StepAp214,
    ParasolidXT,
}

/// <summary>
/// Versioned catalog of component 3D bodies. It describes metadata and asset references only;
/// it never asserts a complete board-to-model transform matrix.
/// </summary>
public sealed class ComponentModelCatalog
{
    public const string SchemaName = "ecad-mcad-bridge/component-models";

    public const string SchemaVersionValue = "1.1.0";

    public const string FileRelativePath = "models/models.json";

    public const string AssetDirectory = "assets/3d";

    public const string Capability = "component-3d-models";

    public string Schema { get; init; } = SchemaName;

    public string SchemaVersion { get; init; } = SchemaVersionValue;

    public IReadOnlyList<ComponentModelEntry> Components { get; init; } = Array.Empty<ComponentModelEntry>();
}

/// <summary>Catalog entry for one snapshot component; keyed by <c>ComponentInstance.Id</c>.</summary>
public sealed class ComponentModelEntry
{
    public string ComponentId { get; init; } = string.Empty;

    public string Designator { get; init; } = string.Empty;

    public IReadOnlyList<ComponentBodyEntry> Bodies { get; init; } = Array.Empty<ComponentBodyEntry>();
}

/// <summary>One component body entry. Ordinal is local to the component.</summary>
public sealed class ComponentBodyEntry
{
    public int BodyOrdinal { get; init; }

    public ComponentModelRepresentation Representation { get; init; } = ComponentModelRepresentation.Unknown;

    public ComponentModelSourceOrigin SourceOrigin { get; init; } = ComponentModelSourceOrigin.Unknown;

    public ComponentModelAssetAcquisition AssetAcquisition { get; init; } = ComponentModelAssetAcquisition.Unknown;

    public ComponentModelAssetFormat? AssetFormat { get; init; }

    public string? AssetPath { get; init; }

    public ModelOrigin Origin { get; init; } = new();

    public ModelState State { get; init; } = new();

    /// <summary>Raw model rotation from the API; null when unavailable. Not a board transform.</summary>
    public double? ModelRotationDegRaw { get; init; }

    /// <summary>Projection side of the 3D body; Unknown when the SDK value is unavailable.</summary>
    public ComponentBodyProjection BodyProjection { get; init; } = ComponentBodyProjection.Unknown;

    /// <summary>Body standoff height. Both IU and millimetres stay null when unavailable.</summary>
    public LengthValue StandoffHeight { get; init; } = LengthValue.FromIu(null);

    /// <summary>Overall body height. Both IU and millimetres stay null when unavailable.</summary>
    public LengthValue OverallHeight { get; init; } = LengthValue.FromIu(null);

    /// <summary>Whether <see cref="ModelState"/> already contains component rotation.</summary>
    public ComponentModelStateSource ModelStateSource { get; init; } = ComponentModelStateSource.Unknown;
}

/// <summary>Raw source-model origin. Each axis is an IU/mm pair or both null.</summary>
public sealed class ModelOrigin
{
    public int? XIu { get; init; }

    public double? XMm { get; init; }

    public int? YIu { get; init; }

    public double? YMm { get; init; }

    public int? ZIu { get; init; }

    public double? ZMm { get; init; }
}

/// <summary>
/// Raw state values from the API. Angles are preserved as reported and are not normalized;
/// the order and axis direction are not interpreted here.
/// </summary>
public sealed class ModelState
{
    public double? RotationXDegRaw { get; init; }

    public double? RotationYDegRaw { get; init; }

    public double? RotationZDegRaw { get; init; }

    public int? DzIu { get; init; }

    public double? DzMm { get; init; }
}
