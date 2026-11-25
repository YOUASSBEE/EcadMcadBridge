using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>
/// Resolves a PCB component owner to the exact ComponentInstance.Id used by the snapshot.
/// Primary keys are UniqueId and SourceUniqueId; fallback requires an unambiguous match.
/// </summary>
internal sealed class AltiumComponentIdentityResolver
{
    private readonly Dictionary<string, KeyIndexEntry> _byUniqueId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KeyIndexEntry> _bySourceUniqueId = new(StringComparer.Ordinal);
    private readonly Dictionary<FallbackKey, List<string>> _byFallback = new();

    public static AltiumComponentIdentityResolver Build(
        IReadOnlyList<AltiumComponentIdentity> identities)
    {
        var resolver = new AltiumComponentIdentityResolver();
        foreach (var identity in identities)
        {
            if (identity is null)
                continue;

            var key = identity.Key;
            if (!string.IsNullOrWhiteSpace(key.UniqueId))
                AddKey(resolver._byUniqueId, key.UniqueId, identity.ComponentId);
            if (!string.IsNullOrWhiteSpace(key.SourceUniqueId))
                AddKey(resolver._bySourceUniqueId, key.SourceUniqueId, identity.ComponentId);

            var fallback = FallbackKey.From(key);
            if (!resolver._byFallback.TryGetValue(fallback, out var list))
            {
                list = new List<string>();
                resolver._byFallback[fallback] = list;
            }

            if (!list.Contains(identity.ComponentId, StringComparer.Ordinal))
                list.Add(identity.ComponentId);
        }

        return resolver;
    }

    public AltiumComponentResolution Resolve(AltiumComponentKey owner)
    {
        if (TryResolve(_byUniqueId, owner.UniqueId, AltiumComponentResolutionMethod.UniqueId, out var byUnique))
            return byUnique;

        if (TryResolve(_bySourceUniqueId, owner.SourceUniqueId, AltiumComponentResolutionMethod.SourceUniqueId, out var bySource))
            return bySource;

        if (_byFallback.TryGetValue(FallbackKey.From(owner), out var matches))
        {
            return matches.Count switch
            {
                1 => new AltiumComponentResolution(
                    AltiumComponentResolutionStatus.Resolved,
                    matches[0],
                    AltiumComponentResolutionMethod.Fallback),
                > 1 => new AltiumComponentResolution(
                    AltiumComponentResolutionStatus.Ambiguous,
                    null,
                    AltiumComponentResolutionMethod.Fallback),
                _ => new AltiumComponentResolution(
                    AltiumComponentResolutionStatus.Unknown,
                    null,
                    AltiumComponentResolutionMethod.None),
            };
        }

        return new AltiumComponentResolution(
            AltiumComponentResolutionStatus.Unknown,
            null,
            AltiumComponentResolutionMethod.None);
    }

    private static void AddKey(
        Dictionary<string, KeyIndexEntry> index,
        string keyValue,
        string componentId)
    {
        if (!index.TryGetValue(keyValue, out var entry))
        {
            entry = new KeyIndexEntry();
            index[keyValue] = entry;
        }

        entry.Add(componentId);
    }

    private static bool TryResolve(
        Dictionary<string, KeyIndexEntry> index,
        string? keyValue,
        AltiumComponentResolutionMethod method,
        out AltiumComponentResolution resolution)
    {
        if (!string.IsNullOrWhiteSpace(keyValue) && index.TryGetValue(keyValue, out var entry))
        {
            resolution = entry.ToResolution(method);
            return true;
        }

        resolution = default!;
        return false;
    }

    private sealed class KeyIndexEntry
    {
        private string? _componentId;
        private bool _ambiguous;

        public void Add(string componentId)
        {
            if (_componentId is null)
            {
                _componentId = componentId;
                return;
            }

            if (!string.Equals(_componentId, componentId, StringComparison.Ordinal))
                _ambiguous = true;
        }

        public AltiumComponentResolution ToResolution(AltiumComponentResolutionMethod method) =>
            _ambiguous
                ? new AltiumComponentResolution(
                    AltiumComponentResolutionStatus.Ambiguous,
                    null,
                    method)
                : new AltiumComponentResolution(
                    AltiumComponentResolutionStatus.Resolved,
                    _componentId,
                    method);
    }

    private readonly record struct FallbackKey(
        string Designator,
        int XIu,
        int YIu,
        double RotationDeg,
        BoardSide Side)
    {
        public static FallbackKey From(AltiumComponentKey key) =>
            new(key.Designator, key.XIu, key.YIu, key.RotationDeg, key.Side);
    }
}
