using System.Globalization;
using System.IO;
using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using PCB;

namespace EcadMcadBridge.Altium.Adapter;

internal sealed record AltiumComponentModelResult(
    ComponentModelsWriteRequest Request,
    IReadOnlyList<string> Warnings,
    AltiumComponentModelDiagnostics Diagnostics);

internal sealed class AltiumComponentModelDiagnostics
{
    public int Components { get; set; }

    public int BodiesDetected { get; set; }

    public int BodiesWithModel { get; set; }

    public int ModelsMissing { get; set; }

    public int ModelsEmpty { get; set; }

    public int ModelsStale { get; set; }

    public int ModelStateUnknown { get; set; }

    public int StepExportsSucceeded { get; set; }

    public int StepExportsFailed { get; set; }

    public int StepFallbacks { get; set; }

    public int MetadataWarnings { get; set; }

    public int MultiBodyComponents { get; set; }

    public int BodiesWithoutAsset => BodiesDetected - StepExportsSucceeded;

    public string Summary() =>
        $"components={Components} bodies={BodiesDetected} withModel={BodiesWithModel} " +
        $"missingModel={ModelsMissing} emptyModel={ModelsEmpty} staleModel={ModelsStale} " +
        $"unknownModelState={ModelStateUnknown} " +
        $"stepAssets={StepExportsSucceeded} exportFailures={StepExportsFailed} " +
        $"fallbacks={StepFallbacks} metadataWarnings={MetadataWarnings} " +
        $"multiBodyComponents={MultiBodyComponents} bodiesWithoutAsset={BodiesWithoutAsset}";
}

internal sealed class AltiumModelInventoryException : Exception
{
    public AltiumModelInventoryException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Read-only per-body component model extraction. Exports each IPCB_ComponentBody to a
/// temporary STEP file and builds the Core model write request.
/// </summary>
internal static class AltiumComponentModelReader
{
    private const string StepExtension = ".step";

    public static AltiumComponentModelResult Read(
        IPCB_Board board,
        BoardSnapshot snapshot,
        IReadOnlyList<AltiumComponentIdentity> identities,
        string exportDirectory)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(identities);
        if (string.IsNullOrWhiteSpace(exportDirectory))
            throw new ArgumentException("Export directory is required.", nameof(exportDirectory));

        Directory.CreateDirectory(exportDirectory);

        var warnings = new List<string>();
        var diagnostics = new AltiumComponentModelDiagnostics
        {
            Components = snapshot.Components.Count,
        };
        var resolver = AltiumComponentIdentityResolver.Build(identities);
        var ordinals = new AltiumBodyOrdinalTracker();
        var bodies = new List<AltiumBodyInventoryEntry>();
        var assets = new List<ComponentModelAssetSource>();

        var iterator = board.BoardIterator_Create();
        if (iterator is null)
            throw new AltiumModelInventoryException("The component body iterator is unavailable.");

        try
        {
            try
            {
                iterator.AddFilter_ObjectSet(new TObjectSet { TObjectId.eComponentBodyObject });
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
            }
            catch (Exception ex)
            {
                throw new AltiumModelInventoryException(
                    "The component body iterator filter could not be configured: " + ex.GetType().Name + ".");
            }

            var index = 0;
            for (var primitive = iterator.FirstPCBObject(); primitive is not null; primitive = iterator.NextPCBObject())
            {
                if (primitive is not IPCB_ComponentBody body)
                    continue;

                index++;
                var owner = GetOwner(body);
                var ownerKey = CreateOwnerKey(owner);
                var resolution = resolver.Resolve(ownerKey);
                var componentId = AltiumComponentResolutionGuard.RequireComponentId(resolution);
                var ordinal = ordinals.Next(componentId);
                diagnostics.BodiesDetected++;
                var label = Label(ownerKey.Designator, ordinal);
                bodies.Add(ReadBody(
                    body,
                    componentId,
                    ordinal,
                    label,
                    index,
                    exportDirectory,
                    assets,
                    warnings,
                    diagnostics));
            }
        }
        catch (AltiumModelInventoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AltiumModelInventoryException(
                "Component body enumeration failed: " + ex.GetType().Name + ".");
        }
        finally
        {
            board.BoardIterator_Destroy(ref iterator);
        }

