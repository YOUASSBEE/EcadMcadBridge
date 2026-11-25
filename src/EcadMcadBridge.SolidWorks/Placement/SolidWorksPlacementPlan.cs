using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Placement;
using EcadMcadBridge.SolidWorks.Importing;

namespace EcadMcadBridge.SolidWorks.Placement;

/// <summary>One supported body reference with its calculated board-to-model transform.</summary>
public sealed record SolidWorksPlacementRequest(
    string ComponentId,
    string Designator,
    int BodyOrdinal,
    string AssetPath,
    string NativePartPath,
    Matrix4x4D Transform,
    ModelStateInput ModelState,
    string ModelStateSource,
    ComponentBodyProjection BodyProjection,
    BoardSide ComponentSide,
    double StandoffHeightMm,
    double? OverallHeightMm,
    double ModelRotationDegRaw,
    Vector2Mm ModelOriginBoardMm,
    Vector2Mm LocalModelOriginOffsetMm,
    Vector2Mm BoardOriginMm);

/// <summary>Deterministic placement inventory plus explicit diagnostics for every skipped body.</summary>
public sealed record SolidWorksPlacementPlan(
    IReadOnlyList<SolidWorksPlacementRequest> Requests,
    IReadOnlyList<SolidWorksDiagnostic> Diagnostics,
    int SupportedBodyReferences,
    int UnsupportedBodyReferences,
    IReadOnlyList<string> RequestedDesignators,
    IReadOnlyList<string> MissingDesignators,
    Vector2Mm BoardOriginMm)
{
    public bool IsValid =>
        Requests.Count > 0
        && MissingDesignators.Count == 0
        && Diagnostics.All(item => item.Severity != SolidWorksDiagnosticSeverity.Error);

    public IReadOnlyList<SolidWorksDiagnostic> Errors =>
        Diagnostics.Where(item => item.Severity == SolidWorksDiagnosticSeverity.Error).ToArray();

    public IReadOnlyList<string> SelectedDesignators =>
        Requests.Select(item => item.Designator).Distinct(StringComparer.Ordinal).ToArray();
}
