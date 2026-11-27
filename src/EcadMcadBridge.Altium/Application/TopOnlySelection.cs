using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;

namespace EcadMcadBridge.Altium.Application;

internal sealed record TopOnlySelectionCandidate(
    string Reference,
    AltiumComponentKey Key,
    bool OwnershipAvailable,
    bool BodyInventoryReadable,
    bool BodyTopOnly,
    bool ModelAvailable);

internal sealed record TopOnlyExportEntry(string Reference, string? ComponentId, BoardSide? Side);

internal sealed record TopOnlySkipEntry(string Reference, string Reason);

internal sealed record TopOnlyFilterResult(
    bool Allowed,
    string? Error,
    AltiumBoardSnapshotResult? Snapshot,
    IReadOnlyList<TopOnlyExportEntry> Selected,
    IReadOnlyList<TopOnlyExportEntry> Exported,
    IReadOnlyList<TopOnlySkipEntry> Skipped);

internal delegate TopOnlyFilterResult ExportTopOnlyFilter(AltiumBoardSnapshotResult snapshot);

/// <summary>Pure Top-only selection policy; no CAD objects cross this boundary.</summary>
internal static class TopOnlySelectionFilter
{
    public static TopOnlyFilterResult Apply(
        AltiumBoardSnapshotResult source,
        IReadOnlyList<TopOnlySelectionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidates);

        var selected = new List<TopOnlyExportEntry>(candidates.Count);
        var exported = new List<TopOnlyExportEntry>();
        var skipped = new List<TopOnlySkipEntry>();
        var allowed = new HashSet<string>(StringComparer.Ordinal);

        if (source.ComponentIdentities is null || source.Snapshot is null)
            return Reject("Component ownership data is unavailable.", selected, skipped);

        var resolver = AltiumComponentIdentityResolver.Build(source.ComponentIdentities);
        foreach (var candidate in candidates)
        {
            if (!candidate.OwnershipAvailable)
            {
                Skip(candidate.Reference, "Component ownership could not be read.", selected, skipped);
                continue;
            }

            var resolution = resolver.Resolve(candidate.Key);
            if (resolution.Status != AltiumComponentResolutionStatus.Resolved
                || string.IsNullOrWhiteSpace(resolution.ComponentId))
            {
                Skip(candidate.Reference, "Component ownership could not be proven.", selected, skipped);
                continue;
            }

            var component = source.Snapshot.Components.FirstOrDefault(
                item => string.Equals(item.Id, resolution.ComponentId, StringComparison.Ordinal));
            var entry = new TopOnlyExportEntry(candidate.Reference, resolution.ComponentId, component?.Placement.Side);
            selected.Add(entry);

            if (component is null)
            {
                skipped.Add(new TopOnlySkipEntry(candidate.Reference, "Component is absent from the board snapshot."));
                continue;
            }

            if (component.Placement.Side != BoardSide.Top)
            {
                skipped.Add(new TopOnlySkipEntry(candidate.Reference, "Only Top component side is supported."));
                continue;
            }

            if (!candidate.BodyInventoryReadable)
            {
                skipped.Add(new TopOnlySkipEntry(candidate.Reference, "Component body ownership could not be verified."));
                continue;
            }

            if (!candidate.BodyTopOnly)
            {
                skipped.Add(new TopOnlySkipEntry(candidate.Reference, "Only Top body projection is supported."));
                continue;
            }

            if (!candidate.ModelAvailable)
            {
                skipped.Add(new TopOnlySkipEntry(candidate.Reference, "The selected component has no usable 3D model."));
                continue;
            }

            exported.Add(entry);
            allowed.Add(component.Id);
        }

        if (allowed.Count == 0)
            return Reject("No selected component satisfied the Top-only contract.", selected, skipped);

        var filtered = TopOnlySnapshotBuilder.Filter(source, allowed);
        return new TopOnlyFilterResult(true, null, filtered, selected, exported, skipped);
    }

    private static void Skip(
        string reference,
        string reason,
        List<TopOnlyExportEntry> selected,
        List<TopOnlySkipEntry> skipped)
    {
        selected.Add(new TopOnlyExportEntry(reference, null, null));
        skipped.Add(new TopOnlySkipEntry(reference, reason));
    }

    private static TopOnlyFilterResult Reject(
        string error,
        IReadOnlyList<TopOnlyExportEntry> selected,
        IReadOnlyList<TopOnlySkipEntry> skipped) =>
        new(false, error, null, selected, Array.Empty<TopOnlyExportEntry>(), skipped);
}

/// <summary>Read-back assertion: package contents must match the approved Top-only selection.</summary>
internal static class TopOnlyPackageVerifier
{
    public static string? Verify(PackageReadResult read, BoardSnapshot expected)
    {
        if (read.Snapshot is null || read.Models is null)
            return "The verified package has no snapshot or component model catalog.";

        var expectedIds = expected.Components
            .Select(component => component.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var actualIds = read.Snapshot.Components
            .Select(component => component.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (!expectedIds.SequenceEqual(actualIds, StringComparer.Ordinal))
            return "Verified package components differ from the approved Top-only selection.";

        if (actualIds.Any(id =>
                read.Snapshot.Components.First(component => component.Id == id).Placement.Side != BoardSide.Top))
            return "Verified package contains a component that is not on Top.";

        var expectedModelIds = expectedIds;
        var actualModelIds = read.Models.Components
            .Select(component => component.ComponentId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (!expectedModelIds.SequenceEqual(actualModelIds, StringComparer.Ordinal))
            return "Verified package catalog differs from the approved Top-only selection.";

        if (read.Models.Components.SelectMany(component => component.Bodies)
            .Any(body => body.BodyProjection != ComponentBodyProjection.Top))
            return "Verified package contains a body that is not on Top.";

        return null;
    }
}

internal static class TopOnlySnapshotBuilder
{
    public static AltiumBoardSnapshotResult Filter(
        AltiumBoardSnapshotResult source,
        IReadOnlySet<string> allowed)
    {
        var snapshot = source.Snapshot;
        var components = snapshot.Components
            .Where(component => allowed.Contains(component.Id))
            .ToArray();
        var componentIds = components.Select(component => component.Id).ToHashSet(StringComparer.Ordinal);
        var filtered = new BoardSnapshot
        {
            Source = snapshot.Source,
            SourceUnits = snapshot.SourceUnits,
            Units = snapshot.Units,
            Properties = snapshot.Properties,
            Outline = snapshot.Outline,
            Layers = snapshot.Layers,
            Components = components,
            PadStacks = snapshot.PadStacks,
            Pads = snapshot.Pads
                .Where(pad => string.IsNullOrEmpty(pad.ComponentId) || componentIds.Contains(pad.ComponentId))
                .ToArray(),
            Nets = snapshot.Nets,
            Tracks = snapshot.Tracks,
            Vias = snapshot.Vias,
            Regions = snapshot.Regions,
            Texts = snapshot.Texts
                .Where(text => text.ComponentId is null || componentIds.Contains(text.ComponentId))
                .ToArray(),
        };

        return new AltiumBoardSnapshotResult(
            filtered,
            source.Board,
            source.ComponentIdentities,
            source.Warnings,
            source.Regions,
            source.Diagnostics,
            allowed);
    }
}
