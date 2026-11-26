using System.Text;
using System.Text.Json;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Serialization;
using EcadMcadBridge.Core.Validation;

namespace EcadMcadBridge.Core.Packaging;

/// <summary>Reads and validates an Exchange Package without modifying it.</summary>
public static class ExchangePackageReader
{
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static PackageReadResult Read(string packageDirectory)
    {
        var diagnostics = new List<PackageDiagnostic>();
        if (string.IsNullOrWhiteSpace(packageDirectory))
            return Fail(PackageDiagnosticCodes.MissingPackage, "package", "Package directory is required.");

        string root;
        try
        {
            root = Path.GetFullPath(packageDirectory);
        }
        catch (Exception ex)
        {
            return Fail(
                PackageDiagnosticCodes.MissingPackage,
                "package",
                "Package directory is not a valid path: " + ex.GetType().Name);
        }

        if (!Directory.Exists(root))
            return Fail(PackageDiagnosticCodes.MissingPackage, "package", "Package directory does not exist.");

        var actualPaths = CollectRegularFiles(root, diagnostics);
        if (diagnostics.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
            return PackageReadResult.Failure(null, null, diagnostics);

        var manifestPath = Path.Combine(root, PackageManifest.ManifestFileName);
        if (!File.Exists(manifestPath))
            return Fail(PackageDiagnosticCodes.MissingManifest, PackageManifest.ManifestFileName, "Manifest is missing.");

        PackageManifest manifest;
        try
        {
            manifest = PackageManifestJson.Deserialize(File.ReadAllText(manifestPath, Utf8));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or IOException or DecoderFallbackException)
        {
            return Fail(
                PackageDiagnosticCodes.InvalidManifest,
                PackageManifest.ManifestFileName,
                "Manifest is not valid: " + ex.GetType().Name);
        }

        var manifestDiagnostics = ExchangePackageValidator.ValidateManifest(manifest);
        diagnostics.AddRange(manifestDiagnostics);
        if (manifestDiagnostics.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
            return PackageReadResult.Failure(manifest, null, diagnostics);

        var checksumPath = Path.Combine(root, PackageManifest.ChecksumsFileName);
        if (!File.Exists(checksumPath))
            return PackageReadResult.Failure(
                manifest,
                null,
                Add(diagnostics, Error(PackageDiagnosticCodes.MissingChecksums, PackageManifest.ChecksumsFileName, "Checksums file is missing.")));

        IReadOnlyList<PackageChecksumEntry> checksums;
        try
        {
            if (!PackageChecksums.TryParse(File.ReadAllText(checksumPath, Utf8), out checksums, out var checksumError))
                return PackageReadResult.Failure(
                    manifest,
                    null,
                    Add(
                        diagnostics,
                        Error(
                            PackageDiagnosticCodes.InvalidChecksumFormat,
                            PackageManifest.ChecksumsFileName,
                            checksumError)));
        }
        catch (Exception ex) when (ex is IOException or DecoderFallbackException)
        {
            return PackageReadResult.Failure(
                manifest,
                null,
                Add(
                    diagnostics,
                    Error(
                        PackageDiagnosticCodes.InvalidChecksumFormat,
                        PackageManifest.ChecksumsFileName,
                        "Checksums file is not valid: " + ex.GetType().Name)));
        }

        var expectedPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            PackageManifest.ManifestFileName,
        };
        foreach (var file in manifest.Files)
            expectedPaths.Add(file.Path);

        var checksumPaths = new HashSet<string>(checksums.Select(item => item.Path), StringComparer.Ordinal);
        if (!checksumPaths.SetEquals(expectedPaths))
        {
            diagnostics.Add(
                Error(
                    PackageDiagnosticCodes.ChecksumMismatch,
                    PackageManifest.ChecksumsFileName,
                    "Checksums do not cover exactly the manifest files."));
        }

        actualPaths.Remove(PackageManifest.ChecksumsFileName);

        foreach (var extra in actualPaths.Where(path => !expectedPaths.Contains(path)))
        {
            diagnostics.Add(
                Error(
                    PackageDiagnosticCodes.ExtraFile,
                    extra,
                    "Package contains a file not listed in the manifest/checksums."));
        }

        foreach (var missing in expectedPaths.Where(path => !actualPaths.Contains(path)))
        {
            diagnostics.Add(
                Error(
                    PackageDiagnosticCodes.MissingFile,
                    missing,
                    "File listed in the manifest/checksums is missing."));
        }

        var modelsCapability = manifest.Capabilities.Contains(
            PackageManifest.ComponentModelsCapability,
            StringComparer.Ordinal);
        var modelsListed = manifest.Files.Any(
            file => string.Equals(file.Path, PackageManifest.ComponentModelsFileName, StringComparison.Ordinal));
        var modelsOnDisk = actualPaths.Contains(PackageManifest.ComponentModelsFileName);
        if (modelsCapability != modelsListed)
        {
            diagnostics.Add(
                Error(
                    PackageDiagnosticCodes.ModelCatalogCapabilityMismatch,
                    PackageManifest.ComponentModelsFileName,
                    "The component-3d-models capability and models/models.json must be present together."));
        }
        else if (!modelsListed && modelsOnDisk)
        {
            diagnostics.Add(
                Error(
                    PackageDiagnosticCodes.ModelCatalogCapabilityMismatch,
                    PackageManifest.ComponentModelsFileName,
                    "models/models.json requires the component-3d-models capability."));
        }

        var boardCapability = manifest.Capabilities.Contains(
            PackageManifest.BoardModelCapability,
            StringComparer.Ordinal);
        var boardListed = manifest.Files.Any(
            file => string.Equals(file.Path, PackageManifest.BoardModelFileName, StringComparison.Ordinal));
        var boardOnDisk = actualPaths.Contains(PackageManifest.BoardModelFileName);
        if (boardCapability != boardListed)
        {
            diagnostics.Add(
                Error(
                    PackageDiagnosticCodes.BoardModelCapabilityMismatch,
                    PackageManifest.BoardModelFileName,
                    "The board-3d-model capability and board.model.json must be present together."));
        }
        else if (!boardListed && boardOnDisk)
        {
            diagnostics.Add(
                Error(
                    PackageDiagnosticCodes.BoardModelCapabilityMismatch,
                    PackageManifest.BoardModelFileName,
                    "board.model.json requires the board-3d-model capability."));
        }

        foreach (var checksum in checksums)
        {
            if (!PackagePath.TryGetFullPath(root, checksum.Path, out var fullPath, out var pathReason))
            {
                diagnostics.Add(Error(PackageDiagnosticCodes.InvalidPath, checksum.Path, pathReason));
                continue;
            }

            if (string.Equals(checksum.Path, PackageManifest.ChecksumsFileName, StringComparison.Ordinal))
            {
                diagnostics.Add(
                    Error(
                        PackageDiagnosticCodes.InvalidChecksumFormat,
                        checksum.Path,
                        "checksums.sha256 must not cover itself."));
                continue;
            }

            if (!File.Exists(fullPath))
            {
                diagnostics.Add(Error(PackageDiagnosticCodes.MissingFile, checksum.Path, "File is missing."));
                continue;
            }

            var actualHash = PackageChecksums.ComputeFileSha256(fullPath);
            if (!string.Equals(actualHash, checksum.Sha256, StringComparison.Ordinal))
            {
                diagnostics.Add(
                    Error(
                        PackageDiagnosticCodes.ChecksumMismatch,
                        checksum.Path,
                        "SHA-256 in checksums.sha256 does not match the file."));
            }
        }

        foreach (var file in manifest.Files)
        {
            if (!PackagePath.TryGetFullPath(root, file.Path, out var fullPath, out var pathReason))
            {
                diagnostics.Add(Error(PackageDiagnosticCodes.InvalidPath, file.Path, pathReason));
                continue;
            }

            if (!File.Exists(fullPath))
            {
                diagnostics.Add(Error(PackageDiagnosticCodes.MissingFile, file.Path, "File is missing."));
                continue;
            }

            var info = new FileInfo(fullPath);
            if (info.Length != file.Size)
            {
                diagnostics.Add(
                    Error(
                        PackageDiagnosticCodes.SizeMismatch,
                        file.Path,
                        $"Expected {file.Size} bytes, found {info.Length}."));
            }

            var actualHash = PackageChecksums.ComputeFileSha256(fullPath);
            if (!string.Equals(actualHash, file.Sha256, StringComparison.Ordinal))
            {
                diagnostics.Add(
                    Error(
                        PackageDiagnosticCodes.ChecksumMismatch,
                        file.Path,
                        "SHA-256 in manifest does not match the file."));
            }
        }

        if (diagnostics.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
            return PackageReadResult.Failure(manifest, null, diagnostics);

        var snapshotEntry = manifest.Files.FirstOrDefault(
            file => string.Equals(file.Path, PackageManifest.SnapshotFileName, StringComparison.Ordinal));
        if (snapshotEntry is null)
        {
            return PackageReadResult.Failure(
                manifest,
                null,
                Add(
                    diagnostics,
                    Error(
                        PackageDiagnosticCodes.MissingFile,
                        PackageManifest.SnapshotFileName,
                        "Board snapshot file is missing.")));
        }

        BoardSnapshot snapshot;
        if (!PackagePath.TryGetFullPath(root, snapshotEntry.Path, out var snapshotPath, out var snapshotPathReason))
        {
            return PackageReadResult.Failure(
                manifest,
                null,
                Add(diagnostics, Error(PackageDiagnosticCodes.InvalidPath, snapshotEntry.Path, snapshotPathReason)));
        }

        try
        {
            var snapshotBytes = File.ReadAllBytes(snapshotPath);
            snapshot = BoardSnapshotJson.Deserialize(Utf8.GetString(snapshotBytes));
            var validation = BoardSnapshotValidator.Validate(snapshot);
            diagnostics.AddRange(validation.Diagnostics.Select(ToPackageDiagnostic));
            if (!validation.IsValid)
                return PackageReadResult.Failure(manifest, null, diagnostics);

            if (boardCapability && snapshot.Outline?.Completeness != OutlineCompleteness.Complete)
            {
                return PackageReadResult.Failure(
                    manifest,
                    null,
                    Add(
                        diagnostics,
                        Error(
                            PackageDiagnosticCodes.BoardOutlineNotComplete,
                            "board.outline.completeness",
                            "The board-3d-model capability requires a confirmed complete board outline.")));
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or DecoderFallbackException)
        {
            return PackageReadResult.Failure(
                manifest,
                null,
                Add(
                    diagnostics,
                    Error(
                        PackageDiagnosticCodes.CorruptSnapshot,
                        snapshotEntry.Path,
                        "Board snapshot is not valid: " + ex.GetType().Name)));
        }

        ComponentModelCatalog? models = null;
        if (modelsListed)
        {
            if (!PackagePath.TryGetFullPath(root, PackageManifest.ComponentModelsFileName, out var modelsPath, out var modelsPathReason))
            {
                return PackageReadResult.Failure(
                    manifest,
                    null,
                    Add(
                        diagnostics,
                        Error(
                            PackageDiagnosticCodes.InvalidModelAsset,
                            PackageManifest.ComponentModelsFileName,
                            modelsPathReason)));
            }

            try
            {
                models = ComponentModelsJson.Deserialize(File.ReadAllText(modelsPath, Utf8));
            }
            catch (Exception ex) when (ex is JsonException or IOException or DecoderFallbackException)
            {
                return PackageReadResult.Failure(
                    manifest,
                    null,
                    Add(
                        diagnostics,
                        Error(
                            PackageDiagnosticCodes.InvalidModelCatalog,
                            PackageManifest.ComponentModelsFileName,
                            "Component model catalog is not valid: " + ex.GetType().Name)));
            }

            diagnostics.AddRange(ComponentModelsValidator.ValidateStructure(models, snapshot));
            diagnostics.AddRange(ComponentModelsValidator.ValidateAssetLinks(models, manifest, actualPaths));
            if (diagnostics.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
                return PackageReadResult.Failure(manifest, null, diagnostics);
        }

        BoardModelDescriptor? boardModel = null;
        if (boardListed)
        {
            if (!PackagePath.TryGetFullPath(
                    root,
                    PackageManifest.BoardModelFileName,
                    out var boardModelPath,
                    out var boardModelPathReason))
            {
                return PackageReadResult.Failure(
                    manifest,
                    null,
                    Add(
                        diagnostics,
                        Error(
                            PackageDiagnosticCodes.InvalidBoardModelAsset,
                            PackageManifest.BoardModelFileName,
                            boardModelPathReason)));
            }

            try
            {
                boardModel = BoardModelJson.Deserialize(File.ReadAllText(boardModelPath, Utf8));
            }
            catch (Exception ex) when (ex is JsonException or IOException or DecoderFallbackException)
            {
                return PackageReadResult.Failure(
                    manifest,
                    null,
                    Add(
                        diagnostics,
                        Error(
                            PackageDiagnosticCodes.InvalidBoardModel,
                            PackageManifest.BoardModelFileName,
                            "Board model descriptor is not valid: " + ex.GetType().Name)));
            }

            diagnostics.AddRange(BoardModelValidator.ValidateStructure(boardModel, snapshot));
            diagnostics.AddRange(BoardModelValidator.ValidateAssetLink(boardModel, manifest, actualPaths));
            if (diagnostics.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
                return PackageReadResult.Failure(manifest, null, diagnostics);
        }

        return PackageReadResult.Success(manifest, snapshot, models, boardModel, diagnostics);
    }

    private static HashSet<string> CollectRegularFiles(
        string root,
        List<PackageDiagnostic> diagnostics)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var visitedDirectories = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (!visitedDirectories.Add(directory))
                continue;

            IReadOnlyList<string> entries;
            try
            {
                entries = Directory.GetFileSystemEntries(directory);
            }
            catch (Exception ex)
            {
                diagnostics.Add(
                    Error(
                        PackageDiagnosticCodes.InvalidPath,
                        RelativePath(root, directory),
                        "Directory cannot be enumerated: " + ex.GetType().Name));
                continue;
            }

            foreach (var entry in entries)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(
                        Error(
                            PackageDiagnosticCodes.InvalidPath,
                            RelativePath(root, entry),
                            "File system entry cannot be inspected: " + ex.GetType().Name));
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    diagnostics.Add(
                        Error(
                            PackageDiagnosticCodes.UnsupportedLink,
                            RelativePath(root, entry),
                            "Reparse points and symbolic links are not allowed in a package."));
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                var relative = RelativePath(root, entry);
                if (!PackagePath.IsSafeRelative(relative, out var pathReason))
                {
                    diagnostics.Add(Error(PackageDiagnosticCodes.InvalidPath, relative, pathReason));
                    continue;
                }

                result.Add(relative);
            }
        }

        return result;
    }

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static PackageDiagnostic ToPackageDiagnostic(BoardDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            diagnostic.Severity == BoardDiagnosticSeverity.Error
                ? PackageDiagnosticSeverity.Error
                : PackageDiagnosticSeverity.Warning,
            diagnostic.Path,
            diagnostic.Message);

    private static PackageDiagnostic Error(string code, string path, string message) =>
        new(code, PackageDiagnosticSeverity.Error, path, message);

    private static IReadOnlyList<PackageDiagnostic> Add(
        List<PackageDiagnostic> diagnostics,
        PackageDiagnostic diagnostic)
    {
        diagnostics.Add(diagnostic);
        return diagnostics;
    }

    private static PackageReadResult Fail(string code, string path, string message) =>
        PackageReadResult.Failure(
            null,
            null,
            new[] { Error(code, path, message) });
}
