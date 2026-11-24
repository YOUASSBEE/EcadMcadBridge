using System.Text;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Serialization;
using EcadMcadBridge.Core.Validation;

namespace EcadMcadBridge.Core.Packaging;

/// <summary>Writes a complete Exchange Package into a new package directory.</summary>
public static class ExchangePackageWriter
{
    public static PackageWriteResult Write(
        string outputRoot,
        BoardSnapshot snapshot,
        PackageWriteOptions options)
    {
        var diagnostics = new List<PackageDiagnostic>();
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return Fail(PackageDiagnosticCodes.InvalidMetadata, "outputRoot", "Output root is required.");
        }

        if (snapshot is null)
            return Fail(PackageDiagnosticCodes.InvalidMetadata, "snapshot", "Board snapshot is required.");
        if (options is null)
            return Fail(PackageDiagnosticCodes.InvalidMetadata, "options", "Package options are required.");

        var validation = BoardSnapshotValidator.Validate(snapshot);
        diagnostics.AddRange(validation.Diagnostics.Select(ToPackageDiagnostic));
        if (!validation.IsValid)
            return PackageWriteResult.Failure(diagnostics);

        var metadata = ValidateOptions(options);
        diagnostics.AddRange(metadata);
        if (metadata.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
            return PackageWriteResult.Failure(diagnostics);

        var snapshotBytes = BoardSnapshotJson.SerializeToUtf8(snapshot);
        var snapshotSha = PackageChecksums.ComputeSha256(snapshotBytes);
        var capabilities = (options.Capabilities ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var manifest = new PackageManifest
        {
            PackageId = options.PackageId,
            CreatedAtUtc = options.CreatedAtUtc,
            Producer = options.Producer,
            Source = options.Source,
            SourceUnits = "altium-iu",
            Units = "mm",
            Files = new[]
            {
                new PackageFileEntry
                {
                    Path = PackageManifest.SnapshotFileName,
                    Sha256 = snapshotSha,
                    Size = snapshotBytes.LongLength,
                },
            },
            Capabilities = capabilities,
        };

        var manifestValidation = ExchangePackageValidator.ValidateManifest(manifest);
        diagnostics.AddRange(manifestValidation);
        if (manifestValidation.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
            return PackageWriteResult.Failure(diagnostics);

        var manifestBytes = PackageManifestJson.SerializeToUtf8(manifest);
        var checksums = new[]
        {
            new PackageChecksumEntry(
                PackageManifest.ManifestFileName,
                PackageChecksums.ComputeSha256(manifestBytes)),
            new PackageChecksumEntry(PackageManifest.SnapshotFileName, snapshotSha),
        };
        var checksumBytes = PackageChecksums.Serialize(checksums);

        string root;
        try
        {
            root = Path.GetFullPath(outputRoot);
        }
        catch (Exception ex)
        {
            return Fail(PackageDiagnosticCodes.InvalidMetadata, "outputRoot", "Output root is not a valid path: " + ex.GetType().Name);
        }

        var packageDirectory = Path.Combine(root, options.PackageId.ToString("D"));
        if (Directory.Exists(packageDirectory) || File.Exists(packageDirectory))
            return Fail(PackageDiagnosticCodes.ExistingPackage, packageDirectory, "Package id already exists; refusing to overwrite.");

        var tempDirectory = Path.Combine(
            root,
            ".tmp-" + options.PackageId.ToString("N") + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllBytes(
                Path.Combine(tempDirectory, PackageManifest.SnapshotFileName),
                snapshotBytes);
            File.WriteAllBytes(
                Path.Combine(tempDirectory, PackageManifest.ManifestFileName),
                manifestBytes);
            File.WriteAllBytes(
                Path.Combine(tempDirectory, PackageManifest.ChecksumsFileName),
                checksumBytes);

            VerifyWrittenFiles(tempDirectory, manifestBytes, snapshotBytes, checksumBytes, snapshotSha);
            Directory.Move(tempDirectory, packageDirectory);
        }
        catch (Exception ex)
        {
            TryDelete(tempDirectory);
            if (Directory.Exists(packageDirectory) || File.Exists(packageDirectory))
                return Fail(PackageDiagnosticCodes.ExistingPackage, packageDirectory, "Package id already exists; refusing to overwrite.");
            return Fail(
                PackageDiagnosticCodes.WriteFailed,
                "package",
                "Package write failed: " + ex.GetType().Name);
        }

        return PackageWriteResult.Success(packageDirectory, manifest, diagnostics);
    }

    private static IReadOnlyList<PackageDiagnostic> ValidateOptions(PackageWriteOptions options)
    {
        var diagnostics = new List<PackageDiagnostic>();
        if (options.PackageId == Guid.Empty)
            diagnostics.Add(Error(PackageDiagnosticCodes.InvalidMetadata, "packageId", "A non-empty UUID is required."));
        if (options.CreatedAtUtc == default)
            diagnostics.Add(Error(PackageDiagnosticCodes.InvalidMetadata, "createdAtUtc", "A creation timestamp is required."));
        if (options.Producer is null
            || string.IsNullOrWhiteSpace(options.Producer.Component)
            || string.IsNullOrWhiteSpace(options.Producer.Version))
        {
            diagnostics.Add(Error(PackageDiagnosticCodes.InvalidMetadata, "producer", "Producer component and version are required."));
        }

        if (options.Source is null
            || string.IsNullOrWhiteSpace(options.Source.Cad)
            || !PackageChecksums.IsLowerHexSha256(options.Source.DocumentSha256))
        {
            diagnostics.Add(
                Error(
                    PackageDiagnosticCodes.InvalidMetadata,
                    "source",
                    "Source CAD and a 64-character lowercase documentSha256 are required."));
        }

        if (options.Capabilities is null)
        {
            diagnostics.Add(Error(PackageDiagnosticCodes.InvalidMetadata, "capabilities", "Capabilities must not be null."));
        }
        else
        {
            for (var i = 0; i < options.Capabilities.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(options.Capabilities[i]))
                    diagnostics.Add(Error(PackageDiagnosticCodes.InvalidMetadata, $"capabilities[{i}]", "Capability must not be empty."));
            }
        }

        return diagnostics;
    }

    private static void VerifyWrittenFiles(
        string tempDirectory,
        byte[] manifestBytes,
        byte[] snapshotBytes,
        byte[] checksumBytes,
        string snapshotSha)
    {
        var manifestPath = Path.Combine(tempDirectory, PackageManifest.ManifestFileName);
        var snapshotPath = Path.Combine(tempDirectory, PackageManifest.SnapshotFileName);
        var checksumPath = Path.Combine(tempDirectory, PackageManifest.ChecksumsFileName);
        if (!File.ReadAllBytes(manifestPath).SequenceEqual(manifestBytes))
            throw new IOException("manifest verification failed.");
        if (!File.ReadAllBytes(snapshotPath).SequenceEqual(snapshotBytes)
            || !string.Equals(PackageChecksums.ComputeFileSha256(snapshotPath), snapshotSha, StringComparison.Ordinal))
        {
            throw new IOException("snapshot verification failed.");
        }

        if (!File.ReadAllBytes(checksumPath).SequenceEqual(checksumBytes))
            throw new IOException("checksums verification failed.");
    }

    private static PackageDiagnostic ToPackageDiagnostic(BoardDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            diagnostic.Severity == BoardDiagnosticSeverity.Error
                ? PackageDiagnosticSeverity.Error
                : PackageDiagnosticSeverity.Warning,
            diagnostic.Path,
            diagnostic.Message);

    private static PackageWriteResult Fail(string code, string path, string message) =>
        PackageWriteResult.Failure(new[] { Error(code, path, message) });

    private static PackageDiagnostic Error(string code, string path, string message) =>
        new(code, PackageDiagnosticSeverity.Error, path, message);

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup; the package itself was never published.
        }
    }
}
