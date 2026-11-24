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

        var actualPaths = Directory
            .GetFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!.Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
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

        if (!diagnostics.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
        {
            var snapshotEntry = manifest.Files.FirstOrDefault(
                file => string.Equals(file.Path, PackageManifest.SnapshotFileName, StringComparison.Ordinal));
            if (snapshotEntry is null)
            {
                diagnostics.Add(
                    Error(
                        PackageDiagnosticCodes.MissingFile,
                        PackageManifest.SnapshotFileName,
                        "Board snapshot file is missing."));
            }
            else if (PackagePath.TryGetFullPath(
                         root,
                         snapshotEntry.Path,
                         out var snapshotPath,
                         out var snapshotPathReason))
            {
                try
                {
                    var snapshotBytes = File.ReadAllBytes(snapshotPath);
                    var snapshot = BoardSnapshotJson.Deserialize(Utf8.GetString(snapshotBytes));
                    var validation = BoardSnapshotValidator.Validate(snapshot);
                    diagnostics.AddRange(validation.Diagnostics.Select(ToPackageDiagnostic));
                    if (!validation.IsValid)
                    {
                        return PackageReadResult.Failure(manifest, null, diagnostics);
                    }

                    return PackageReadResult.Success(manifest, snapshot, diagnostics);
                }
                catch (Exception ex) when (ex is JsonException or IOException or DecoderFallbackException)
                {
                    diagnostics.Add(
                        Error(
                            PackageDiagnosticCodes.CorruptSnapshot,
                            snapshotEntry.Path,
                            "Board snapshot is not valid: " + ex.GetType().Name));
                }
            }
            else
            {
                diagnostics.Add(Error(PackageDiagnosticCodes.InvalidPath, snapshotEntry.Path, snapshotPathReason));
            }
        }

        return PackageReadResult.Failure(manifest, null, diagnostics);
    }

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