        diagnostics.MultiBodyComponents = bodies
            .GroupBy(body => body.ComponentId, StringComparer.Ordinal)
            .Count(group => group.Count() > 1);

        return new AltiumComponentModelResult(
            AltiumComponentModelRequestBuilder.Build(snapshot, bodies, assets),
            warnings,
            diagnostics);
    }

    private static AltiumBodyInventoryEntry ReadBody(
        IPCB_ComponentBody body,
        string componentId,
        int bodyOrdinal,
        string label,
        int globalIndex,
        string exportDirectory,
        List<ComponentModelAssetSource> assets,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        var bodyProjection = ReadBodyProjection(body, label, warnings, diagnostics);
        var standoffHeight = ReadStandoffHeight(body, label, warnings, diagnostics);
        var overallHeight = ReadOverallHeight(body, label, warnings, diagnostics);
        IPCB_Model? model = null;
        try
        {
            model = body.GetModel();
        }
        catch (Exception ex)
        {
            AddMetadataWarning(diagnostics, warnings, label, "model", ex);
        }

        if (model is null)
        {
            diagnostics.ModelsMissing++;
            warnings.Add(label + " has no 3D model; no STEP asset was created.");
            return NotAcquired(
                componentId,
                bodyOrdinal,
                bodyProjection,
                standoffHeight,
                overallHeight);
        }

        diagnostics.BodiesWithModel++;
        var modelType = ReadModelType(model, label, warnings, diagnostics);
        var embed = ReadEmbed(model, label, warnings, diagnostics);
        var source = ReadModelSource(model, label, warnings, diagnostics);
        var fileName = ReadFileName(model, label, warnings, diagnostics);
        var representation = AltiumModelClassification.MapRepresentation(modelType);
        var externalFileName = AltiumModelClassification.IsExternalFileName(
            fileName,
            source,
            representation)
            ? fileName
            : null;
        var sourceOrigin = AltiumModelClassification.ClassifySourceOrigin(
            modelType,
            embed,
            source,
            externalFileName);
        var origin = ReadOrigin(model, label, warnings, diagnostics);
        var state = ReadState(model, label, warnings, diagnostics);
        var rotation = ReadRotation(model, label, warnings, diagnostics);
        var stateSource = AltiumModelMetadataMapper.MapStateSource(representation, sourceOrigin);
        var empty = ReadIsEmpty(model, label, warnings, diagnostics);
        var stale = ReadIsStale(model, label, warnings, diagnostics);
        var modelState = AltiumModelStateClassifier.Classify(empty, stale);

        if (modelState != AltiumModelStateStatus.Clean)
        {
            if (empty is null || stale is null)
            {
                diagnostics.ModelStateUnknown++;
                warnings.Add(
                    label + " model emptiness/staleness could not be verified; " +
                    "no STEP asset was created.");
            }

            if (empty == true)
            {
                diagnostics.ModelsEmpty++;
                warnings.Add(label + " has an empty model; no STEP asset was created.");
            }

            if (stale == true)
            {
                diagnostics.ModelsStale++;
                warnings.Add(label + " has a stale model; no STEP asset was created.");
            }

            return new AltiumBodyInventoryEntry(
                componentId,
                bodyOrdinal,
                representation,
                sourceOrigin,
                origin,
                state,
                rotation,
                ComponentModelAssetAcquisition.NotAcquired,
                null,
                bodyProjection,
                standoffHeight,
                overallHeight,
                stateSource);
        }

        var assetKey = "body-" + globalIndex.ToString("D6", CultureInfo.InvariantCulture);
        var path = Path.Combine(exportDirectory, assetKey + StepExtension);
        if (!TryExport(body, model, path, label, warnings, diagnostics))
        {
            diagnostics.StepExportsFailed++;
            return new AltiumBodyInventoryEntry(
                componentId,
                bodyOrdinal,
                representation,
                sourceOrigin,
                origin,
                state,
                rotation,
                ComponentModelAssetAcquisition.NotAcquired,
                null,
                bodyProjection,
                standoffHeight,
                overallHeight,
                stateSource);
        }

        diagnostics.StepExportsSucceeded++;
        assets.Add(new ComponentModelAssetSource
        {
            AssetKey = assetKey,
            SourcePath = path,
            Format = ComponentModelAssetFormat.StepAp214,
        });
        return new AltiumBodyInventoryEntry(
            componentId,
            bodyOrdinal,
            representation,
            sourceOrigin,
            origin,
            state,
            rotation,
            ComponentModelAssetAcquisition.SdkExport,
            assetKey,
            bodyProjection,
            standoffHeight,
            overallHeight,
            stateSource);
    }

