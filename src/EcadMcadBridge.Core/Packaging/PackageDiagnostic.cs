namespace EcadMcadBridge.Core.Packaging;

public enum PackageDiagnosticSeverity
{
    Error,
    Warning,
}

/// <summary>Structured package diagnostic. Codes are stable and UI-independent.</summary>
public sealed record PackageDiagnostic(
    string Code,
    PackageDiagnosticSeverity Severity,
    string Path,
    string Message);

public static class PackageDiagnosticCodes
{
    public const string MissingPackage = "EMB-PKG-001";
    public const string MissingManifest = "EMB-PKG-002";
    public const string InvalidManifest = "EMB-PKG-003";
    public const string UnsupportedSchema = "EMB-PKG-004";
    public const string UnsupportedSchemaVersion = "EMB-PKG-005";
    public const string InvalidUnits = "EMB-PKG-006";
    public const string InvalidPath = "EMB-PKG-007";
    public const string DuplicatePath = "EMB-PKG-008";
    public const string MissingChecksums = "EMB-PKG-009";
    public const string InvalidChecksumFormat = "EMB-PKG-010";
    public const string ChecksumMismatch = "EMB-PKG-011";
    public const string MissingFile = "EMB-PKG-012";
    public const string SizeMismatch = "EMB-PKG-013";
    public const string CorruptSnapshot = "EMB-PKG-014";
    public const string SnapshotValidation = "EMB-PKG-015";
    public const string ExistingPackage = "EMB-PKG-016";
    public const string WriteFailed = "EMB-PKG-017";
    public const string InvalidMetadata = "EMB-PKG-018";
    public const string ExtraFile = "EMB-PKG-019";
    public const string UnsupportedManifestField = "EMB-PKG-020";
    public const string UnsupportedLink = "EMB-PKG-021";
    public const string InvalidModelCatalog = "EMB-PKG-022";
    public const string UnsupportedModelSchema = "EMB-PKG-023";
    public const string InvalidModelReference = "EMB-PKG-024";
    public const string InvalidModelAsset = "EMB-PKG-025";
    public const string ModelCatalogCapabilityMismatch = "EMB-PKG-026";
    public const string InvalidBoardModel = "EMB-PKG-027";
    public const string UnsupportedBoardModelSchema = "EMB-PKG-028";
    public const string BoardModelCapabilityMismatch = "EMB-PKG-029";
    public const string InvalidBoardModelAsset = "EMB-PKG-030";
}
