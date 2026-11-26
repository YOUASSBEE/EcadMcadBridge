using System.Globalization;
using EcadMcadBridge.Core.Placement;
using EcadMcadBridge.SolidWorks.Importing;
using EcadMcadBridge.SolidWorks.Interop;
using EcadMcadBridge.SolidWorks.Placement;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace EcadMcadBridge.SolidWorks.Assemblies;

/// <summary>One body reference to place in the assembly.</summary>
public sealed record SolidWorksPlacedOccurrence(
    string ComponentId,
    string Designator,
    int BodyOrdinal,
    string NativePartPath,
    Matrix4x4D RequestedTransform);

/// <summary>The board substrate occurrence and its alignment transform.</summary>
public sealed record SolidWorksPlacedBoardOccurrence(
    string NativePartPath,
    Matrix4x4D RequestedTransform);

/// <summary>Verification for the board occurrence after transform assignment, fix and reopen.</summary>
public sealed record SolidWorksPlacedBoardResult(
    string NativePartPath,
    string OccurrenceName,
    Matrix4x4D RequestedTransform,
    Matrix4x4D ReadbackTransform,
    double TranslationResidualMm,
    double RotationBasisResidual,
    double Determinant,
    bool Fixed,
    bool Verified);

/// <summary>Verification for one requested occurrence after transform assignment and reopen.</summary>
public sealed record SolidWorksPlacedOccurrenceResult(
    string ComponentId,
    string Designator,
    int BodyOrdinal,
    string NativePartPath,
    string OccurrenceName,
    Matrix4x4D RequestedTransform,
    Matrix4x4D ReadbackTransform,
    double TranslationResidualMm,
    double RotationBasisResidual,
    double Determinant,
    bool Verified);

public sealed record SolidWorksPlacedAssemblyResult(
    string AssemblyPath,
    int RequestedOccurrences,
    int AddedOccurrences,
    int ReopenedOccurrences,
    bool Saved,
    bool Reopened,
    bool TransformsVerified,
    IReadOnlyList<SolidWorksPlacedOccurrenceResult> Occurrences,
    IReadOnlyList<SolidWorksDiagnostic> Diagnostics,
    SolidWorksDocumentLifecycle Lifecycle)
{
    /// <summary>Board occurrence result; null when no board part was requested.</summary>
    public SolidWorksPlacedBoardResult? BoardOccurrence { get; init; }
}

/// <summary>
/// Creates a native SLDASM with one occurrence per body reference and explicitly sets each
/// rigid transform. Only documents tracked as Bridge-owned are closed.
/// </summary>
public sealed class SolidWorksPlacedAssemblyService
{
    private const double TranslationToleranceMm = 0.001;
    private const double RotationTolerance = 1e-8;
    private readonly ISldWorks _solidWorks;

    public SolidWorksPlacedAssemblyService(ISldWorks solidWorks)
    {
        _solidWorks = solidWorks ?? throw new ArgumentNullException(nameof(solidWorks));
    }

    public SolidWorksPlacedAssemblyResult Create(
        string templatePath,
        string assemblyPath,
        IReadOnlyList<SolidWorksPlacedOccurrence> occurrences) =>
        Create(templatePath, assemblyPath, occurrences, board: null);

