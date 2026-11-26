using System.Globalization;
using EcadMcadBridge.Core.Placement;
using EcadMcadBridge.SolidWorks.Importing;

namespace EcadMcadBridge.SolidWorks.Placement;

/// <summary>The board occurrence transform and the measured values that produced it.</summary>
public sealed record BoardPlacementTransformResult(
    Matrix4x4D? Transform,
    double TranslationXMm,
    double TranslationYMm,
    double TranslationZMm,
    double ExpectedTopSurfaceZMm,
    double ResultingTopSurfaceZMm,
    IReadOnlyList<SolidWorksDiagnostic> Diagnostics)
{
    public bool IsSupported =>
        Transform is not null
        && Diagnostics.All(item => item.Severity != SolidWorksDiagnosticSeverity.Error);
}

/// <summary>
/// Aligns the imported board frame with the board-local frame used by component placement:
/// XY origin at the Outer-contour minimum, and the measured top surface at
/// <c>BoardSnapshot.Thickness / 2</c>. The measured board thickness is never changed.
/// </summary>
public static class BoardPlacementTransformBuilder
{
    public static BoardPlacementTransformResult Build(
        double nominalThicknessMm,
        SolidWorksBoardGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var diagnostics = new List<SolidWorksDiagnostic>();
        if (!double.IsFinite(nominalThicknessMm) || nominalThicknessMm <= 0)
        {
            diagnostics.Add(Error(
                "SW-BRD-020",
                "board.thickness",
                "A finite positive nominal board thickness is required."));
            return Unsupported(nominalThicknessMm, geometry, diagnostics);
        }

        if (!IsFinite(geometry.MinX) || !IsFinite(geometry.MinY)
            || !IsFinite(geometry.MaxZ) || !double.IsFinite(geometry.ThicknessMm)
            || geometry.ThicknessMm <= 0)
        {
            diagnostics.Add(Error(
                "SW-BRD-021",
                "board",
                "Measured board bounds are not finite."));
            return Unsupported(nominalThicknessMm, geometry, diagnostics);
        }

        var expectedTop = nominalThicknessMm / 2.0;
        var tx = -geometry.MinX;
        var ty = -geometry.MinY;
        var tz = expectedTop - geometry.MaxZ;
        var transform = Matrix4x4D.CreateTranslation(tx, ty, tz);
        var resultingTop = geometry.MaxZ + tz;
        if (Math.Abs(resultingTop - expectedTop) > 1e-9)
        {
            diagnostics.Add(Error(
                "SW-BRD-022",
                "board",
                "The board top surface could not be aligned: residual=" +
                (resultingTop - expectedTop).ToString("R", CultureInfo.InvariantCulture) + " mm."));
            return Unsupported(nominalThicknessMm, geometry, diagnostics);
        }

        return new BoardPlacementTransformResult(
            transform,
            tx,
            ty,
            tz,
            expectedTop,
            resultingTop,
            diagnostics);
    }

    private static BoardPlacementTransformResult Unsupported(
        double nominalThicknessMm,
        SolidWorksBoardGeometry geometry,
        IReadOnlyList<SolidWorksDiagnostic> diagnostics) =>
        new(
            null,
            0,
            0,
            0,
            double.IsFinite(nominalThicknessMm) ? nominalThicknessMm / 2.0 : 0,
            geometry.MaxZ,
            diagnostics);

    private static bool IsFinite(double value) => double.IsFinite(value);

    private static SolidWorksDiagnostic Error(string code, string path, string message) =>
        new(code, SolidWorksDiagnosticSeverity.Error, path, message);
}
