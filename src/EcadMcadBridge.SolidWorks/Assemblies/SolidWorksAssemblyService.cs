using System.Globalization;
using EcadMcadBridge.SolidWorks.Importing;
using EcadMcadBridge.SolidWorks.Interop;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace EcadMcadBridge.SolidWorks.Assemblies;

public sealed record SolidWorksAssemblyResult(
    string AssemblyPath,
    int ComponentCount,
    int ReopenedComponentCount,
    bool Saved,
    bool Reopened,
    int Errors,
    int Warnings,
    IReadOnlyList<SolidWorksDiagnostic> Diagnostics,
    SolidWorksDocumentLifecycle Lifecycle);

/// <summary>
/// Minimal API-level assembly smoke: insert already-created native parts at 0,0,0.
/// This is not ECAD placement. Only documents newly opened by this operation are closed.
/// </summary>
public sealed class SolidWorksAssemblyService
{
    private readonly ISldWorks _solidWorks;

    public SolidWorksAssemblyService(ISldWorks solidWorks)
    {
        _solidWorks = solidWorks ?? throw new ArgumentNullException(nameof(solidWorks));
    }

    public SolidWorksAssemblyResult Create(
        string templatePath,
        string assemblyPath,
        IReadOnlyList<string> partPaths)
    {
        var diagnostics = new List<SolidWorksDiagnostic>();
        var tracker = SolidWorksDocumentTracker.Capture(_solidWorks);
        if (partPaths.Count < 2)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-ASM-001",
                SolidWorksDiagnosticSeverity.Error,
                "assembly",
                "At least two native parts are required for the assembly smoke."));
            return Failure(assemblyPath, 0, 0, false, false, 0, 0, diagnostics, tracker);
        }

        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-ASM-002",
                SolidWorksDiagnosticSeverity.Error,
                "template",
                "Assembly template is missing."));
            return Failure(assemblyPath, 0, 0, false, false, 0, 0, diagnostics, tracker);
        }

        if (!SolidWorksFileGuard.TryValidateNewTarget(assemblyPath, _solidWorks, out var targetReason))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-ASM-014",
                SolidWorksDiagnosticSeverity.Error,
                "assembly",
                targetReason));
            return Failure(assemblyPath, 0, 0, false, false, 0, 0, diagnostics, tracker);
        }

        ModelDoc2? document = null;
        var temporaryParts = new List<string>();
        try
        {
            document = _solidWorks.NewDocument(templatePath, 0, 0, 0) as ModelDoc2;
            if (document is null || document.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-003",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "NewDocument did not create an assembly."));
                return Failure(assemblyPath, 0, 0, false, false, 0, 0, diagnostics, tracker);
            }

            tracker.Track(document);
            var title = document.GetTitle();
            var activateErrors = 0;
            var active = _solidWorks.ActivateDoc3(title, false, 0, ref activateErrors) as ModelDoc2;
            if (active is null || activateErrors != 0)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-009",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "The new assembly could not be activated."));
                return Failure(assemblyPath, 0, 0, false, false, 0, 0, diagnostics, tracker);
            }

            var verifyDirectory = Path.Combine(
                Path.GetDirectoryName(assemblyPath)!,
                ".bridge-verify");
            Directory.CreateDirectory(verifyDirectory);
            var openParts = new List<string>();
            foreach (var partPath in partPaths.Take(2))
            {
                // SOLIDWORKS rejects a second document with the same basename, so the
                // assembly smoke loads byte-identical unique copies of the native parts.
                var verifyPartPath = Path.Combine(
                    verifyDirectory,
                    Guid.NewGuid().ToString("N") + ".SLDPRT");
                File.Copy(partPath, verifyPartPath, overwrite: false);
                temporaryParts.Add(verifyPartPath);

                var partOpenErrors = 0;
                var partOpenWarnings = 0;
                var part = _solidWorks.OpenDoc6(
                    verifyPartPath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    string.Empty,
                    ref partOpenErrors,
                    ref partOpenWarnings);
                AddLoadWarnings(diagnostics, partOpenWarnings);
                if (part is null || partOpenErrors != 0)
                {
                    diagnostics.Add(new SolidWorksDiagnostic(
                        "SW-ASM-011",
                        SolidWorksDiagnosticSeverity.Error,
                        "assembly",
                        "A native part could not be loaded into memory."));
                    continue;
                }

                tracker.Track(part, partOpenWarnings);
                openParts.Add(verifyPartPath);
            }

            if (openParts.Count < 2)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-012",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "Fewer than two native parts could be loaded."));
                return Failure(assemblyPath, 0, 0, false, false, 0, 0, diagnostics, tracker);
            }

            activateErrors = 0;
            _solidWorks.ActivateDoc3(title, false, 0, ref activateErrors);

            var assembly = document as AssemblyDoc;
            if (assembly is null)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-004",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "AssemblyDoc is unavailable."));
                return Failure(assemblyPath, 0, 0, false, false, 0, 0, diagnostics, tracker);
            }

            foreach (var partPath in openParts)
            {
                var component = assembly.AddComponent5(
                    partPath,
                    (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                    string.Empty,
                    false,
                    string.Empty,
                    0,
                    0,
                    0);
                if (component is null)
                {
                    diagnostics.Add(new SolidWorksDiagnostic(
                        "SW-ASM-005",
                        SolidWorksDiagnosticSeverity.Error,
                        "assembly",
                        "AddComponent5 returned null for a native part (" +
                        Path.GetFileName(partPath) + ")."));
                }
            }

            var components = CountComponents(assembly);
            if (components < 2)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-013",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "Assembly contains fewer than two components."));
                return Failure(assemblyPath, components, 0, false, false, 0, 0, diagnostics, tracker);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
            var extension = document.Extension;
            if (extension is null)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-017",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "ModelDoc2.Extension is unavailable."));
                return Failure(assemblyPath, components, 0, false, false, 0, 0, diagnostics, tracker);
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
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-006",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "Saving the test assembly failed (returned=" +
                    saved.ToString(CultureInfo.InvariantCulture) +
                    ", errors=" + saveErrors.ToString(CultureInfo.InvariantCulture) + ")."));
                return Failure(
                    assemblyPath,
                    components,
                    0,
                    false,
                    false,
                    saveErrors,
                    saveWarnings,
                    diagnostics,
                    tracker);
            }

            AddCleanupWarnings(diagnostics, tracker.CloseCreated(document));

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
            var alreadyOpen = (openWarnings & (int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen) != 0;
            if (alreadyOpen)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-007",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "The saved assembly was already open during reopen verification."));
                return Failure(
                    assemblyPath,
                    components,
                    0,
                    true,
                    false,
                    saveErrors,
                    saveWarnings + openWarnings,
                    diagnostics,
                    tracker);
            }

            if (reopened is null || openErrors != 0)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-007",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "Reopening the test assembly failed."));
                return Failure(
                    assemblyPath,
                    components,
                    0,
                    true,
                    false,
                    saveErrors,
                    saveWarnings + openWarnings,
                    diagnostics,
                    tracker);
            }

            tracker.Track(reopened, openWarnings);
            var reopenedComponents = reopened is AssemblyDoc reopenedAssembly
                ? CountComponents(reopenedAssembly)
                : 0;
            if (reopenedComponents != components)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-016",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "Reopened assembly has a different component count."));
            }

            AddCleanupWarnings(diagnostics, tracker.CloseCreated(reopened));
            return new SolidWorksAssemblyResult(
                assemblyPath,
                components,
                reopenedComponents,
                true,
                reopenedComponents == components,
                saveErrors,
                saveWarnings + openWarnings,
                diagnostics,
                tracker.Snapshot());
        }
        catch (Exception ex)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-ASM-008",
                SolidWorksDiagnosticSeverity.Error,
                "assembly",
                "SOLIDWORKS assembly smoke failed: " + ex.GetType().Name + "."));
            return Failure(assemblyPath, 0, 0, false, false, 0, 0, diagnostics, tracker);
        }
        finally
        {
            AddCleanupWarnings(diagnostics, tracker.CloseCreated());
            foreach (var path in temporaryParts)
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                    // Temporary assembly inputs live inside the Bridge output directory.
                }
            }
        }
    }

    private SolidWorksAssemblyResult Failure(
        string assemblyPath,
        int componentCount,
        int reopenedComponentCount,
        bool saved,
        bool reopened,
        int errors,
        int warnings,
        IReadOnlyList<SolidWorksDiagnostic> diagnostics,
        SolidWorksDocumentTracker tracker)
    {
        var cleanup = tracker.CloseCreated();
        if (diagnostics is List<SolidWorksDiagnostic> list)
            AddCleanupWarnings(list, cleanup);
        return new SolidWorksAssemblyResult(
            assemblyPath,
            componentCount,
            reopenedComponentCount,
            saved,
            reopened,
            errors,
            warnings,
            diagnostics,
            tracker.Snapshot());
    }

    private static int CountComponents(AssemblyDoc assembly)
    {
        var components = assembly.GetComponents(false) as object[];
        return components?.Length ?? 0;
    }

    private static void AddSaveWarnings(List<SolidWorksDiagnostic> diagnostics, int warnings)
    {
        foreach (var flag in SolidWorksWarningDecoder.DecodeSaveWarnings(warnings))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-ASM-015",
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
                "SW-ASM-016",
                SolidWorksDiagnosticSeverity.Warning,
                "assembly",
                "Open warning " + flag.Name + " (" +
                flag.Value.ToString(CultureInfo.InvariantCulture) + ")."));
        }
    }

    private static void AddCleanupWarnings(
        List<SolidWorksDiagnostic> diagnostics,
        IReadOnlyList<string> warnings)
    {
        foreach (var warning in warnings)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-ASM-CLEANUP",
                SolidWorksDiagnosticSeverity.Warning,
                "documents",
                warning));
        }
    }
}