    private static bool TryExport(
        IPCB_ComponentBody body,
        IPCB_Model model,
        string path,
        string label,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        try
        {
            var saved = body.SaveModelToFile(path);
            if (saved && AltiumStepExportFile.IsValid(path))
                return true;

            if (saved)
                warnings.Add(label + " produced an invalid STEP file; the file was discarded.");
        }
        catch (Exception ex)
        {
            warnings.Add(label + " SaveModelToFile failed: " + ex.GetType().Name + ".");
        }

        AltiumStepExportFile.TryDelete(path);

        try
        {
            model.ExportToStep(path);
            if (AltiumStepExportFile.IsValid(path))
            {
                diagnostics.StepFallbacks++;
                warnings.Add(label + " used the IPCB_Model.ExportToStep fallback.");
                return true;
            }
        }
        catch (Exception ex)
        {
            warnings.Add(label + " ExportToStep fallback failed: " + ex.GetType().Name + ".");
        }

        AltiumStepExportFile.TryDelete(path);
        warnings.Add(label + " has no valid STEP asset; the body entry was kept without an asset.");
        return false;
    }

    private static IPCB_Component GetOwner(IPCB_ComponentBody body)
    {
        try
        {
            var owner = body.GetState_Component();
            if (owner is null)
                throw new AltiumModelInventoryException("A component body has no owning component.");
            return owner;
        }
        catch (AltiumModelInventoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AltiumModelInventoryException(
                "A component body owner could not be read: " + ex.GetType().Name + ".");
        }
    }

    private static AltiumComponentKey CreateOwnerKey(IPCB_Component owner)
    {
        try
        {
            var designator = owner.GetState_Name()?.GetState_Text() ?? string.Empty;
            var uniqueId = owner.GetState_UniqueId();
            var sourceUniqueId = owner.GetState_SourceUniqueId();
            var x = owner.GetState_XLocation();
            var y = owner.GetState_YLocation();
            var rotation = AngleNormalizer.NormalizeDegrees(owner.GetState_Rotation());
            var side = AltiumBoardSnapshotAdapter.ResolveComponentSide(owner);
            return new AltiumComponentKey(
                uniqueId,
                sourceUniqueId,
                designator,
                x,
                y,
                rotation,
                side);
        }
        catch (Exception ex)
        {
            throw new AltiumModelInventoryException(
                "A component body owner identity could not be read: " + ex.GetType().Name + ".");
        }
    }

    private static AltiumBodyInventoryEntry NotAcquired(
        string componentId,
        int bodyOrdinal,
        ComponentBodyProjection bodyProjection,
        LengthValue standoffHeight,
        LengthValue overallHeight) =>
        new(
            componentId,
            bodyOrdinal,
            ComponentModelRepresentation.Unknown,
            ComponentModelSourceOrigin.Unknown,
            new ModelOrigin(),
            new ModelState(),
            null,
            ComponentModelAssetAcquisition.NotAcquired,
            null,
            bodyProjection,
            standoffHeight,
            overallHeight,
            ComponentModelStateSource.Unknown);

    private static ComponentBodyProjection ReadBodyProjection(
        IPCB_ComponentBody body,
        string label,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        try
        {
            return AltiumModelMetadataMapper.MapBodyProjection(body.GetBodyProjection());
        }
        catch (Exception ex)
        {
            AddMetadataWarning(diagnostics, warnings, label, "bodyProjection", ex);
            return ComponentBodyProjection.Unknown;
        }
    }

