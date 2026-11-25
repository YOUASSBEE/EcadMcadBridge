using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Core.Packaging;

/// <summary>
/// Validates a component model catalog against the board snapshot and the package file set.
/// It never loads geometry and never interprets transforms.
/// </summary>
public static class ComponentModelsValidator
{
    private const string AssetPrefix = "assets/3d/";

    /// <summary>Same unit tolerance as BoardSnapshotValidator.</summary>
    private const double UnitToleranceMm = 1e-9;

    public static IReadOnlyList<PackageDiagnostic> ValidateStructure(
        ComponentModelCatalog catalog,
        BoardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<PackageDiagnostic>();

        if (!string.Equals(catalog.Schema, ComponentModelCatalog.SchemaName, StringComparison.Ordinal))
        {
            Error(
                PackageDiagnosticCodes.UnsupportedModelSchema,
                "models.schema",
                "Unsupported component model catalog schema.",
                diagnostics);
        }

        if (!TryParseMajor(catalog.SchemaVersion, out var major) || major != 1)
        {
            Error(
                PackageDiagnosticCodes.UnsupportedModelSchema,
                "models.schemaVersion",
                "Component model catalog major version is not supported.",
                diagnostics);
        }

        var snapshotComponents = new Dictionary<string, ComponentInstance>(StringComparer.Ordinal);
        foreach (var component in snapshot.Components)
            snapshotComponents[component.Id] = component;

        var seenComponentIds = new HashSet<string>(StringComparer.Ordinal);
        var caseInsensitivePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var catalogComponents = catalog.Components ?? Array.Empty<ComponentModelEntry>();
        for (var i = 0; i < catalogComponents.Count; i++)
        {
            var component = catalogComponents[i];
            var componentPath = $"models.components[{i}]";
            if (component is null)
            {
                Error(PackageDiagnosticCodes.InvalidModelCatalog, componentPath, "Component entry is null.", diagnostics);
                continue;
            }

            var componentId = component.ComponentId ?? string.Empty;
            if (componentId.Length == 0)
            {
                Error(PackageDiagnosticCodes.InvalidModelReference, componentPath + ".componentId", "Component id is required.", diagnostics);
            }
            else if (!seenComponentIds.Add(componentId))
            {
                Error(
                    PackageDiagnosticCodes.InvalidModelReference,
                    componentPath + ".componentId",
                    "Duplicate component id in the model catalog.",
                    diagnostics);
            }
            else if (!snapshotComponents.TryGetValue(componentId, out var snapshotComponent))
            {
                Error(
                    PackageDiagnosticCodes.InvalidModelReference,
                    componentPath + ".componentId",
                    "Component id is not present in board.snapshot.json.",
                    diagnostics);
            }
            else if (!string.Equals(component.Designator, snapshotComponent.Designator, StringComparison.Ordinal))
            {
                Error(
                    PackageDiagnosticCodes.InvalidModelReference,
                    componentPath + ".designator",
                    "Designator does not match the board snapshot component.",
                    diagnostics);
            }

            var bodies = component.Bodies ?? Array.Empty<ComponentBodyEntry>();
            var ordinals = new HashSet<int>();
            for (var j = 0; j < bodies.Count; j++)
            {
                var body = bodies[j];
                var bodyPath = componentPath + $".bodies[{j}]";
                if (body is null)
                {
                    Error(PackageDiagnosticCodes.InvalidModelCatalog, bodyPath, "Body entry is null.", diagnostics);
                    continue;
                }

                if (body.BodyOrdinal < 0)
                {
                    Error(PackageDiagnosticCodes.InvalidModelReference, bodyPath + ".bodyOrdinal", "Body ordinal must not be negative.", diagnostics);
                }
                else if (!ordinals.Add(body.BodyOrdinal))
                {
                    Error(
                        PackageDiagnosticCodes.InvalidModelReference,
                        bodyPath + ".bodyOrdinal",
                        "Duplicate body ordinal within the component.",
                        diagnostics);
                }

                ValidateEnum(body.Representation, bodyPath + ".representation", diagnostics);
                ValidateEnum(body.SourceOrigin, bodyPath + ".sourceOrigin", diagnostics);
                ValidateEnum(body.AssetAcquisition, bodyPath + ".assetAcquisition", diagnostics);
                ValidateEnum(body.BodyProjection, bodyPath + ".bodyProjection", diagnostics);
                ValidateEnum(body.ModelStateSource, bodyPath + ".modelStateSource", diagnostics);
                if (body.AssetFormat.HasValue)
                    ValidateEnum(body.AssetFormat.Value, bodyPath + ".assetFormat", diagnostics);

                ValidateLength(body.StandoffHeight, bodyPath + ".standoffHeight", diagnostics);
                ValidateLength(body.OverallHeight, bodyPath + ".overallHeight", diagnostics);

                ValidateFinite(body.Origin.XMm, bodyPath + ".sdkOrigin.xMm", diagnostics);
                ValidateFinite(body.Origin.YMm, bodyPath + ".sdkOrigin.yMm", diagnostics);
                ValidateFinite(body.Origin.ZMm, bodyPath + ".sdkOrigin.zMm", diagnostics);
                ValidateFinite(body.State.RotationXDegRaw, bodyPath + ".sdkState.rotationXDegRaw", diagnostics);
                ValidateFinite(body.State.RotationYDegRaw, bodyPath + ".sdkState.rotationYDegRaw", diagnostics);
                ValidateFinite(body.State.RotationZDegRaw, bodyPath + ".sdkState.rotationZDegRaw", diagnostics);
                ValidateFinite(body.State.DzMm, bodyPath + ".sdkState.dzMm", diagnostics);
                ValidateFinite(body.ModelRotationDegRaw, bodyPath + ".modelRotationDegRaw", diagnostics);

                ValidatePair(body.Origin.XIu, body.Origin.XMm, bodyPath + ".sdkOrigin.x", diagnostics);
                ValidatePair(body.Origin.YIu, body.Origin.YMm, bodyPath + ".sdkOrigin.y", diagnostics);
                ValidatePair(body.Origin.ZIu, body.Origin.ZMm, bodyPath + ".sdkOrigin.z", diagnostics);
                ValidatePair(body.State.DzIu, body.State.DzMm, bodyPath + ".sdkState.dz", diagnostics);

                var hasPath = body.AssetPath is not null;
                var isBlankPath = hasPath && string.IsNullOrWhiteSpace(body.AssetPath);
                if (isBlankPath)
                {
                    Error(
                        PackageDiagnosticCodes.InvalidModelAsset,
                        bodyPath + ".assetPath",
                        "assetPath must not be empty or whitespace.",
                        diagnostics);
                }

                var hasFormat = body.AssetFormat.HasValue;
                if (!hasPath && hasFormat)
                {
                    Error(
                        PackageDiagnosticCodes.InvalidModelAsset,
                        bodyPath,
                        "assetFormat requires a non-null assetPath.",
                        diagnostics);
                }
                else if (hasPath && !hasFormat)
                {
                    Error(
                        PackageDiagnosticCodes.InvalidModelAsset,
                        bodyPath,
                        "assetPath requires assetFormat.",
                        diagnostics);
                }

                var requiresAsset = body.AssetAcquisition is ComponentModelAssetAcquisition.SdkExport
                    or ComponentModelAssetAcquisition.SourceCopyVerified;
                if (requiresAsset && (!hasPath || isBlankPath))
                {
                    Error(
                        PackageDiagnosticCodes.InvalidModelAsset,
                        bodyPath + ".assetAcquisition",
                        "The acquisition method requires an asset file.",
                        diagnostics);
                }
                else if (body.AssetAcquisition == ComponentModelAssetAcquisition.NotAcquired && hasPath)
                {
                    Error(
                        PackageDiagnosticCodes.InvalidModelAsset,
                        bodyPath + ".assetAcquisition",
                        "An asset file is present but assetAcquisition says it was not acquired.",
                        diagnostics);
                }

                if (hasPath && !isBlankPath)
                {
                    var path = body.AssetPath!;
                    if (!TryValidateAssetPath(path, body.AssetFormat, out var reason))
                    {
                        Error(PackageDiagnosticCodes.InvalidModelAsset, bodyPath + ".assetPath", reason, diagnostics);
                    }
                    else if (caseInsensitivePaths.TryGetValue(path, out var existing)
                             && !string.Equals(existing, path, StringComparison.Ordinal))
                    {
                        Error(
                            PackageDiagnosticCodes.InvalidModelAsset,
                            bodyPath + ".assetPath",
                            "Asset paths differ only by case.",
                            diagnostics);
                    }
                    else
                    {
                        caseInsensitivePaths[path] = path;
                    }
                }
            }
        }

        foreach (var snapshotComponent in snapshot.Components)
        {
            if (!seenComponentIds.Contains(snapshotComponent.Id))
            {
                Error(
                    PackageDiagnosticCodes.InvalidModelReference,
                    "models.components",
                    "A board snapshot component has no model catalog entry.",
                    diagnostics);
            }
        }

        return diagnostics;
    }

