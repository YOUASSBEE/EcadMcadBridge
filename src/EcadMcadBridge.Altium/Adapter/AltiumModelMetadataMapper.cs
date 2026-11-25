using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>Converts raw SDK values into the Core catalog metadata without interpreting transforms.</summary>
internal static class AltiumModelMetadataMapper
{
    public static ModelOrigin MapOrigin(int? xIu, int? yIu) => new()
    {
        XIu = xIu,
        XMm = xIu.HasValue ? AltiumUnits.IuToMm(xIu.Value) : null,
        YIu = yIu,
        YMm = yIu.HasValue ? AltiumUnits.IuToMm(yIu.Value) : null,
        ZIu = null,
        ZMm = null,
    };

    public static ModelState MapState(
        double? rotationXDegRaw,
        double? rotationYDegRaw,
        double? rotationZDegRaw,
        int? dzIu) => new()
    {
        RotationXDegRaw = rotationXDegRaw,
        RotationYDegRaw = rotationYDegRaw,
        RotationZDegRaw = rotationZDegRaw,
        DzIu = dzIu,
        DzMm = dzIu.HasValue ? AltiumUnits.IuToMm(dzIu.Value) : null,
    };

    public static double? MapRotation(double rotation) =>
        double.IsFinite(rotation) ? rotation : null;

    public static ComponentBodyProjection MapBodyProjection(PCB.TBoardSide side) => side switch
    {
        PCB.TBoardSide.eBoardSide_Top => ComponentBodyProjection.Top,
        PCB.TBoardSide.eBoardSide_Bottom => ComponentBodyProjection.Bottom,
        _ => ComponentBodyProjection.Unknown,
    };

    public static LengthValue MapLength(int? iu) => LengthValue.FromIu(iu);

    public static ComponentModelStateSource MapStateSource(
        ComponentModelRepresentation representation,
        ComponentModelSourceOrigin sourceOrigin) =>
        representation == ComponentModelRepresentation.Generic
        && sourceOrigin == ComponentModelSourceOrigin.Embedded
            ? ComponentModelStateSource.PlacedBodyEffective
            : ComponentModelStateSource.Unknown;
}
