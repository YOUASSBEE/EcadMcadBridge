namespace EcadMcadBridge.Altium.Adapter;

/// <summary>
/// Builds a deterministic, unique snapshot ComponentId while preserving raw identity keys
/// for the model resolver. A repeated raw key is disambiguated by component index but
/// remains visible to the resolver as a duplicate source key.
/// </summary>
internal static class AltiumComponentIdBuilder
{
    public static string? NormalizeKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    public static string Build(
        string? uniqueId,
        string? sourceUniqueId,
        string designator,
        int index,
        HashSet<string> usedIds,
        List<string> warnings,
        AltiumAdapterDiagnostics? diagnostics = null)
    {
        var uniqueKey = NormalizeKey(uniqueId);
        var sourceKey = NormalizeKey(sourceUniqueId);
        var fallback = sourceKey ?? designator + ":" + index;
        var candidate = AltiumBoardSnapshotAdapter.MakeId(
            "component",
            uniqueKey,
            fallback,
            warnings,
            diagnostics);
        return AltiumBoardSnapshotAdapter.EnsureUniqueId(candidate, usedIds, index);
    }
}