    public static IReadOnlyList<PackageDiagnostic> ValidateAssetLinks(
        ComponentModelCatalog catalog,
        PackageManifest manifest,
        IReadOnlySet<string> actualPaths)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(actualPaths);
        var diagnostics = new List<PackageDiagnostic>();

        var manifestFiles = new Dictionary<string, PackageFileEntry>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
            manifestFiles[file.Path] = file;

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var referencedIgnoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in catalog.Components ?? Array.Empty<ComponentModelEntry>())
        {
            foreach (var body in component.Bodies ?? Array.Empty<ComponentBodyEntry>())
            {
                if (body.AssetPath is null)
                    continue;

                var path = body.AssetPath;
                if (referencedIgnoreCase.TryGetValue(path, out var existing)
                    && !string.Equals(existing, path, StringComparison.Ordinal))
                {
                    Error(
                        PackageDiagnosticCodes.InvalidModelAsset,
                        path,
                        "Asset references differ only by case.",
                        diagnostics);
                }
                else
                {
                    referencedIgnoreCase[path] = path;
                }

                referenced.Add(path);

                if (!manifestFiles.TryGetValue(path, out var entry))
                {
                    Error(
                        PackageDiagnosticCodes.InvalidModelAsset,
                        path,
                        "Asset is not listed in the manifest.",
                        diagnostics);
                    continue;
                }

                if (!actualPaths.Contains(path))
                {
                    Error(
                        PackageDiagnosticCodes.InvalidModelAsset,
                        path,
                        "Asset file is missing from the package.",
                        diagnostics);
                    continue;
                }

                if (!TryGetAssetStem(path, out var stem)
                    || !string.Equals(stem, entry.Sha256, StringComparison.Ordinal))
                {
                    Error(
                        PackageDiagnosticCodes.InvalidModelAsset,
                        path,
                        "Asset file name does not match its declared SHA-256.",
                        diagnostics);
                }
            }
        }

        foreach (var actual in actualPaths)
        {
            if (!actual.StartsWith(AssetPrefix, StringComparison.Ordinal))
                continue;

            if (!referenced.Contains(actual))
            {
                Error(
                    PackageDiagnosticCodes.InvalidModelAsset,
                    actual,
                    "Asset file is not referenced by the model catalog.",
                    diagnostics);
            }
        }

        return diagnostics;
    }

    private static bool TryValidateAssetPath(
        string path,
        ComponentModelAssetFormat? format,
        out string reason)
    {
        reason = string.Empty;
        if (!PackagePath.IsSafeRelative(path, out var pathReason))
        {
            reason = pathReason;
            return false;
        }

        if (!path.StartsWith(AssetPrefix, StringComparison.Ordinal))
        {
            reason = "asset path must be inside assets/3d/.";
            return false;
        }

        var fileName = path[AssetPrefix.Length..];
        if (fileName.Length == 0 || fileName.Contains('/', StringComparison.Ordinal))
        {
            reason = "asset path must contain exactly one file name.";
            return false;
        }

        if (!TryGetAssetStem(path, out var stem))
        {
            reason = "asset file name must be <lowercase-sha256>.<extension>.";
            return false;
        }

        if (!PackageChecksums.IsLowerHexSha256(stem))
        {
            reason = "asset file name must use a lowercase SHA-256.";
            return false;
        }

        var extension = Path.GetExtension(fileName);
        var expected = format switch
        {
            ComponentModelAssetFormat.StepAp214 => ".step",
            ComponentModelAssetFormat.ParasolidXT => ".x_t",
            _ => string.Empty,
        };

        if (format is null || !string.Equals(extension, expected, StringComparison.Ordinal))
        {
            reason = "asset file extension does not match assetFormat.";
            return false;
        }

        return true;
    }

    private static bool TryGetAssetStem(string path, out string stem)
    {
        stem = string.Empty;
        var fileName = path[(path.LastIndexOf('/') + 1)..];
        var dot = fileName.LastIndexOf('.');
        if (dot <= 0)
            return false;
        stem = fileName[..dot];
        return true;
    }

    private static void ValidateEnum<TEnum>(TEnum value, string path, List<PackageDiagnostic> diagnostics)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            Error(PackageDiagnosticCodes.InvalidModelCatalog, path, "Unknown enum value.", diagnostics);
    }

    private static void ValidateFinite(double? value, string path, List<PackageDiagnostic> diagnostics)
    {
        if (value.HasValue && !double.IsFinite(value.Value))
            Error(PackageDiagnosticCodes.InvalidModelCatalog, path, "Value must be finite.", diagnostics);
    }

    private static void ValidateLength(
        LengthValue value,
        string path,
        List<PackageDiagnostic> diagnostics)
    {
        ValidateFinite(value.Mm, path + ".mm", diagnostics);
        if (!value.HasValue)
            return;

        if (!value.Mm.HasValue)
        {
            Error(
                PackageDiagnosticCodes.InvalidModelCatalog,
                path,
                "A length with an IU value must also expose a millimetre value.",
                diagnostics);
            return;
        }

        if (!value.HasConsistentMm(UnitToleranceMm))
        {
            Error(
                PackageDiagnosticCodes.InvalidModelCatalog,
                path,
                "The millimetre value does not match the IU value within tolerance.",
                diagnostics);
        }
    }

    private static void ValidatePair(
        int? iu,
        double? mm,
        string path,
        List<PackageDiagnostic> diagnostics)
    {
        if (iu.HasValue != mm.HasValue)
        {
            Error(
                PackageDiagnosticCodes.InvalidModelCatalog,
                path,
                "IU and millimetre values must both be set or both be null.",
                diagnostics);
            return;
        }

        if (!iu.HasValue || !mm.HasValue || !double.IsFinite(mm.Value))
            return;

        if (Math.Abs(mm.Value - AltiumUnits.IuToMm(iu.Value)) > UnitToleranceMm)
        {
            Error(
                PackageDiagnosticCodes.InvalidModelCatalog,
                path,
                "Millimetre value does not match the IU value within tolerance.",
                diagnostics);
        }
    }

    private static bool TryParseMajor(string? version, out int major)
    {
        major = 0;
        if (string.IsNullOrWhiteSpace(version))
            return false;

        var parts = version.Split('.');
        return parts.Length == 3
            && int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out major)
            && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)
            && int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    private static void Error(string code, string path, string message, List<PackageDiagnostic> diagnostics) =>
        diagnostics.Add(new PackageDiagnostic(code, PackageDiagnosticSeverity.Error, path, message));
}
