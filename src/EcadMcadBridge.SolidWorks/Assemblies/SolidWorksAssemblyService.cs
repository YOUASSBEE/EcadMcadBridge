using System.Globalization;
using EcadMcadBridge.SolidWorks.Importing;
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
    IReadOnlyList<SolidWorksDiagnostic> Diagnostics);

/// <summary>
/// Minimal API-level assembly smoke: insert already-created native parts at 0,0,0.
/// This is not ECAD placement.
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
        if (partPaths.Count < 2)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-ASM-001",
                SolidWorksDiagnosticSeverity.Error,
                "assembly",
                "At least two native parts are required for the assembly smoke."));
            return new SolidWorksAssemblyResult(
                assemblyPath,
                0,
                0,
                false,
                false,
                0,
                0,
                diagnostics);
        }

        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-ASM-002",
                SolidWorksDiagnosticSeverity.Error,
                "template",
                "Assembly template is missing."));
            return new SolidWorksAssemblyResult(
                assemblyPath,
                0,
                0,
                false,
                false,
                0,
                0,
                diagnostics);
        }

        ModelDoc2? document = null;
        string? title = null;
        var openedPartTitles = new List<string>();
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
                return BuildFailure(assemblyPath, diagnostics);
            }

            title = document.GetTitle();
            var activateErrors = 0;
            var active = _solidWorks.ActivateDoc3(title, false, 0, ref activateErrors) as ModelDoc2;
            if (active is null || activateErrors != 0)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-009",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "The new assembly could not be activated."));
                return BuildFailure(assemblyPath, diagnostics);
            }

            var openParts = new List<string>();
            foreach (var partPath in partPaths.Take(2))
            {
                var partOpenErrors = 0;
                var partOpenWarnings = 0;
                var part = _solidWorks.OpenDoc6(
                    partPath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    string.Empty,
                    ref partOpenErrors,
                    ref partOpenWarnings);
                if (part is null || partOpenErrors != 0)
                {
                    diagnostics.Add(new SolidWorksDiagnostic(
                        "SW-ASM-011",
                        SolidWorksDiagnosticSeverity.Error,
                        "assembly",
                        "A native part could not be loaded into memory."));
                    continue;
                }

                openParts.Add(partPath);
                var partTitle = part.GetTitle();
                if (!string.IsNullOrWhiteSpace(partTitle) && !openedPartTitles.Contains(partTitle, StringComparer.Ordinal))
                    openedPartTitles.Add(partTitle);
            }

            if (openParts.Count > 0)
            {
                activateErrors = 0;
                _solidWorks.ActivateDoc3(title, false, 0, ref activateErrors);
            }

            if (openParts.Count < 2)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-012",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "Fewer than two native parts could be loaded."));
                return BuildFailure(assemblyPath, diagnostics);
            }

            var assembly = document as AssemblyDoc;
            if (assembly is null)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-004",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "AssemblyDoc is unavailable."));
                return BuildFailure(assemblyPath, diagnostics);
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
                    Path.GetFileName(partPath) + ", components=" +
                    assembly.GetComponentCount(false).ToString(CultureInfo.InvariantCulture) +
                    ", openParts=" + openParts.Count.ToString(CultureInfo.InvariantCulture) +
                    ", activateErrors=" + activateErrors.ToString(CultureInfo.InvariantCulture) + ")."));
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
                return BuildFailure(assemblyPath, diagnostics);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
            var extension = document.Extension;
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
            if (!saved || !File.Exists(assemblyPath) || new FileInfo(assemblyPath).Length <= 0)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-006",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "Saving the test assembly failed."));
                return new SolidWorksAssemblyResult(
                    assemblyPath,
                    components,
                    0,
                    false,
                    false,
                    saveErrors,
                    saveWarnings,
                    diagnostics);
            }

            Close(title);
            title = null;
            foreach (var partTitle in openedPartTitles)
                Close(partTitle);
            openedPartTitles.Clear();

            var openErrors = 0;
            var openWarnings = 0;
            var reopened = _solidWorks.OpenDoc6(
                assemblyPath,
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                string.Empty,
                ref openErrors,
                ref openWarnings);
            if (reopened is null || openErrors != 0)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-ASM-007",
                    SolidWorksDiagnosticSeverity.Error,
                    "assembly",
                    "Reopening the test assembly failed."));
                return new SolidWorksAssemblyResult(
                    assemblyPath,
                    components,
                    0,
                    true,
                    false,
                    saveErrors,
                    saveWarnings + openWarnings,
                    diagnostics);
            }

            var reopenedTitle = reopened.GetTitle();
            var reopenedComponents = reopened is AssemblyDoc reopenedAssembly
                ? CountComponents(reopenedAssembly)
                : 0;
            Close(reopenedTitle);
            return new SolidWorksAssemblyResult(
                assemblyPath,
                components,
                reopenedComponents,
                true,
                reopenedComponents == components,
                saveErrors,
                saveWarnings + openWarnings,
                diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-ASM-008",
                SolidWorksDiagnosticSeverity.Error,
                "assembly",
                "SOLIDWORKS assembly smoke failed: " + ex.GetType().Name + "."));
            return BuildFailure(assemblyPath, diagnostics);
        }
        finally
        {
            if (title is not null)
                Close(title);
            foreach (var partTitle in openedPartTitles)
                Close(partTitle);
        }
    }

    private SolidWorksAssemblyResult BuildFailure(
        string assemblyPath,
        IReadOnlyList<SolidWorksDiagnostic> diagnostics) =>
        new(
            assemblyPath,
            0,
            0,
            false,
            false,
            0,
            0,
            diagnostics);

    private static int CountComponents(AssemblyDoc assembly)
    {
        var components = assembly.GetComponents(false) as object[];
        return components?.Length ?? 0;
    }

    private void Close(string title)
    {
        try
        {
            _solidWorks.CloseDoc(title);
        }
        catch
        {
            // Best effort; only documents created by this service are targeted.
        }
    }
}
