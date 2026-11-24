namespace EcadMcadBridge.Core.Validation;

/// <summary>Stable diagnostic codes for the neutral board model.</summary>
public static class BoardDiagnosticCodes
{
    public const string MissingId = "EMB-VAL-001";
    public const string DuplicateId = "EMB-VAL-002";
    public const string InvalidReference = "EMB-VAL-003";
    public const string MissingRequiredAttribute = "EMB-VAL-004";
    public const string InconsistentUnitPair = "EMB-VAL-005";
    public const string NonFiniteNumber = "EMB-VAL-006";
    public const string NegativeDimension = "EMB-VAL-007";
    public const string InvalidArc = "EMB-VAL-008";
    public const string InvalidContour = "EMB-VAL-009";
    public const string InvalidPolygon = "EMB-VAL-010";
    public const string InvalidGeometry = "EMB-VAL-011";

    public const string MissingOptionalData = "EMB-VAL-101";
    public const string EmptyCollection = "EMB-VAL-102";
}
