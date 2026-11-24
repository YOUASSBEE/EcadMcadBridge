namespace EcadMcadBridge.Core.Diagnostics;

/// <summary>Normalizes a rotation to the exchange range [-180, 180) degrees.</summary>
public static class AngleNormalizer
{
    public static double NormalizeDegrees(double degrees)
    {
        if (!double.IsFinite(degrees))
            throw new ArgumentOutOfRangeException(nameof(degrees), "Angle must be finite.");

        var normalized = degrees % 360.0;
        if (normalized < 0.0)
            normalized += 360.0;
        if (normalized >= 180.0)
            normalized -= 360.0;

        return normalized;
    }
}
