namespace EcadMcadBridge.Core.Diagnostics;

/// <summary>
/// Altium IU (coord) conversion. One IU is 0.00000254 mm; the original IU value is
/// always kept next to the millimetre value (D-015).
/// </summary>
public static class AltiumUnits
{
    public const double MillimetersPerIu = 0.00000254;

    public static double IuToMm(int iu) => iu * MillimetersPerIu;

    public static int MmToIu(double mm) =>
        checked((int)Math.Round(mm / MillimetersPerIu, MidpointRounding.AwayFromZero));
}
