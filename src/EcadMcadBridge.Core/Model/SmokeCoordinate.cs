namespace EcadMcadBridge.Core.Model;

/// <summary>A coordinate pair in the original Altium IU and in millimetres.</summary>
public sealed record SmokeCoordinate(int XIu, int YIu, double XMm, double YMm);
