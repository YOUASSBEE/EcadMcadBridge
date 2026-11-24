namespace EcadMcadBridge.Core.Validation;

public enum BoardDiagnosticSeverity
{
    Error,
    Warning,
}

/// <summary>Structured validator message. Codes are stable and UI-independent.</summary>
public sealed record BoardDiagnostic(
    string Code,
    BoardDiagnosticSeverity Severity,
    string Path,
    string Message);
