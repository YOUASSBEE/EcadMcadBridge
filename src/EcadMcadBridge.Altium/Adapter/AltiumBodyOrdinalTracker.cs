namespace EcadMcadBridge.Altium.Adapter;

/// <summary>Assigns a local, zero-based body ordinal per component in enumeration order.</summary>
internal sealed class AltiumBodyOrdinalTracker
{
    private readonly Dictionary<string, int> _next = new(StringComparer.Ordinal);

    public int Next(string componentId)
    {
        _next.TryGetValue(componentId, out var next);
        _next[componentId] = next + 1;
        return next;
    }
}
