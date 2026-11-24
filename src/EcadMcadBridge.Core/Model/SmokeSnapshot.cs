namespace EcadMcadBridge.Core.Model;

/// <summary>
/// Diagnostic snapshot of the active PCB. Field order is part of the format contract.
/// </summary>
public sealed class SmokeSnapshot
{
    public string Schema { get; init; } = "ecad-mcad-bridge/smoke-snapshot";

    public string SchemaVersion { get; init; } = "0.1.0";

    public string Cad { get; init; } = "altium";

    public string CadVersion { get; init; } = "26";

    public string DocumentFileName { get; init; } = string.Empty;

    public string SourceUnits { get; init; } = "altium-iu";

    public string Units { get; init; } = "mm";

    public SmokeCoordinate SampleCoordinate { get; init; } = new(1_000_000, 2_000_000, 2.54, 5.08);

    public int ComponentCount { get; init; }

    public int OutlinePointCount { get; init; }

    public DateTimeOffset ExportedAtUtc { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
