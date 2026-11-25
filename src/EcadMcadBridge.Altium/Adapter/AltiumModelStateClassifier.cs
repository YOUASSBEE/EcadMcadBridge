namespace EcadMcadBridge.Altium.Adapter;

internal enum AltiumModelStateStatus
{
    Clean,
    Empty,
    Stale,
    Unknown,
}

/// <summary>Tri-state model emptiness/staleness classification; errors never mean "clean".</summary>
internal static class AltiumModelStateClassifier
{
    public static AltiumModelStateStatus Classify(bool? isEmpty, bool? isStale)
    {
        if (isStale == true)
            return AltiumModelStateStatus.Stale;
        if (isEmpty == true)
            return AltiumModelStateStatus.Empty;
        if (isEmpty is null || isStale is null)
            return AltiumModelStateStatus.Unknown;
        return AltiumModelStateStatus.Clean;
    }
}
