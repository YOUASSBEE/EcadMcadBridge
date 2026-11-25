using System.Globalization;
using EcadMcadBridge.SolidWorks.Interop;
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
    IReadOnlyList<SolidWorksDiagnostic> Diagnostics,
    SolidWorksDocumentLifecycle Lifecycle)
{
    public bool IsComplete => Diagnostics.All(item => item.Severity != SolidWorksDiagnosticSeverity.Error)
        && BodyParts.Count > 0;
}

/// <summary>
/// Sequential, single-threaded per-body STEP import. The caller must provide an STA thread.
/// Only documents tracked as newly opened by this operation are closed.
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
        var tracker = SolidWorksDocumentTracker.Capture(_solidWorks);

        if (!plan.IsValid)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-IMP-001",
                SolidWorksDiagnosticSeverity.Error,
                "plan",
                "The import plan contains errors."));
            return new SolidWorksImportResult(
                parts,
                assets,
                diagnostics,
                tracker.Snapshot());
        }

        try
        {
            foreach (var asset in plan.Assets)
            {
                var imported = ImportAsset(asset, diagnostics, tracker);
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
        }
        finally
        {
            AddCleanupWarnings(diagnostics, tracker.CloseCreated());
        }

        return new SolidWorksImportResult(parts, assets, diagnostics, tracker.Snapshot());
    }

    private ImportedAssetResult? ImportAsset(
        SolidWorksStepAsset asset,
        List<SolidWorksDiagnostic> diagnostics,
        SolidWorksDocumentTracker tracker)
    {
        var saveErrors = 0;
        var saveWarnings = 0;
        var openErrors = 0;
        var openWarnings = 0;
        var importedBodies = 0;
        var reopenedBodies = 0;
        var saveSucceeded = false;
        var reopenSucceeded = false;
        string? tempPartPath = null;
        string? verifyPartPath = null;

        try
        {
            if (!SolidWorksFileGuard.TryValidateNewTarget(asset.NativePartPath, _solidWorks, out var targetReason))
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-020",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    targetReason));
                return null;
            }

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
            var importedDocument = _solidWorks.LoadFile4(asset.FullPath, "r", importData, ref errors) as ModelDoc2;
            if (importedDocument is null || errors != 0)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-004",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "LoadFile4 failed with errors=" + errors.ToString(CultureInfo.InvariantCulture) + "."));
                return null;
            }

            tracker.Track(importedDocument);
            var documentType = importedDocument.GetType();
            var isAssembly = documentType == (int)swDocumentTypes_e.swDocASSEMBLY;
            if (documentType == (int)swDocumentTypes_e.swDocPART && importedDocument is PartDoc topPart)
            {
                importedBodies = CountSolidBodies(topPart);
            }
            else if (isAssembly && importedDocument is AssemblyDoc importedAssembly)
            {
                TrackComponents(importedAssembly, tracker);
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

            if (importedBodies <= 0)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-006",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "Imported STEP contains no solid B-Rep bodies."));
                return null;
            }

            var nativeDirectory = Path.GetDirectoryName(asset.NativePartPath)!;
            var tempDirectory = Path.Combine(nativeDirectory, ".bridge-temp");
            Directory.CreateDirectory(tempDirectory);
            tempPartPath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".SLDPRT");
            saveSucceeded = SaveAsPart(
                asset,
                importedDocument,
                isAssembly,
                tempPartPath,
                diagnostics,
                ref saveErrors,
                ref saveWarnings);
            AddSaveWarnings(diagnostics, asset.AssetPath, saveWarnings);
            if (!SolidWorksFileGuard.IsSaveSuccessful(saveSucceeded, saveErrors, tempPartPath))
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-009",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "SaveAs3 did not create a valid SLDPRT (returned=" +
                    saveSucceeded.ToString(CultureInfo.InvariantCulture) +
                    ", errors=" + saveErrors.ToString(CultureInfo.InvariantCulture) + ")."));
                return null;
            }

            AddCleanupWarnings(diagnostics, tracker.CloseCreated(importedDocument));
            if (File.Exists(asset.NativePartPath))
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-020",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "Target SLDPRT appeared before the temporary save could be moved."));
                return null;
            }

            try
            {
                File.Move(tempPartPath, asset.NativePartPath);
                tempPartPath = null;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-020",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "The temporary SLDPRT could not be moved to the native target: " +
                    ex.GetType().Name + "."));
                return null;
            }

            var verifyDirectory = Path.Combine(nativeDirectory, ".bridge-verify");
            Directory.CreateDirectory(verifyDirectory);
            verifyPartPath = Path.Combine(verifyDirectory, Guid.NewGuid().ToString("N") + ".SLDPRT");
            File.Copy(asset.NativePartPath, verifyPartPath, overwrite: false);

            var reopened = _solidWorks.OpenDoc6(
                verifyPartPath,
                (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                string.Empty,
                ref openErrors,
                ref openWarnings);
            AddLoadWarnings(diagnostics, asset.AssetPath, openWarnings);
            var alreadyOpen = (openWarnings & (int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen) != 0;
            if (alreadyOpen)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-010",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "The saved SLDPRT was already open during reopen verification."));
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

            tracker.Track(reopened, openWarnings);
            reopenedBodies = CountSolidBodies(reopened);
            reopenSucceeded = reopenedBodies == importedBodies;
            if (!reopenSucceeded)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-011",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "Reopened SLDPRT contains " +
                    reopenedBodies.ToString(CultureInfo.InvariantCulture) +
                    " solid bodies; expected " +
                    importedBodies.ToString(CultureInfo.InvariantCulture) + "."));
            }

            AddCleanupWarnings(diagnostics, tracker.CloseCreated(reopened));
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
            AddCleanupWarnings(diagnostics, tracker.CloseCreated());
            foreach (var temporary in new[] { tempPartPath, verifyPartPath })
            {
                if (temporary is null)
                    continue;
                try
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
                catch
                {
                    // Temporary native output is inside the Bridge output directory only.
                }
            }
        }
    }

    private bool SaveAsPart(
        SolidWorksStepAsset asset,
        ModelDoc2 document,
        bool isAssembly,
        string targetPath,
        List<SolidWorksDiagnostic> diagnostics,
        ref int saveErrors,
        ref int saveWarnings)
    {
        var extension = document.Extension;
        if (extension is null)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-IMP-008",
                SolidWorksDiagnosticSeverity.Error,
                asset.AssetPath,
                "ModelDoc2.Extension is unavailable."));
            return false;
        }

        if (!isAssembly)
        {
            return extension.SaveAs3(
                targetPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                null,
                ref saveErrors,
                ref saveWarnings);
        }

        var original = 0;
        var originalRead = false;
        try
        {
            try
            {
                original = _solidWorks.GetUserPreferenceIntegerValue(
                    (int)swUserPreferenceIntegerValue_e.swSaveAssemblyAsPartOptions);
                originalRead = true;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-018",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "Assembly-as-part preference could not be read: " + ex.GetType().Name + "."));
                return false;
            }

            if (!_solidWorks.SetUserPreferenceIntegerValue(
                    (int)swUserPreferenceIntegerValue_e.swSaveAssemblyAsPartOptions,
                    (int)swSaveAsmAsPartOptions_e.swSaveAsmAsPart_AllComponents))
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-IMP-018",
                    SolidWorksDiagnosticSeverity.Error,
                    asset.AssetPath,
                    "Assembly-as-part preference could not be set to AllComponents."));
                return false;
            }

            return extension.SaveAs3(
                targetPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                null,
                ref saveErrors,
                ref saveWarnings);
        }
        finally
        {
            if (originalRead)
            {
                try
                {
                    if (_solidWorks.SetUserPreferenceIntegerValue(
                            (int)swUserPreferenceIntegerValue_e.swSaveAssemblyAsPartOptions,
                            original))
                    {
                        diagnostics.Add(new SolidWorksDiagnostic(
                            "SW-IMP-019",
                            SolidWorksDiagnosticSeverity.Info,
                            asset.AssetPath,
                            "Assembly-as-part preference restored to " +
                            original.ToString(CultureInfo.InvariantCulture) + "."));
                    }
                    else
                    {
                        diagnostics.Add(new SolidWorksDiagnostic(
                            "SW-IMP-019",
                            SolidWorksDiagnosticSeverity.Error,
                            asset.AssetPath,
                            "Assembly-as-part preference could not be restored."));
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new SolidWorksDiagnostic(
                        "SW-IMP-019",
                        SolidWorksDiagnosticSeverity.Error,
                        asset.AssetPath,
                        "Assembly-as-part preference restore failed: " + ex.GetType().Name + "."));
                }
            }
        }
    }

    private void TrackComponents(AssemblyDoc assembly, SolidWorksDocumentTracker tracker)
    {
        var components = assembly.GetComponents(false) as object[];
        if (components is null)
            return;

        foreach (var item in components)
        {
            if (item is Component2 component
                && component.GetModelDoc2() is ModelDoc2 model)
            {
                tracker.Track(model);
            }
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
            if (item is Component2 component
                && component.GetModelDoc2() is PartDoc part)
            {
                count += CountSolidBodies(part);
            }
        }

        return count;
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

    private static void AddSaveWarnings(
        List<SolidWorksDiagnostic> diagnostics,
        string assetPath,
        int warnings)
    {
        foreach (var flag in SolidWorksWarningDecoder.DecodeSaveWarnings(warnings))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-IMP-021",
                SolidWorksDiagnosticSeverity.Warning,
                assetPath,
                "Save warning " + flag.Name + " (" +
                flag.Value.ToString(CultureInfo.InvariantCulture) + ")."));
        }
    }

    private static void AddLoadWarnings(
        List<SolidWorksDiagnostic> diagnostics,
        string assetPath,
        int warnings)
    {
        foreach (var flag in SolidWorksWarningDecoder.DecodeLoadWarnings(warnings))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-IMP-022",
                SolidWorksDiagnosticSeverity.Warning,
                assetPath,
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
                "SW-IMP-CLEANUP",
                SolidWorksDiagnosticSeverity.Warning,
                "documents",
                warning));
        }
    }

    private sealed record ImportedAssetResult(SolidWorksImportedAsset Asset);
}