    public SolidWorksPlacedAssemblyResult Create(
        string templatePath,
        string assemblyPath,
        IReadOnlyList<SolidWorksPlacedOccurrence> occurrences,
        SolidWorksPlacedBoardOccurrence? board)
    {
        var diagnostics = new List<SolidWorksDiagnostic>();
        var tracker = SolidWorksDocumentTracker.Capture(_solidWorks);
        var verified = new List<SolidWorksPlacedOccurrenceResult>();

        if (occurrences.Count == 0 && board is null)
        {
            return Failure(
                assemblyPath,
                0,
                0,
                0,
                false,
                false,
                false,
                verified,
                diagnostics,
                tracker,
                "SW-PLC-ASM-001",
                "At least one placement occurrence is required.");
        }

        var duplicate = occurrences
            .GroupBy(item => item.ComponentId + "\u001f" + item.BodyOrdinal.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return Failure(
                assemblyPath,
                occurrences.Count,
                0,
                0,
                false,
                false,
                false,
                verified,
                diagnostics,
                tracker,
                "SW-PLC-ASM-002",
                "Duplicate component/body ordinal in the placement inventory.");
        }

        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
        {
            return Failure(
                assemblyPath,
                occurrences.Count,
                0,
                0,
                false,
                false,
                false,
                verified,
                diagnostics,
                tracker,
                "SW-PLC-ASM-003",
                "Assembly template is missing.");
        }

        if (!SolidWorksFileGuard.TryValidateNewTarget(assemblyPath, _solidWorks, out var targetReason))
        {
            return Failure(
                assemblyPath,
                occurrences.Count,
                0,
                0,
                false,
                false,
                false,
                verified,
                diagnostics,
                tracker,
                "SW-PLC-ASM-004",
                targetReason);
        }

        ModelDoc2? document = null;
        try
        {
            document = _solidWorks.NewDocument(templatePath, 0, 0, 0) as ModelDoc2;
            if (document is null || document.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                return Failure(
                    assemblyPath,
                    occurrences.Count,
                    0,
                    0,
                    false,
                    false,
                    false,
                    verified,
                    diagnostics,
                    tracker,
                    "SW-PLC-ASM-005",
                    "NewDocument did not create an assembly.");
            }

            tracker.Track(document);
            var title = document.GetTitle();
            var activateErrors = 0;
            if (_solidWorks.ActivateDoc3(title, false, 0, ref activateErrors) is null || activateErrors != 0)
            {
                return Failure(
                    assemblyPath,
                    occurrences.Count,
                    0,
                    0,
                    false,
                    false,
                    false,
                    verified,
                    diagnostics,
                    tracker,
                    "SW-PLC-ASM-006",
                    "The new assembly could not be activated.");
            }

            var assembly = document as AssemblyDoc;
            if (assembly is null)
            {
                return Failure(
                    assemblyPath,
                    occurrences.Count,
                    0,
                    0,
                    false,
                    false,
                    false,
                    verified,
                    diagnostics,
                    tracker,
                    "SW-PLC-ASM-007",
                    "AssemblyDoc is unavailable.");
            }

            if (!LoadUniqueParts(occurrences, board, tracker, diagnostics))
            {
                return Failure(
                    assemblyPath,
                    occurrences.Count,
                    0,
                    0,
                    false,
                    false,
                    false,
                    verified,
                    diagnostics,
                    tracker,
                    "SW-PLC-ASM-008",
                    "At least one native SLDPRT could not be loaded.");
            }

            activateErrors = 0;
            _solidWorks.ActivateDoc3(title, false, 0, ref activateErrors);

            var added = 0;
            foreach (var occurrence in occurrences)
            {
                var component = assembly.AddComponent5(
                    occurrence.NativePartPath,
                    (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                    string.Empty,
                    false,
                    string.Empty,
                    0,
                    0,
                    0);
                if (component is null)
                {
                    diagnostics.Add(Error(
                        "SW-PLC-ASM-009",
                        occurrence.Designator,
                        "AddComponent5 returned null for " + Path.GetFileName(occurrence.NativePartPath) + "."));
                    continue;
                }

                added++;
                TrackComponent(component, tracker, diagnostics);
                var result = ApplyTransform(component, occurrence, diagnostics);
                if (result is not null)
                    verified.Add(result);
            }

            SolidWorksPlacedBoardResult? boardResult = null;
            if (board is not null)
            {
                var boardComponent = assembly.AddComponent5(
                    board.NativePartPath,
                    (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                    string.Empty,
                    false,
                    string.Empty,
                    0,
                    0,
                    0);
                if (boardComponent is null)
                {
                    diagnostics.Add(Error(
                        "SW-PLC-ASM-036",
                        "BOARD",
                        "AddComponent5 returned null for " + Path.GetFileName(board.NativePartPath) + "."));
                }
                else
                {
                    added++;
                    TrackComponent(boardComponent, tracker, diagnostics);
                    boardResult = ApplyBoardOccurrence(boardComponent, board, assembly, diagnostics);
                }
            }

            var expectedAdded = occurrences.Count + (board is null ? 0 : 1);
            if (added != expectedAdded)
            {
                diagnostics.Add(Error(
                    "SW-PLC-ASM-010",
                    "assembly",
                    "Only " + added.ToString(CultureInfo.InvariantCulture) + " of " +
                    expectedAdded.ToString(CultureInfo.InvariantCulture) + " occurrences were added."));
            }

            var assembleVerified = verified.Count == occurrences.Count &&
                                   verified.All(item => item.Verified);
            if (!assembleVerified)
            {
                diagnostics.Add(Error(
                    "SW-PLC-ASM-011",
                    "assembly",
                    "Not every requested occurrence was added and verified."));
            }

            var boardAssembled = board is null || boardResult is { Verified: true, Fixed: true };
            if (!boardAssembled)
            {
                diagnostics.Add(Error(
                    "SW-PLC-ASM-037",
                    "BOARD",
                    "The board occurrence was not added, aligned and fixed."));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
            var extension = document.Extension;
            if (extension is null)
            {
                return Failure(
                    assemblyPath,
                    occurrences.Count,
                    added,
                    0,
                    false,
                    false,
                    false,
                    verified,
                    diagnostics,
                    tracker,
                    "SW-PLC-ASM-012",
                    "ModelDoc2.Extension is unavailable.");
            }

            var saveErrors = 0;
            var saveWarnings = 0;
            var saved = extension.SaveAs3(
                assemblyPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                null,
                ref saveErrors,
                ref saveWarnings);
            AddSaveWarnings(diagnostics, saveWarnings);
            if (!SolidWorksFileGuard.IsSaveSuccessful(saved, saveErrors, assemblyPath))
            {
                return Failure(
                    assemblyPath,
                    occurrences.Count,
                    added,
                    0,
                    false,
                    false,
                    false,
                    verified,
                    diagnostics,
                    tracker,
                    "SW-PLC-ASM-013",
                    "Saving the placed assembly failed (errors=" +
                    saveErrors.ToString(CultureInfo.InvariantCulture) + ").");
            }

            CloseAndReport(diagnostics, tracker, document);

            var openErrors = 0;
            var openWarnings = 0;
            var reopened = _solidWorks.OpenDoc6(
                assemblyPath,
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                string.Empty,
                ref openErrors,
                ref openWarnings);
            AddLoadWarnings(diagnostics, openWarnings);
            if ((openWarnings & (int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen) != 0)
            {
                return Failure(
                    assemblyPath,
                    occurrences.Count,
                    added,
                    0,
                    true,
                    false,
                    false,
                    verified,
                    diagnostics,
                    tracker,
                    "SW-PLC-ASM-014",
                    "The saved assembly was already open during reopen verification.");
            }

            if (reopened is null || openErrors != 0)
            {
                return Failure(
                    assemblyPath,
                    occurrences.Count,
                    added,
                    0,
                    true,
                    false,
                    false,
                    verified,
                    diagnostics,
                    tracker,
                    "SW-PLC-ASM-015",
                    "Reopening the placed assembly failed.");
            }

            tracker.Track(reopened, openWarnings);
            var reopenedAssembly = reopened as AssemblyDoc;
            if (reopenedAssembly is null)
            {
                return Failure(
                    assemblyPath,
                    occurrences.Count,
                    added,
                    0,
                    true,
                    false,
                    false,
                    verified,
                    diagnostics,
                    tracker,
                    "SW-PLC-ASM-016",
                    "Reopened document is not an AssemblyDoc.");
            }

            var reopenedCount = CountComponents(reopenedAssembly);
            TrackComponents(reopenedAssembly, tracker, diagnostics);
            var allOccurrences = board is null
                ? occurrences.ToArray()
                : occurrences.Concat(new[]
                {
                    new SolidWorksPlacedOccurrence(
                        "__board__",
                        "BOARD",
                        -1,
                        board.NativePartPath,
                        board.RequestedTransform),
                }).ToArray();
            var reopenedVerified = VerifyReopened(
                reopenedAssembly,
                allOccurrences,
                diagnostics);
            if (board is not null)
            {
                boardResult = ReadBoardOccurrence(reopenedAssembly, board, diagnostics) ?? boardResult;
            }

            var boardReopened = board is null || boardResult is { Fixed: true, Verified: true };
            var expectedOccurrences = occurrences.Count + (board is null ? 0 : 1);
            CloseAndReport(diagnostics, tracker, reopened);
            var success = saved
                && reopenedCount == expectedOccurrences
                && assembleVerified
                && boardAssembled
                && reopenedVerified
                && boardReopened
                && diagnostics.All(item => item.Severity != SolidWorksDiagnosticSeverity.Error)
                && tracker.CleanupStatus == SolidWorksCleanupStatus.Verified;

            return new SolidWorksPlacedAssemblyResult(
                assemblyPath,
                expectedOccurrences,
                added,
                reopenedCount,
                true,
                true,
                success,
                verified,
                diagnostics,
                tracker.Snapshot())
            {
                BoardOccurrence = boardResult,
            };
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error(
                "SW-PLC-ASM-017",
                "assembly",
                "Placed assembly failed: " + ex.GetType().Name + "."));
            return Failure(
                assemblyPath,
                occurrences.Count,
                0,
                0,
                false,
                false,
                false,
                verified,
                diagnostics,
                tracker,
                "SW-PLC-ASM-017",
                "Placed assembly failed: " + ex.GetType().Name + ".");
        }
        finally
        {
            CloseAndReport(diagnostics, tracker);
        }
    }

    private bool LoadUniqueParts(
        IReadOnlyList<SolidWorksPlacedOccurrence> occurrences,
        SolidWorksPlacedBoardOccurrence? board,
        SolidWorksDocumentTracker tracker,
        List<SolidWorksDiagnostic> diagnostics)
    {
        var paths = occurrences
            .Select(item => item.NativePartPath)
            .Concat(board is null ? Array.Empty<string>() : new[] { board.NativePartPath })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                diagnostics.Add(Error("SW-PLC-ASM-018", path, "Native SLDPRT is missing."));
                continue;
            }

            var errors = 0;
            var warnings = 0;
            var part = _solidWorks.OpenDoc6(
                path,
                (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                string.Empty,
                ref errors,
                ref warnings);
            AddLoadWarnings(diagnostics, warnings);
            if (part is null || errors != 0)
            {
                diagnostics.Add(Error(
                    "SW-PLC-ASM-019",
                    path,
                    "Native SLDPRT could not be opened (errors=" + errors.ToString(CultureInfo.InvariantCulture) + ")."));
                continue;
            }

            tracker.Track(part, warnings);
            loaded.Add(path);
        }

        return loaded.Count == paths.Length;
    }

    private SolidWorksPlacedOccurrenceResult? ApplyTransform(
        Component2 component,
        SolidWorksPlacedOccurrence occurrence,
        List<SolidWorksDiagnostic> diagnostics)
    {
        var mathUtility = _solidWorks.GetMathUtility() as IMathUtility;
        if (mathUtility is null)
        {
            diagnostics.Add(Error("SW-PLC-ASM-020", occurrence.Designator, "IMathUtility is unavailable."));
            return null;
        }

        if (!SolidWorksTransformConverter.TryToArrayData(
                occurrence.RequestedTransform,
                out var arrayData,
                out var conversionError))
        {
            diagnostics.Add(Error("SW-PLC-ASM-021", occurrence.Designator, conversionError));
            return null;
        }

        var mathTransform = mathUtility.CreateTransform(arrayData) as MathTransform;
        if (mathTransform is null)
        {
            diagnostics.Add(Error("SW-PLC-ASM-022", occurrence.Designator, "CreateTransform returned null."));
            return null;
        }

        var applied = component.SetTransformAndSolve3(mathTransform, true);
        if (!applied)
        {
            diagnostics.Add(Error("SW-PLC-ASM-023", occurrence.Designator, "SetTransformAndSolve3 returned false."));
            return null;
        }

        var readbackTransform = component.GetTotalTransform(false);
        if (readbackTransform is null
            || readbackTransform.ArrayData is not double[] readbackData)
        {
            diagnostics.Add(Error("SW-PLC-ASM-024", occurrence.Designator, "Transform readback is unavailable."));
            return null;
        }

        if (!SolidWorksTransformConverter.TryFromArrayData(
                readbackData,
                out var readback,
                out var readbackError))
        {
            diagnostics.Add(Error("SW-PLC-ASM-025", occurrence.Designator, readbackError));
            return null;
        }

        var residual = SolidWorksTransformConverter.Measure(occurrence.RequestedTransform, readback);
        var verified = residual.TranslationMm <= TranslationToleranceMm
            && residual.RotationBasisMax <= RotationTolerance
            && Math.Abs(residual.Determinant - 1.0) <= RotationTolerance;
        if (!verified)
        {
            diagnostics.Add(Error(
                "SW-PLC-ASM-026",
                occurrence.Designator,
                "Transform readback exceeds tolerance (translation=" +
                residual.TranslationMm.ToString("R", CultureInfo.InvariantCulture) +
                " mm, rotation=" +
                residual.RotationBasisMax.ToString("R", CultureInfo.InvariantCulture) + ")."));
        }

        return new SolidWorksPlacedOccurrenceResult(
            occurrence.ComponentId,
            occurrence.Designator,
            occurrence.BodyOrdinal,
            occurrence.NativePartPath,
            component.Name2 ?? component.Name ?? string.Empty,
            occurrence.RequestedTransform,
            readback,
            residual.TranslationMm,
            residual.RotationBasisMax,
            residual.Determinant,
            verified);
    }

    private SolidWorksPlacedBoardResult? ApplyBoardOccurrence(
        Component2 component,
        SolidWorksPlacedBoardOccurrence board,
        AssemblyDoc assembly,
        List<SolidWorksDiagnostic> diagnostics)
    {
        var occurrence = new SolidWorksPlacedOccurrence(
            "__board__",
            "BOARD",
            -1,
            board.NativePartPath,
            board.RequestedTransform);
        var applied = ApplyTransform(component, occurrence, diagnostics);
        if (applied is null)
            return null;

        var fixedOk = TryFix(assembly, component, diagnostics);
        return new SolidWorksPlacedBoardResult(
            board.NativePartPath,
            applied.OccurrenceName,
            applied.RequestedTransform,
            applied.ReadbackTransform,
            applied.TranslationResidualMm,
            applied.RotationBasisResidual,
            applied.Determinant,
            fixedOk,
            applied.Verified && fixedOk);
    }

    private static bool TryFix(
        AssemblyDoc assembly,
        Component2 component,
        List<SolidWorksDiagnostic> diagnostics)
    {
        try
        {
            if (component.IsFixed())
                return true;

            if (!component.Select4(false, null, false))
            {
                diagnostics.Add(Error("SW-PLC-ASM-038", "BOARD", "The board component could not be selected for fixing."));
                return false;
            }

            assembly.FixComponent();
            component.DeSelect();
            if (!component.IsFixed())
            {
                diagnostics.Add(Error("SW-PLC-ASM-038", "BOARD", "The board component is not fixed after FixComponent."));
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error(
                "SW-PLC-ASM-038",
                "BOARD",
                "Fixing the board component failed: " + ex.GetType().Name + "."));
            return false;
        }
    }

    private SolidWorksPlacedBoardResult? ReadBoardOccurrence(
        AssemblyDoc assembly,
        SolidWorksPlacedBoardOccurrence board,
        List<SolidWorksDiagnostic> diagnostics)
    {
        var path = SolidWorksPath.Normalize(board.NativePartPath);
        var components = assembly.GetComponents(false) as object[] ?? Array.Empty<object>();
        Component2? match = null;
        foreach (var item in components)
        {
            if (item is Component2 component
                && string.Equals(
                    SolidWorksPath.Normalize(component.GetPathName()),
                    path,
                    StringComparison.OrdinalIgnoreCase))
            {
                match = component;
                break;
            }
        }

        if (match is null)
        {
            diagnostics.Add(Error("SW-PLC-ASM-039", "BOARD", "The board occurrence is missing after reopen."));
            return null;
        }

        var transform = match.GetTotalTransform(false);
        var readbackError = "unknown";
        if (transform?.ArrayData is not double[] data
            || !SolidWorksTransformConverter.TryFromArrayData(data, out var readback, out readbackError))
        {
            diagnostics.Add(Error(
                "SW-PLC-ASM-039",
                "BOARD",
                "The board occurrence transform could not be read: " + readbackError));
            return null;
        }

        var residual = SolidWorksTransformConverter.Measure(board.RequestedTransform, readback);
        var fixedOk = match.IsFixed();
        var verified = residual.TranslationMm <= TranslationToleranceMm
            && residual.RotationBasisMax <= RotationTolerance
            && Math.Abs(residual.Determinant - 1.0) <= RotationTolerance
            && fixedOk;
        if (!verified)
        {
            diagnostics.Add(Error(
                "SW-PLC-ASM-039",
                "BOARD",
                "Reopened board occurrence is not verified (translation=" +
                residual.TranslationMm.ToString("R", CultureInfo.InvariantCulture) +
                " mm, rotation=" + residual.RotationBasisMax.ToString("R", CultureInfo.InvariantCulture) +
                ", fixed=" + fixedOk.ToString() + ")."));
        }

        return new SolidWorksPlacedBoardResult(
            board.NativePartPath,
            match.Name2 ?? match.Name ?? "BOARD",
            board.RequestedTransform,
            readback,
            residual.TranslationMm,
            residual.RotationBasisMax,
            residual.Determinant,
            fixedOk,
            verified);
    }

    private bool VerifyReopened(
        AssemblyDoc assembly,
        IReadOnlyList<SolidWorksPlacedOccurrence> occurrences,
        List<SolidWorksDiagnostic> diagnostics)
    {
        var components = assembly.GetComponents(false) as object[] ?? Array.Empty<object>();
        if (components.Length != occurrences.Count)
        {
            diagnostics.Add(Error(
                "SW-PLC-ASM-027",
                "assembly",
                "Reopened occurrence count is " + components.Length.ToString(CultureInfo.InvariantCulture) +
                "; expected " + occurrences.Count.ToString(CultureInfo.InvariantCulture) + "."));
            return false;
        }

        var unmatched = occurrences.ToList();
        var allVerified = true;
        foreach (var item in components)
        {
            if (item is not Component2 component)
            {
                allVerified = false;
                continue;
            }

            var path = SolidWorksPath.Normalize(component.GetPathName());
            var transform = component.GetTotalTransform(false);
            var readbackError = "unknown";
            if (path is null
                || transform?.ArrayData is not double[] data
                || !SolidWorksTransformConverter.TryFromArrayData(data, out var readback, out readbackError))
            {
                diagnostics.Add(Error(
                    "SW-PLC-ASM-028",
                    component.Name2 ?? "occurrence",
                    "Occurrence transform or path could not be read: " + readbackError));
                allVerified = false;
                continue;
            }

            var candidates = unmatched
                .Select((occurrence, index) => new
                {
                    Occurrence = occurrence,
                    Index = index,
                    Residual = SolidWorksTransformConverter.Measure(occurrence.RequestedTransform, readback),
                })
                .Where(candidate => string.Equals(
                    SolidWorksPath.Normalize(candidate.Occurrence.NativePartPath),
                    path,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.Residual.TranslationMm)
                .ToArray();
            if (candidates.Length == 0)
            {
                diagnostics.Add(Error(
                    "SW-PLC-ASM-029",
                    path,
                    "Reopened occurrence does not match any requested native part."));
                allVerified = false;
                continue;
            }

            var match = candidates[0];
            unmatched.RemoveAt(match.Index);
            var verified = match.Residual.TranslationMm <= TranslationToleranceMm
                && match.Residual.RotationBasisMax <= RotationTolerance
                && Math.Abs(match.Residual.Determinant - 1.0) <= RotationTolerance;
            if (!verified)
            {
                diagnostics.Add(Error(
                    "SW-PLC-ASM-030",
                    match.Occurrence.Designator,
                    "Reopened transform exceeds tolerance (translation=" +
                    match.Residual.TranslationMm.ToString("R", CultureInfo.InvariantCulture) +
                    " mm, rotation=" +
                    match.Residual.RotationBasisMax.ToString("R", CultureInfo.InvariantCulture) + ")."));
            }

            allVerified &= verified;
        }

        if (unmatched.Count > 0)
        {
            diagnostics.Add(Error(
                "SW-PLC-ASM-031",
                "assembly",
                unmatched.Count.ToString(CultureInfo.InvariantCulture) +
                " requested occurrence(s) were not found after reopen."));
            allVerified = false;
        }

        return allVerified;
    }

    private static void TrackComponent(
        Component2 component,
        SolidWorksDocumentTracker tracker,
        List<SolidWorksDiagnostic> diagnostics)
    {
        try
        {
            if (component.GetModelDoc2() is ModelDoc2 model)
                tracker.Track(model);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-PLC-ASM-032",
                SolidWorksDiagnosticSeverity.Warning,
                "assembly",
                "Occurrence document could not be tracked: " + ex.GetType().Name + "."));
        }
    }

    private static void TrackComponents(
        AssemblyDoc assembly,
        SolidWorksDocumentTracker tracker,
        List<SolidWorksDiagnostic> diagnostics)
    {
        try
        {
            if (assembly.GetComponents(false) is not object[] components)
                return;
            foreach (var item in components)
            {
                if (item is Component2 component)
                    TrackComponent(component, tracker, diagnostics);
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-PLC-ASM-033",
                SolidWorksDiagnosticSeverity.Warning,
                "assembly",
                "Assembly documents could not be tracked: " + ex.GetType().Name + "."));
        }
    }

