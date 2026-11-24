namespace EcadMcadBridge.Core.Validation;

public sealed class BoardValidationResult
{
    public BoardValidationResult(IReadOnlyList<BoardDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        Errors = Diagnostics.Where(item => item.Severity == BoardDiagnosticSeverity.Error).ToArray();
        Warnings = Diagnostics.Where(item => item.Severity == BoardDiagnosticSeverity.Warning).ToArray();
    }

    public IReadOnlyList<BoardDiagnostic> Diagnostics { get; }

    public IReadOnlyList<BoardDiagnostic> Errors { get; }

    public IReadOnlyList<BoardDiagnostic> Warnings { get; }

    /// <summary>False when at least one error makes the snapshot unsuitable for exchange.</summary>
    public bool IsValid => Errors.Count == 0;
}
