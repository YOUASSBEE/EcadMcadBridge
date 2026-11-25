namespace EcadMcadBridge.Altium.Adapter;

/// <summary>Converts an identity resolution into a body binding or a fatal inventory error.</summary>
internal static class AltiumComponentResolutionGuard
{
    public static string RequireComponentId(AltiumComponentResolution resolution)
    {
        if (resolution.Status != AltiumComponentResolutionStatus.Resolved
            || string.IsNullOrWhiteSpace(resolution.ComponentId))
        {
            throw new AltiumModelInventoryException(
                "A component body owner could not be resolved to one snapshot component " +
                "(" + resolution.Status + ").");
        }

        return resolution.ComponentId;
    }
}
