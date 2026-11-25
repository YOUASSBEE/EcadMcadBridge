using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace EcadMcadBridge.SolidWorks.Importing;

public sealed record ImportedBodyPart(
    string ComponentId,
    string Designator,
    int BodyOrdinal,
    string AssetPath,
    string NativePartPath,
    int SolidBodyCount);

public sealed record SolidWorksImportedAsset(
    string AssetPath,
    string NativePartPath,
    int ImportedBodyCount,
    int ReopenedBodyCount,
    bool SaveSucceeded,
    bool ReopenSucceeded,
    int SaveErrors,
    int SaveWarnings,
    int OpenErrors,
    int OpenWarnings);

public sealed record SolidWorksImportResult(
    IReadOnlyList<ImportedBodyPart> BodyParts,
    IReadOnlyList<SolidWorksImportedAsset> Assets,
    IReadOnlyList<SolidWorksDiagnostic> Diagnostics)
{
    public bool IsComplete => Diagnostics.All(item => item.Severity != SolidWorksDiagnosticSeverity.Error)
        && BodyParts.Count > 0;
}

/// <summary>
/// Sequential, single-threaded per-body STEP import. The caller must provide an STA thread.
/// </summary>
public sealed class SolidWorksImportService
{
    private readonly ISldWorks _solidWorks;

    public SolidWorksImportService(ISldWorks solidWorks)
    {
        _solidWorks = solidWorks ?? throw new ArgumentNullException(nameof(solidWorks));
    }