    private static int CountComponents(AssemblyDoc assembly) =>
        (assembly.GetComponents(false) as object[])?.Length ?? 0;

    private SolidWorksPlacedAssemblyResult Failure(
        string assemblyPath,
        int requested,
        int added,
        int reopened,
        bool saved,
        bool reopenedFlag,
        bool verified,
        IReadOnlyList<SolidWorksPlacedOccurrenceResult> occurrences,
        IReadOnlyList<SolidWorksDiagnostic> diagnostics,
        SolidWorksDocumentTracker tracker,
        string code,
        string message)
    {
        if (diagnostics is List<SolidWorksDiagnostic> list)
        {
            if (!list.Any(item => item.Code == code))
                list.Add(Error(code, "assembly", message));
            CloseAndReport(list, tracker);
        }

        return new SolidWorksPlacedAssemblyResult(
            assemblyPath,
            requested,
            added,
            reopened,
            saved,
            reopenedFlag,
            verified,
            occurrences,
            diagnostics,
            tracker.Snapshot());
    }

    private static SolidWorksDiagnostic Error(string code, string path, string message) =>
        new(code, SolidWorksDiagnosticSeverity.Error, path, message);

    private static void AddSaveWarnings(List<SolidWorksDiagnostic> diagnostics, int warnings)
    {
        foreach (var flag in SolidWorksWarningDecoder.DecodeSaveWarnings(warnings))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-PLC-ASM-034",
                SolidWorksDiagnosticSeverity.Warning,
                "assembly",
                "Save warning " + flag.Name + " (" +
                flag.Value.ToString(CultureInfo.InvariantCulture) + ")."));
        }
    }

    private static void AddLoadWarnings(List<SolidWorksDiagnostic> diagnostics, int warnings)
    {
        foreach (var flag in SolidWorksWarningDecoder.DecodeLoadWarnings(warnings))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-PLC-ASM-035",
                SolidWorksDiagnosticSeverity.Warning,
                "assembly",
                "Open warning " + flag.Name + " (" +
                flag.Value.ToString(CultureInfo.InvariantCulture) + ")."));
        }
    }

    private static void CloseAndReport(
        List<SolidWorksDiagnostic> diagnostics,
        SolidWorksDocumentTracker tracker,
        ModelDoc2? preferredFirst = null)
    {
        foreach (var warning in tracker.CloseCreated(preferredFirst))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-PLC-ASM-CLEANUP",
                SolidWorksDiagnosticSeverity.Warning,
                "documents",
                warning));
        }

        if (tracker.CleanupStatus == SolidWorksCleanupStatus.Verified)
            return;
        if (diagnostics.Any(item => item.Code == "SW-PLC-ASM-CLEANUP-STATUS"))
            return;

        diagnostics.Add(Error(
            "SW-PLC-ASM-CLEANUP-STATUS",
            "documents",
            "Document cleanup is " + tracker.CleanupStatus + "; placed assembly cannot be considered successful."));
    }
}
