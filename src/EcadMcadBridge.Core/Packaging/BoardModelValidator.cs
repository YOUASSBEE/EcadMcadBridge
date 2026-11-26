using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Core.Packaging;

/// <summary>
/// Validates the board model descriptor against the snapshot and the package file set.
/// It never loads geometry and never interprets the STEP contents.
/// </summary>
public static class BoardModelValidator
{
    public const string AssetPrefix = "assets/board/";

    private const double UnitToleranceMm = 1e-9;

    public static IReadOnlyList<PackageDiagnostic> ValidateStructure(
        BoardModelDescriptor? descriptor,
        BoardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<PackageDiagnostic>();
        if (descriptor is null)
        {
            Error(PackageDiagnosticCodes.InvalidBoardModel, "boardModel", "Board model descriptor is required.", diagnostics);
            return diagnostics;
        }

        if (!string.Equals(descriptor.Schema, BoardModelDescriptor.SchemaName, StringComparison.Ordinal))
        {
            Error(
                PackageDiagnosticCodes.UnsupportedBoardModelSchema,
                "boardModel.schema",
                "Unsupported board model schema.",
                diagnostics);
        }

        if (!TryParseMajor(descriptor.SchemaVersion, out var major) || major != 1)
        {
            Error(
                PackageDiagnosticCodes.UnsupportedBoardModelSchema,
                "boardModel.schemaVersion",
                "Board model schema major version is not supported.",
                diagnostics);
        }

        if (descriptor.AssetFormat != ComponentModelAssetFormat.StepAp214)
        {
            Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                "boardModel.assetFormat",
                "Only step-ap214 board assets are supported.",
                diagnostics);
        }

        if (!PackageChecksums.IsLowerHexSha256(descriptor.Sha256))
        {
            Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                "boardModel.sha256",
                "Board asset SHA-256 must be 64 lowercase hexadecimal characters.",
                diagnostics);
        }

        if (descriptor.Size <= 0)
        {
            Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                "boardModel.size",
                "Board asset size must be positive.",
                diagnostics);
        }

        if (!string.Equals(descriptor.Units, "mm", StringComparison.Ordinal))
        {
            Error(
                PackageDiagnosticCodes.InvalidBoardModel,
                "boardModel.units",
                "Board model units must be millimetres.",
                diagnostics);
        }

        if (!descriptor.Frame.IsReference)
        {
            Error(
                PackageDiagnosticCodes.InvalidBoardModel,
                "boardModel.frame",
                "Board model coordinate frame does not match the supported reference frame.",
                diagnostics);
        }

        if (descriptor.NominalThickness.HasValue)
        {
            var thickness = descriptor.NominalThickness;
            if (!thickness.Mm.HasValue || !double.IsFinite(thickness.Mm.Value) || thickness.Mm.Value <= 0)
            {
                Error(
                    PackageDiagnosticCodes.InvalidBoardModel,
                    "boardModel.nominalThickness",
                    "Nominal board thickness must be finite and positive when present.",
                    diagnostics);
            }
            else if (!thickness.HasConsistentMm(UnitToleranceMm))
            {
                Error(
                    PackageDiagnosticCodes.InvalidBoardModel,
                    "boardModel.nominalThickness",
                    "Nominal board thickness millimetres do not match its IU value.",
                    diagnostics);
            }
        }

        ValidateAssetPath(descriptor, diagnostics);
        return diagnostics;
    }

    public static IReadOnlyList<PackageDiagnostic> ValidateAssetLink(
        BoardModelDescriptor descriptor,
        PackageManifest manifest,
        IReadOnlySet<string> actualPaths)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(actualPaths);
        var diagnostics = new List<PackageDiagnostic>();

        var entry = manifest.Files.FirstOrDefault(
            file => string.Equals(file.Path, descriptor.AssetPath, StringComparison.Ordinal));
        if (entry is null)
        {
            Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                descriptor.AssetPath,
                "Board asset is not listed in the manifest.",
                diagnostics);
        }
        else
        {
            if (!string.Equals(entry.Sha256, descriptor.Sha256, StringComparison.Ordinal))
            {
                Error(
                    PackageDiagnosticCodes.InvalidBoardModelAsset,
                    descriptor.AssetPath,
                    "Board descriptor SHA-256 does not match the manifest entry.",
                    diagnostics);
            }

            if (entry.Size != descriptor.Size)
            {
                Error(
                    PackageDiagnosticCodes.InvalidBoardModelAsset,
                    descriptor.AssetPath,
                    "Board descriptor size does not match the manifest entry.",
                    diagnostics);
            }
        }

        if (!actualPaths.Contains(descriptor.AssetPath))
        {
            Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                descriptor.AssetPath,
                "Board asset file is missing from the package.",
                diagnostics);
        }

        foreach (var actual in actualPaths)
        {
            if (!actual.StartsWith(AssetPrefix, StringComparison.Ordinal))
                continue;

            if (!string.Equals(actual, descriptor.AssetPath, StringComparison.Ordinal))
            {
                Error(
                    PackageDiagnosticCodes.InvalidBoardModelAsset,
                    actual,
                    "Board asset file is not referenced by the board model descriptor.",
                    diagnostics);
            }
        }

        return diagnostics;
    }

    private static void ValidateAssetPath(
        BoardModelDescriptor descriptor,
        List<PackageDiagnostic> diagnostics)
    {
        var path = descriptor.AssetPath ?? string.Empty;
        if (!PackagePath.IsSafeRelative(path, out var pathReason))
        {
            Error(PackageDiagnosticCodes.InvalidBoardModelAsset, "boardModel.assetPath", pathReason, diagnostics);
            return;
        }

        if (!path.StartsWith(AssetPrefix, StringComparison.Ordinal))
        {
            Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                "boardModel.assetPath",
                "Board asset path must be inside assets/board/.",
                diagnostics);
            return;
        }

        var fileName = path[AssetPrefix.Length..];
        if (fileName.Length == 0 || fileName.Contains('/', StringComparison.Ordinal))
        {
            Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                "boardModel.assetPath",
                "Board asset path must contain exactly one file name.",
                diagnostics);
            return;
        }

        var dot = fileName.LastIndexOf('.');
        if (dot <= 0)
        {
            Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                "boardModel.assetPath",
                "Board asset file name must be <lowercase-sha256>.<extension>.",
                diagnostics);
            return;
        }

        var stem = fileName[..dot];
        if (!PackageChecksums.IsLowerHexSha256(stem))
        {
            Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                "boardModel.assetPath",
                "Board asset file name must use a lowercase SHA-256.",
                diagnostics);
            return;
        }

        if (!string.Equals(stem, descriptor.Sha256, StringComparison.Ordinal))
        {
            Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                "boardModel.assetPath",
                "Board asset file name does not match the declared SHA-256.",
                diagnostics);
        }

        var extension = Path.GetExtension(fileName);
        var expected = descriptor.AssetFormat == ComponentModelAssetFormat.StepAp214
            ? ".step"
            : string.Empty;
        if (!string.Equals(extension, expected, StringComparison.Ordinal))
        {
            Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                "boardModel.assetPath",
                "Board asset file extension does not match assetFormat.",
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
            && int.TryParse(
                parts[0],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out major)
            && int.TryParse(
                parts[1],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out _)
            && int.TryParse(
                parts[2],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out _);
    }

    private static void Error(string code, string path, string message, List<PackageDiagnostic> diagnostics) =>
        diagnostics.Add(new PackageDiagnostic(code, PackageDiagnosticSeverity.Error, path, message));
}