    public SolidWorksImportResult Import(SolidWorksImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var diagnostics = new List<SolidWorksDiagnostic>();
        var assets = new List<SolidWorksImportedAsset>();
        var parts = new List<ImportedBodyPart>();

        if (!plan.IsValid)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-IMP-001",
                SolidWorksDiagnosticSeverity.Error,
                "plan",
                "The import plan contains errors."));
            return new SolidWorksImportResult(parts, assets, diagnostics);
        }

        foreach (var asset in plan.Assets)
        {
            var imported = ImportAsset(asset, diagnostics);
            if (imported is null)
                continue;

            assets.Add(imported.Asset);
            if (!imported.Asset.SaveSucceeded || !imported.Asset.ReopenSucceeded)
                continue;

            foreach (var body in asset.BodyReferences)
            {
                parts.Add(new ImportedBodyPart(
                    body.ComponentId,
                    body.Designator,
                    body.BodyOrdinal,
                    asset.AssetPath,
                    imported.Asset.NativePartPath,
                    imported.Asset.ReopenedBodyCount));
            }
        }

        var mappedReferences = new HashSet<string>(
            parts.Select(part => part.ComponentId + "\u001f" + part.BodyOrdinal.ToString(CultureInfo.InvariantCulture)
                + "\u001f" + part.AssetPath),
            StringComparer.Ordinal);
        foreach (var body in plan.BodyReferences)
        {
            var key = body.ComponentId + "\u001f" + body.BodyOrdinal.ToString(CultureInfo.InvariantCulture)
                + "\u001f" + body.AssetPath;
            if (!mappedReferences.Contains(key))
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-002",
                    SolidWorksDiagnosticSeverity.Warning,
                    body.AssetPath,
                    $"Body {body.Designator}/{body.BodyOrdinal} was not mapped to a native part."));
            }
        }

        return new SolidWorksImportResult(parts, assets, diagnostics);
    }

    private ImportedAssetResult? ImportAsset(
        SolidWorksStepAsset asset,
        List<SolidWorksDiagnostic> diagnostics)
    {
        string? importedTitle = null;
        string? partTitle = null;
        ModelDoc2? importedDocument = null;
        ModelDoc2? partDocument = null;
        var openedComponentTitles = new List<string>();
        var saveErrors = 0;
        var saveWarnings = 0;
        var openErrors = 0;
        var openWarnings = 0;
        var importedBodies = 0;
        var reopenedBodies = 0;
        var saveSucceeded = false;
        var reopenSucceeded = false;

        try
        {
            var importData = _solidWorks.GetImportFileData(asset.FullPath) as IImportStepData;
            if (importData is null)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-003",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "SOLIDWORKS did not return IImportStepData for the STEP file."));
                return null;
            }

            importData.MapConfigurationData = false;
            var errors = 0;
            importedDocument = _solidWorks.LoadFile4(asset.FullPath, "r", importData, ref errors) as ModelDoc2;
            if (importedDocument is null || errors != 0)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-004",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "LoadFile4 failed with errors=" + errors.ToString(CultureInfo.InvariantCulture) + "."));
                return null;
            }

            importedTitle = importedDocument.GetTitle();
            var documentType = importedDocument.GetType();
            if (documentType == (int)swDocumentTypes_e.swDocPART && importedDocument is PartDoc topPart)
            {
                partDocument = importedDocument;
                importedBodies = CountSolidBodies(topPart);
            }
            else if (documentType == (int)swDocumentTypes_e.swDocASSEMBLY
                     && importedDocument is AssemblyDoc importedAssembly)
            {
                CollectComponentTitles(importedAssembly, openedComponentTitles);
                partDocument = importedDocument;
                importedBodies = CountAssemblySolidBodies(importedAssembly);
            }
            else
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-005",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "Imported document type " +
                    documentType.ToString(CultureInfo.InvariantCulture) +
                    " is not a SOLIDWORKS part or assembly."));
                return null;
            }

            partTitle = importedTitle;
            if (importedBodies <= 0)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-006",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "Imported STEP contains no solid B-Rep bodies."));
                return null;
            }

            var activateErrors = 0;
            var active = _solidWorks.ActivateDoc3(importedTitle, false, 0, ref activateErrors) as ModelDoc2;
            if (active is null || activateErrors != 0)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-007",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "Imported document could not be activated; errors=" +
                    activateErrors.ToString(CultureInfo.InvariantCulture) + "."));
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(asset.NativePartPath)!);
            var extension = partDocument.Extension;
            if (extension is null)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-008",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "ModelDoc2.Extension is unavailable."));
                return null;
            }

            saveSucceeded = extension.SaveAs3(
                asset.NativePartPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                null,
                ref saveErrors,
                ref saveWarnings);
            if (!saveSucceeded
                || !File.Exists(asset.NativePartPath)
                || new FileInfo(asset.NativePartPath).Length <= 0)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-009",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "SaveAs3 did not create a non-empty SLDPRT."));
                return null;
            }

            CloseCreated(partTitle);
            partTitle = null;
            CloseCreated(importedTitle);
            importedTitle = null;
            foreach (var componentTitle in openedComponentTitles)
                CloseCreated(componentTitle);
            openedComponentTitles.Clear();

            var reopened = _solidWorks.OpenDoc6(
                asset.NativePartPath,
                (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                string.Empty,
                ref openErrors,
                ref openWarnings);
            if (reopened is null || openErrors != 0)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-010",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "Reopening the saved SLDPRT failed with errors=" +
                    openErrors.ToString(CultureInfo.InvariantCulture) + "."));
                return new ImportedAssetResult(
                    new SolidWorksImportedAsset(
                        asset.AssetPath,
                        asset.NativePartPath,
                        importedBodies,
                        reopenedBodies,
                        saveSucceeded,
                        reopenSucceeded,
                        saveErrors,
                        saveWarnings,
                        openErrors,
                        openWarnings));
            }

            var reopenedTitle = reopened.GetTitle();
            reopenedBodies = CountSolidBodies(reopened);
            reopenSucceeded = reopenedBodies == importedBodies;
            if (!reopenSucceeded)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-011",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "Reopened SLDPRT contains a different solid body count."));
            }

            CloseCreated(reopenedTitle);
            return new ImportedAssetResult(
                new SolidWorksImportedAsset(
                    asset.AssetPath,
                    asset.NativePartPath,
                    importedBodies,
                    reopenedBodies,
                    saveSucceeded,
                    reopenSucceeded,
                    saveErrors,
                    saveWarnings,
                    openErrors,
                    openWarnings));
        }
        catch (Exception ex)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-IMP-012",
                SolidWorksDiagnosticSeverity.Error,
                asset.AssetPath,
                "SOLIDWORKS import failed: " + ex.GetType().Name + "."));
            return null;
        }
        finally
        {
            if (partTitle is not null)
                CloseCreated(partTitle);
            if (importedTitle is not null)
                CloseCreated(importedTitle);
            foreach (var componentTitle in openedComponentTitles)
                CloseCreated(componentTitle);
        }
    }

    private static int CountAssemblySolidBodies(AssemblyDoc assembly)
    {
        var components = assembly.GetComponents(false) as object[];
        if (components is null)
            return 0;

        var count = 0;
        foreach (var item in components)
        {
            if (item is not Component2 component)
                continue;
            if (component.GetModelDoc2() is PartDoc part)
                count += CountSolidBodies(part);
        }

        return count;
    }

    private static void CollectComponentTitles(AssemblyDoc assembly, ICollection<string> titles)
    {
        var seen = new HashSet<string>(titles, StringComparer.Ordinal);
        var components = assembly.GetComponents(false) as object[];
        if (components is null)
            return;

        foreach (var item in components)
        {
            if (item is not Component2 component)
                continue;
            if (component.GetModelDoc2() is not ModelDoc2 model)
                continue;
            var title = model.GetTitle();
            if (!string.IsNullOrWhiteSpace(title) && seen.Add(title))
                titles.Add(title);
        }
    }

    private void CloseCreated(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;
        try
        {
            _solidWorks.CloseDoc(title);
        }
        catch
        {
            // Best effort; never close unrelated documents.
        }
    }

    private static int CountSolidBodies(ModelDoc2 document)
    {
        if (document is not PartDoc part)
            return 0;
        return CountSolidBodies(part);
    }

    private static int CountSolidBodies(PartDoc part)
    {
        var bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        return bodies?.Length ?? 0;
    }

    private sealed record ImportedAssetResult(SolidWorksImportedAsset Asset);
}
