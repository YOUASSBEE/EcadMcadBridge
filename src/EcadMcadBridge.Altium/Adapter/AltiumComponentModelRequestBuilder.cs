using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>A body inventory row independent of COM types.</summary>
internal sealed record AltiumBodyInventoryEntry(
    string ComponentId,
    int BodyOrdinal,
    ComponentModelRepresentation Representation,
    ComponentModelSourceOrigin SourceOrigin,
    ModelOrigin Origin,
    ModelState State,
    double? ModelRotationDegRaw,
    ComponentModelAssetAcquisition AssetAcquisition,
    string? AssetKey,
    ComponentBodyProjection BodyProjection,
    LengthValue StandoffHeight,
    LengthValue OverallHeight,
    ComponentModelStateSource ModelStateSource);

/// <summary>Builds the Core write request from snapshot components and body inventory.</summary>
internal static class AltiumComponentModelRequestBuilder
{
    public static ComponentModelsWriteRequest Build(
        BoardSnapshot snapshot,
        IReadOnlyList<AltiumBodyInventoryEntry> bodies,
        IReadOnlyList<ComponentModelAssetSource> assets)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(assets);

        var snapshotIds = new HashSet<string>(
            snapshot.Components.Select(component => component.Id),
            StringComparer.Ordinal);
        foreach (var body in bodies)
        {
            if (!snapshotIds.Contains(body.ComponentId))
            {
                throw new InvalidOperationException(
                    "Body inventory references a component that is absent from the snapshot.");
            }
        }

        var grouped = bodies
            .GroupBy(body => body.ComponentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var components = new List<ComponentModelWriteEntry>(snapshot.Components.Count);
        foreach (var component in snapshot.Components)
        {
            if (!grouped.TryGetValue(component.Id, out var componentBodies))
                componentBodies = Array.Empty<AltiumBodyInventoryEntry>();

            var ordinals = new HashSet<int>();
            var entries = new List<ComponentBodyWriteEntry>(componentBodies.Length);
            foreach (var body in componentBodies.OrderBy(item => item.BodyOrdinal))
            {
                if (body.BodyOrdinal < 0 || !ordinals.Add(body.BodyOrdinal))
                {
                    throw new InvalidOperationException(
                        "Body inventory contains a negative or duplicate body ordinal.");
                }

                entries.Add(new ComponentBodyWriteEntry
                {
                    BodyOrdinal = body.BodyOrdinal,
                    Representation = body.Representation,
                    SourceOrigin = body.SourceOrigin,
                    AssetAcquisition = body.AssetAcquisition,
                    AssetKey = body.AssetKey,
                    Origin = body.Origin ?? new ModelOrigin(),
                    State = body.State ?? new ModelState(),
                    ModelRotationDegRaw = body.ModelRotationDegRaw,
                    BodyProjection = body.BodyProjection,
                    StandoffHeight = body.StandoffHeight,
                    OverallHeight = body.OverallHeight,
                    ModelStateSource = body.ModelStateSource,
                });
            }

            components.Add(new ComponentModelWriteEntry
            {
                ComponentId = component.Id,
                Designator = component.Designator,
                Bodies = entries,
            });
        }

        return new ComponentModelsWriteRequest
        {
            Components = components,
            Assets = assets.ToArray(),
        };
    }
}