    private static LengthValue ReadStandoffHeight(
        IPCB_ComponentBody body,
        string label,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        try
        {
            return AltiumModelMetadataMapper.MapLength(body.GetStandoffHeight());
        }
        catch (Exception ex)
        {
            AddMetadataWarning(diagnostics, warnings, label, "standoffHeight", ex);
            return LengthValue.FromIu(null);
        }
    }

    private static LengthValue ReadOverallHeight(
        IPCB_ComponentBody body,
        string label,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        try
        {
            return AltiumModelMetadataMapper.MapLength(body.GetOverallHeight());
        }
        catch (Exception ex)
        {
            AddMetadataWarning(diagnostics, warnings, label, "overallHeight", ex);
            return LengthValue.FromIu(null);
        }
    }

    private static T3DModelType? ReadModelType(
        IPCB_Model model,
        string label,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        try
        {
            return model.GetState_ModelType();
        }
        catch (Exception ex)
        {
            AddMetadataWarning(diagnostics, warnings, label, "modelType", ex);
            return null;
        }
    }

    private static bool? ReadEmbed(
        IPCB_Model model,
        string label,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        try
        {
            return model.GetEmbed();
        }
        catch (Exception ex)
        {
            AddMetadataWarning(diagnostics, warnings, label, "embed", ex);
            return null;
        }
    }

    private static TModelSource? ReadModelSource(
        IPCB_Model model,
        string label,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        try
        {
            return model.GetModelSource();
        }
        catch (Exception ex)
        {
            AddMetadataWarning(diagnostics, warnings, label, "modelSource", ex);
            return null;
        }
    }

    private static string? ReadFileName(
        IPCB_Model model,
        string label,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        try
        {
            return model.GetFileName();
        }
        catch (Exception ex)
        {
            AddMetadataWarning(diagnostics, warnings, label, "fileName", ex);
            return null;
        }
    }

    private static ModelOrigin ReadOrigin(
        IPCB_Model model,
        string label,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        try
        {
            var origin = model.GetOrigin();
            return AltiumModelMetadataMapper.MapOrigin(origin.X, origin.Y);
        }
        catch (Exception ex)
        {
            AddMetadataWarning(diagnostics, warnings, label, "origin", ex);
            return new ModelOrigin();
        }
    }

    private static ModelState ReadState(
        IPCB_Model model,
        string label,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        try
        {
            model.GetState(out var rotationX, out var rotationY, out var rotationZ, out var dz);
            return AltiumModelMetadataMapper.MapState(rotationX, rotationY, rotationZ, dz);
        }
        catch (Exception ex)
        {
            AddMetadataWarning(diagnostics, warnings, label, "state", ex);
            return new ModelState();
        }
    }

    private static double? ReadRotation(
        IPCB_Model model,
        string label,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        try
        {
            return AltiumModelMetadataMapper.MapRotation(model.GetRotation());
        }
        catch (Exception ex)
        {
            AddMetadataWarning(diagnostics, warnings, label, "rotation", ex);
            return null;
        }
    }

    private static bool? ReadIsEmpty(
        IPCB_Model model,
        string label,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        try
        {
            return model.IsEmpty();
        }
        catch (Exception ex)
        {
            AddMetadataWarning(diagnostics, warnings, label, "isEmpty", ex);
            return null;
        }
    }

    private static bool? ReadIsStale(
        IPCB_Model model,
        string label,
        List<string> warnings,
        AltiumComponentModelDiagnostics diagnostics)
    {
        try
        {
            return model.IsStale();
        }
        catch (Exception ex)
        {
            AddMetadataWarning(diagnostics, warnings, label, "isStale", ex);
            return null;
        }
    }

    private static void AddMetadataWarning(
        AltiumComponentModelDiagnostics diagnostics,
        List<string> warnings,
        string label,
        string field,
        Exception exception)
    {
        diagnostics.MetadataWarnings++;
        warnings.Add(label + " metadata '" + field + "' is unavailable: " + exception.GetType().Name + ".");
    }

    private static string Label(string designator, int ordinal) =>
        (string.IsNullOrWhiteSpace(designator) ? "Body" : designator + " body") +
        " " + ordinal.ToString(CultureInfo.InvariantCulture);
}
