using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Core.Packaging;

/// <summary>
/// Typed input for writing a component model catalog. Source paths stay at the writer
/// boundary and are never written into the package.
/// </summary>
public sealed class ComponentModelsWriteRequest
{
    public IReadOnlyList<ComponentModelWriteEntry> Components { get; init; } = Array.Empty<ComponentModelWriteEntry>();

    public IReadOnlyList<ComponentModelAssetSource> Assets { get; init; } = Array.Empty<ComponentModelAssetSource>();
}

/// <summary>Catalog entry supplied by the caller.</summary>
public sealed class ComponentModelWriteEntry
{
    public string ComponentId { get; init; } = string.Empty;

    public string Designator { get; init; } = string.Empty;

    public IReadOnlyList<ComponentBodyWriteEntry> Bodies { get; init; } = Array.Empty<ComponentBodyWriteEntry>();
}

/// <summary>
/// Body metadata supplied by the caller. <see cref="AssetKey"/> refers to
/// <see cref="ComponentModelAssetSource.AssetKey"/>; the writer resolves it to a
/// content-addressed package path.
/// </summary>
public sealed class ComponentBodyWriteEntry
{
    public int BodyOrdinal { get; init; }

    public ComponentModelRepresentation Representation { get; init; } = ComponentModelRepresentation.Unknown;

    public ComponentModelSourceOrigin SourceOrigin { get; init; } = ComponentModelSourceOrigin.Unknown;

    public ComponentModelAssetAcquisition AssetAcquisition { get; init; } = ComponentModelAssetAcquisition.Unknown;

    /// <summary>Key of an input asset, or null when no geometry file is included.</summary>
    public string? AssetKey { get; init; }

    public ModelOrigin Origin { get; init; } = new();

    public ModelState State { get; init; } = new();

    public double? ModelRotationDegRaw { get; init; }

    public ComponentBodyProjection BodyProjection { get; init; } = ComponentBodyProjection.Unknown;

    public LengthValue StandoffHeight { get; init; } = LengthValue.FromIu(null);

    public LengthValue OverallHeight { get; init; } = LengthValue.FromIu(null);

    public ComponentModelStateSource ModelStateSource { get; init; } = ComponentModelStateSource.Unknown;
}

/// <summary>An asset file supplied to the writer. The source path is not persisted.</summary>
public sealed class ComponentModelAssetSource
{
    public string AssetKey { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;

    public ComponentModelAssetFormat Format { get; init; } = ComponentModelAssetFormat.StepAp214;
}
