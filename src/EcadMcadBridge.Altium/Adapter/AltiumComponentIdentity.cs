using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>Fields used to match a PCB component object to a snapshot component.</summary>
internal sealed record AltiumComponentKey(
    string? UniqueId,
    string? SourceUniqueId,
    string Designator,
    int XIu,
    int YIu,
    double RotationDeg,
    BoardSide Side);

/// <summary>Snapshot-backed identity of one Altium component.</summary>
internal sealed record AltiumComponentIdentity(string ComponentId, AltiumComponentKey Key);

internal enum AltiumComponentResolutionStatus
{
    Resolved,
    Unknown,
    Ambiguous,
}

internal enum AltiumComponentResolutionMethod
{
    None,
    UniqueId,
    SourceUniqueId,
    Fallback,
}

internal sealed record AltiumComponentResolution(
    AltiumComponentResolutionStatus Status,
    string? ComponentId,
    AltiumComponentResolutionMethod Method);
