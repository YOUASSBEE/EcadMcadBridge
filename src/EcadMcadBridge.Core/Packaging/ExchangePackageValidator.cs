using System.Globalization;

namespace EcadMcadBridge.Core.Packaging;

/// <summary>Validates package metadata and safe relative paths before reading files.</summary>
public static class ExchangePackageValidator
{
    public const string SnapshotFileName = PackageManifest.SnapshotFileName;

    public static IReadOnlyList<PackageDiagnostic> ValidateManifest(PackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var diagnostics = new List<PackageDiagnostic>();

        if (!string.Equals(manifest.Schema, PackageManifest.SchemaName, StringComparison.Ordinal))
        {
            Error(
                PackageDiagnosticCodes.UnsupportedSchema,
                "manifest.schema",
                $"Unsupported package schema '{manifest.Schema}'.",
                diagnostics);
        }

        if (!TryParseMajor(manifest.SchemaVersion, out var major))
        {
            Error(
                PackageDiagnosticCodes.UnsupportedSchemaVersion,
                "manifest.schemaVersion",
                $"Unsupported package schema version '{manifest.SchemaVersion}'.",
                diagnostics);
        }
        else if (major != 1)
        {
            Error(
                PackageDiagnosticCodes.UnsupportedSchemaVersion,
                "manifest.schemaVersion",
                $"Package schema major version {major} is not supported.",
                diagnostics);
        }

        if (manifest.PackageId == Guid.Empty)
            Error(PackageDiagnosticCodes.InvalidMetadata, "manifest.packageId", "A non-empty UUID is required.", diagnostics);

        if (manifest.CreatedAtUtc == default)
            Error(PackageDiagnosticCodes.InvalidMetadata, "manifest.createdAtUtc", "A creation timestamp is required.", diagnostics);

        if (string.IsNullOrWhiteSpace(manifest.Producer.Component))
            Error(PackageDiagnosticCodes.InvalidMetadata, "manifest.producer.component", "Producer component is required.", diagnostics);

        if (string.IsNullOrWhiteSpace(manifest.Producer.Version))
            Error(PackageDiagnosticCodes.InvalidMetadata, "manifest.producer.version", "Producer version is required.", diagnostics);

        if (string.IsNullOrWhiteSpace(manifest.Source.Cad))
            Error(PackageDiagnosticCodes.InvalidMetadata, "manifest.source.cad", "Source CAD is required.", diagnostics);

        if (!PackageChecksums.IsLowerHexSha256(manifest.Source.DocumentSha256))
            Error(
                PackageDiagnosticCodes.InvalidMetadata,
                "manifest.source.documentSha256",
                "documentSha256 must be 64 lowercase hexadecimal characters.",
                diagnostics);

        if (!string.Equals(manifest.SourceUnits, "altium-iu", StringComparison.Ordinal))
        {
            Error(
                PackageDiagnosticCodes.InvalidUnits,
                "manifest.sourceUnits",
                $"Unsupported sourceUnits '{manifest.SourceUnits}'.",
                diagnostics);
        }

        if (!string.Equals(manifest.Units, "mm", StringComparison.Ordinal))
        {
            Error(
                PackageDiagnosticCodes.InvalidUnits,
                "manifest.units",
                $"Unsupported units '{manifest.Units}'.",
                diagnostics);
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        var pathsIgnoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < manifest.Files.Count; i++)
        {
            var file = manifest.Files[i];
            if (file is null)
            {
                Error(PackageDiagnosticCodes.InvalidManifest, $"manifest.files[{i}]", "File entry is null.", diagnostics);
                continue;
            }

            var pathPrefix = $"manifest.files[{i}]";
            if (!PackagePath.IsSafeRelative(file.Path, out var pathReason))
            {
                Error(PackageDiagnosticCodes.InvalidPath, pathPrefix + ".path", pathReason, diagnostics);
            }
            else
            {
                if (!paths.Add(file.Path))
                    Error(PackageDiagnosticCodes.DuplicatePath, pathPrefix + ".path", $"Duplicate path '{file.Path}'.", diagnostics);
                else if (pathsIgnoreCase.TryGetValue(file.Path, out var existing)
                         && !string.Equals(existing, file.Path, StringComparison.Ordinal))
                    Error(PackageDiagnosticCodes.DuplicatePath, pathPrefix + ".path", $"Path '{file.Path}' conflicts with '{existing}' by case.", diagnostics);
                else
                    pathsIgnoreCase[file.Path] = file.Path;

                if (string.Equals(file.Path, PackageManifest.ManifestFileName, StringComparison.Ordinal)
                    || string.Equals(file.Path, PackageManifest.ChecksumsFileName, StringComparison.Ordinal))
                {
                    Error(
                        PackageDiagnosticCodes.InvalidPath,
                        pathPrefix + ".path",
                        "manifest and checksums files must not be listed in files.",
                        diagnostics);
                }
            }

            if (!PackageChecksums.IsLowerHexSha256(file.Sha256))
                Error(PackageDiagnosticCodes.InvalidMetadata, pathPrefix + ".sha256", "SHA-256 must be 64 lowercase hexadecimal characters.", diagnostics);

            if (file.Size < 0)
                Error(PackageDiagnosticCodes.InvalidMetadata, pathPrefix + ".size", "File size must not be negative.", diagnostics);
        }

        if (manifest.Files.Count == 0)
            Error(PackageDiagnosticCodes.InvalidManifest, "manifest.files", "At least one package file is required.", diagnostics);
        else if (!manifest.Files.Any(file => string.Equals(file.Path, SnapshotFileName, StringComparison.Ordinal)))
        {
            Error(PackageDiagnosticCodes.MissingFile, "manifest.files", $"'{SnapshotFileName}' is required.", diagnostics);
        }

        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        var sorted = true;
        var previous = string.Empty;
        for (var i = 0; i < manifest.Capabilities.Count; i++)
        {
            var capability = manifest.Capabilities[i];
            if (!string.IsNullOrWhiteSpace(capability) && !capabilities.Add(capability))
            {
                Error(
                    PackageDiagnosticCodes.DuplicatePath,
                    $"manifest.capabilities[{i}]",
                    $"Duplicate capability '{capability}'.",
                    diagnostics);
            }

            if (string.IsNullOrWhiteSpace(capability))
                Error(PackageDiagnosticCodes.InvalidMetadata, $"manifest.capabilities[{i}]", "Capability must not be empty.", diagnostics);

            if (sorted && string.CompareOrdinal(previous, capability) > 0)
                sorted = false;
            previous = capability;
        }

        if (!sorted)
            Warning(
                PackageDiagnosticCodes.InvalidMetadata,
                "manifest.capabilities",
                "Capabilities should be sorted by ordinal.",
                diagnostics);

        return diagnostics;
    }

    private static bool TryParseMajor(string? version, out int major)
    {
        major = 0;
        if (string.IsNullOrWhiteSpace(version))
            return false;

        var parts = version.Split('.');
        return parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static void Error(string code, string path, string message, List<PackageDiagnostic> diagnostics) =>
        diagnostics.Add(new PackageDiagnostic(code, PackageDiagnosticSeverity.Error, path, message));

    private static void Warning(string code, string path, string message, List<PackageDiagnostic> diagnostics) =>
        diagnostics.Add(new PackageDiagnostic(code, PackageDiagnosticSeverity.Warning, path, message));
}
