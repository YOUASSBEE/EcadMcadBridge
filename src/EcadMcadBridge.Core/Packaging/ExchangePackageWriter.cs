using System.Security.Cryptography;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Serialization;
using EcadMcadBridge.Core.Validation;

namespace EcadMcadBridge.Core.Packaging;

/// <summary>Writes a complete Exchange Package into a new package directory.</summary>
public static class ExchangePackageWriter
{
    private const int CopyBufferSize = 81920;

    public static PackageWriteResult Write(
        string outputRoot,
        BoardSnapshot snapshot,
        PackageWriteOptions options) =>
        Write(outputRoot, snapshot, options, models: null);

    public static PackageWriteResult Write(
        string outputRoot,
        BoardSnapshot snapshot,
        PackageWriteOptions options,
        ComponentModelsWriteRequest? models) =>
        Write(outputRoot, snapshot, options, models, boardModel: null);

    public static PackageWriteResult Write(
        string outputRoot,
        BoardSnapshot snapshot,
        PackageWriteOptions options,
        ComponentModelsWriteRequest? models,
        BoardModelWriteRequest? boardModel)
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

        var modelsRequested = models is not null;
        var boardRequested = boardModel is not null;
        var requestedCapabilities = options.Capabilities ?? Array.Empty<string>();
        if (!modelsRequested
            && requestedCapabilities.Contains(ComponentModelCatalog.Capability, StringComparer.Ordinal))
        {
            return Fail(
                PackageDiagnosticCodes.InvalidMetadata,
                "capabilities",
                "The component-3d-models capability requires a component model catalog.");
        }

        if (!boardRequested
            && requestedCapabilities.Contains(BoardModelDescriptor.Capability, StringComparer.Ordinal))
        {
            return Fail(
                PackageDiagnosticCodes.InvalidMetadata,
                "capabilities",
                "The board-3d-model capability requires a board model.");
        }

        if (boardRequested && snapshot.Outline?.Completeness != OutlineCompleteness.Complete)
        {
            return Fail(
                PackageDiagnosticCodes.BoardOutlineNotComplete,
                "board.outline.completeness",
                "The board-3d-model capability requires a confirmed complete board outline.");
        }

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

        var snapshotBytes = BoardSnapshotJson.SerializeToUtf8(snapshot);
        var snapshotSha = PackageChecksums.ComputeSha256(snapshotBytes);
        var tempDirectory = Path.Combine(
            root,
            ".tmp-" + options.PackageId.ToString("N") + "-" + Guid.NewGuid().ToString("N"));

        PackageManifest? manifest = null;
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(tempDirectory);

            var stagedAssets = new Dictionary<string, StagedAsset>(StringComparer.Ordinal);
            if (modelsRequested)
            {
                if (!TryStageAssets(models!, tempDirectory, diagnostics, stagedAssets))
                    return PackageWriteResult.Failure(diagnostics);
                TryDelete(Path.Combine(tempDirectory, ".staging"));
            }

            ComponentModelCatalog? catalog = null;
            byte[]? catalogBytes = null;
            if (modelsRequested)
            {
                catalog = ResolveCatalog(models!, stagedAssets, diagnostics);
                var catalogValidation = ComponentModelsValidator.ValidateStructure(catalog, snapshot);
                diagnostics.AddRange(catalogValidation);
                if (diagnostics.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
                    return PackageWriteResult.Failure(diagnostics);

                var referencedAssets = catalog.Components
                    .SelectMany(component => component.Bodies)
                    .Where(body => !string.IsNullOrEmpty(body.AssetPath))
                    .Select(body => body.AssetPath!)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var staged in stagedAssets.Values)
                {
                    if (!referencedAssets.Contains(staged.RelativePath))
                    {
                        diagnostics.Add(Error(
                            PackageDiagnosticCodes.InvalidModelAsset,
                            "models.assets",
                            "An asset input is not referenced by any catalog body."));
                    }
                }

                if (diagnostics.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
                    return PackageWriteResult.Failure(diagnostics);

                catalogBytes = ComponentModelsJson.SerializeToUtf8(catalog);
            }

            BoardModelDescriptor? boardDescriptor = null;
            byte[]? boardDescriptorBytes = null;
            StagedAsset? boardAsset = null;
            if (boardRequested)
            {
                if (!TryStageBoardAsset(boardModel!, tempDirectory, diagnostics, out boardAsset))
                    return PackageWriteResult.Failure(diagnostics);

                boardDescriptor = new BoardModelDescriptor
                {
                    AssetPath = boardAsset!.RelativePath,
                    AssetFormat = ComponentModelAssetFormat.StepAp214,
                    Sha256 = boardAsset.Sha256,
                    Size = boardAsset.Size,
                    Units = "mm",
                    Frame = BoardModelFrame.Reference,
                    NominalThickness = snapshot.Properties.Thickness,
                };
                diagnostics.AddRange(BoardModelValidator.ValidateStructure(boardDescriptor, snapshot));
                if (diagnostics.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
                    return PackageWriteResult.Failure(diagnostics);

                boardDescriptorBytes = BoardModelJson.SerializeToUtf8(boardDescriptor);
            }

            var files = new List<PackageFileEntry>
            {
                new()
                {
                    Path = PackageManifest.SnapshotFileName,
                    Sha256 = snapshotSha,
                    Size = snapshotBytes.LongLength,
                },
            };

            var uniqueAssets = stagedAssets.Values
                .GroupBy(item => item.RelativePath, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
                .ToArray();

            if (catalog is not null && catalogBytes is not null)
            {
                files.Add(new PackageFileEntry
                {
                    Path = PackageManifest.ComponentModelsFileName,
                    Sha256 = PackageChecksums.ComputeSha256(catalogBytes),
                    Size = catalogBytes.LongLength,
                });

                foreach (var asset in uniqueAssets)
                {
                    files.Add(new PackageFileEntry
                    {
                        Path = asset.RelativePath,
                        Sha256 = asset.Sha256,
                        Size = asset.Size,
                    });
                }
            }

            if (boardDescriptor is not null && boardDescriptorBytes is not null && boardAsset is not null)
            {
                files.Add(new PackageFileEntry
                {
                    Path = PackageManifest.BoardModelFileName,
                    Sha256 = PackageChecksums.ComputeSha256(boardDescriptorBytes),
                    Size = boardDescriptorBytes.LongLength,
                });
                files.Add(new PackageFileEntry
                {
                    Path = boardAsset.RelativePath,
                    Sha256 = boardAsset.Sha256,
                    Size = boardAsset.Size,
                });
            }

            var capabilities = requestedCapabilities
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (modelsRequested)
                capabilities.Add(ComponentModelCatalog.Capability);
            if (boardRequested)
                capabilities.Add(BoardModelDescriptor.Capability);

            manifest = new PackageManifest
            {
                PackageId = options.PackageId,
                CreatedAtUtc = options.CreatedAtUtc,
                Producer = options.Producer,
                Source = options.Source,
                SourceUnits = "altium-iu",
                Units = "mm",
                Files = files,
                Capabilities = capabilities
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray(),
            };

            var manifestValidation = ExchangePackageValidator.ValidateManifest(manifest);
            diagnostics.AddRange(manifestValidation);
            if (manifestValidation.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
                return PackageWriteResult.Failure(diagnostics);

            var manifestBytes = PackageManifestJson.SerializeToUtf8(manifest);
            var checksums = new List<PackageChecksumEntry>
            {
                new(
                    PackageManifest.ManifestFileName,
                    PackageChecksums.ComputeSha256(manifestBytes)),
                new(PackageManifest.SnapshotFileName, snapshotSha),
            };

            if (catalogBytes is not null)
            {
                checksums.Add(new PackageChecksumEntry(
                    PackageManifest.ComponentModelsFileName,
                    PackageChecksums.ComputeSha256(catalogBytes)));

                foreach (var asset in uniqueAssets)
                    checksums.Add(new PackageChecksumEntry(asset.RelativePath, asset.Sha256));
            }

            if (boardDescriptorBytes is not null && boardAsset is not null)
            {
                checksums.Add(new PackageChecksumEntry(
                    PackageManifest.BoardModelFileName,
                    PackageChecksums.ComputeSha256(boardDescriptorBytes)));
                checksums.Add(new PackageChecksumEntry(boardAsset.RelativePath, boardAsset.Sha256));
            }

            var checksumBytes = PackageChecksums.Serialize(checksums);

            File.WriteAllBytes(
                Path.Combine(tempDirectory, PackageManifest.SnapshotFileName),
                snapshotBytes);
            if (catalogBytes is not null)
            {
                var catalogPath = Path.Combine(
                    tempDirectory,
                    PackageManifest.ComponentModelsFileName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
                File.WriteAllBytes(catalogPath, catalogBytes);
            }

            if (boardDescriptorBytes is not null)
            {
                var boardDescriptorPath = Path.Combine(
                    tempDirectory,
                    PackageManifest.BoardModelFileName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(boardDescriptorPath)!);
                File.WriteAllBytes(boardDescriptorPath, boardDescriptorBytes);
            }

            File.WriteAllBytes(
                Path.Combine(tempDirectory, PackageManifest.ManifestFileName),
                manifestBytes);
            File.WriteAllBytes(
                Path.Combine(tempDirectory, PackageManifest.ChecksumsFileName),
                checksumBytes);

            VerifyWrittenFiles(
                tempDirectory,
                manifestBytes,
                snapshotBytes,
                checksumBytes,
                snapshotSha,
                catalogBytes,
                uniqueAssets,
                boardDescriptorBytes,
                boardAsset);
            Directory.Move(tempDirectory, packageDirectory);
        }
        catch (Exception ex)
        {
            if (Directory.Exists(packageDirectory) || File.Exists(packageDirectory))
                return Fail(PackageDiagnosticCodes.ExistingPackage, packageDirectory, "Package id already exists; refusing to overwrite.");
            return Fail(
                PackageDiagnosticCodes.WriteFailed,
                "package",
                "Package write failed: " + ex.GetType().Name);
        }
        finally
        {
            TryDelete(tempDirectory);
        }

        return manifest is null
            ? Fail(PackageDiagnosticCodes.WriteFailed, "package", "Package manifest was not produced.")
            : PackageWriteResult.Success(packageDirectory, manifest, diagnostics);
    }

    private static bool TryStageAssets(
        ComponentModelsWriteRequest models,
        string tempDirectory,
        List<PackageDiagnostic> diagnostics,
        Dictionary<string, StagedAsset> stagedAssets)
    {
        var assets = models.Assets ?? Array.Empty<ComponentModelAssetSource>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            if (asset is null)
            {
                diagnostics.Add(Error(PackageDiagnosticCodes.InvalidMetadata, "models.assets", "Asset entry is null."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(asset.AssetKey))
            {
                diagnostics.Add(Error(PackageDiagnosticCodes.InvalidMetadata, "models.assets", "Asset key is required."));
                continue;
            }

            if (!keys.Add(asset.AssetKey))
            {
                diagnostics.Add(Error(PackageDiagnosticCodes.InvalidMetadata, "models.assets", $"Duplicate asset key '{asset.AssetKey}'."));
                continue;
            }

            if (!Enum.IsDefined(asset.Format))
                diagnostics.Add(Error(PackageDiagnosticCodes.InvalidModelAsset, "models.assets", "Unknown asset format."));

            if (string.IsNullOrWhiteSpace(asset.SourcePath))
            {
                diagnostics.Add(Error(PackageDiagnosticCodes.InvalidModelAsset, "models.assets", "Asset source path is required."));
                continue;
            }

            if (!File.Exists(asset.SourcePath))
            {
                diagnostics.Add(Error(PackageDiagnosticCodes.InvalidModelAsset, "models.assets", "An asset source file is missing or unreadable."));
                continue;
            }

            try
            {
                stagedAssets[asset.AssetKey] = StageAsset(asset, tempDirectory);
            }
            catch (AssetSourceChangedException)
            {
                diagnostics.Add(Error(
                    PackageDiagnosticCodes.InvalidModelAsset,
                    "models.assets",
                    "An asset source file changed while it was being copied."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Error(
                    PackageDiagnosticCodes.InvalidModelAsset,
                    "models.assets",
                    "An asset source file could not be read: " + ex.GetType().Name));
            }
        }

        return !diagnostics.Any(item => item.Severity == PackageDiagnosticSeverity.Error);
    }

    private static StagedAsset StageAsset(ComponentModelAssetSource asset, string tempDirectory)
    {
        var before = new FileInfo(asset.SourcePath);
        if (!before.Exists || (before.Attributes & FileAttributes.Directory) != 0)
            throw new AssetSourceChangedException();

        var stagingDirectory = Path.Combine(tempDirectory, ".staging");
        Directory.CreateDirectory(stagingDirectory);
        var partialPath = Path.Combine(stagingDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        string sha;
        long size;
        try
        {
            using (var input = new FileStream(
                       asset.SourcePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete,
                       CopyBufferSize,
                       FileOptions.SequentialScan))
            using (var output = new FileStream(
                       partialPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       CopyBufferSize,
                       FileOptions.SequentialScan))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[CopyBufferSize];
                size = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    hash.AppendData(buffer, 0, read);
                    size += read;
                }

                output.Flush(flushToDisk: true);
                sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            var after = new FileInfo(asset.SourcePath);
            if (!after.Exists
                || after.Length != before.Length
                || after.LastWriteTimeUtc != before.LastWriteTimeUtc
                || size != before.Length)
            {
                throw new AssetSourceChangedException();
            }

            var extension = asset.Format == ComponentModelAssetFormat.ParasolidXT ? "x_t" : "step";
            var fileName = sha + "." + extension;
            var relativePath = ComponentModelCatalog.AssetDirectory + "/" + fileName;
            var finalPath = Path.Combine(tempDirectory, ComponentModelCatalog.AssetDirectory.Replace('/', Path.DirectorySeparatorChar), fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

            if (File.Exists(finalPath))
            {
                File.Delete(partialPath);
                if (new FileInfo(finalPath).Length != size
                    || !string.Equals(PackageChecksums.ComputeFileSha256(finalPath), sha, StringComparison.Ordinal))
                {
                    throw new AssetSourceChangedException();
                }
            }
            else
            {
                File.Move(partialPath, finalPath);
            }

            return new StagedAsset(relativePath, sha, size, asset.Format);
        }
        finally
        {
            if (File.Exists(partialPath))
                TryDeleteFile(partialPath);
        }
    }

    private static bool TryStageBoardAsset(
        BoardModelWriteRequest board,
        string tempDirectory,
        List<PackageDiagnostic> diagnostics,
        out StagedAsset? staged)
    {
        staged = null;
        if (board.Format != ComponentModelAssetFormat.StepAp214)
        {
            diagnostics.Add(Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                "boardModel.assetFormat",
                "Only step-ap214 board assets are supported."));
            return false;
        }

        if (string.IsNullOrWhiteSpace(board.SourcePath) || !File.Exists(board.SourcePath))
        {
            diagnostics.Add(Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                "boardModel.assetPath",
                "Board STEP source file is missing or unreadable."));
            return false;
        }

        try
        {
            staged = StageBoardAsset(board.SourcePath, tempDirectory);
            return true;
        }
        catch (AssetSourceChangedException)
        {
            diagnostics.Add(Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                "boardModel.assetPath",
                "The board STEP source file changed while it was being copied."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(
                PackageDiagnosticCodes.InvalidBoardModelAsset,
                "boardModel.assetPath",
                "The board STEP source file could not be read: " + ex.GetType().Name));
        }

        return false;
    }

    private static StagedAsset StageBoardAsset(string sourcePath, string tempDirectory)
    {
        var before = new FileInfo(sourcePath);
        if (!before.Exists || (before.Attributes & FileAttributes.Directory) != 0)
            throw new AssetSourceChangedException();

        var stagingDirectory = Path.Combine(tempDirectory, ".staging");
        Directory.CreateDirectory(stagingDirectory);
        var partialPath = Path.Combine(stagingDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        string sha;
        long size;
        try
        {
            using (var input = new FileStream(
                       sourcePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete,
                       CopyBufferSize,
                       FileOptions.SequentialScan))
            using (var output = new FileStream(
                       partialPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       CopyBufferSize,
                       FileOptions.SequentialScan))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[CopyBufferSize];
                size = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    hash.AppendData(buffer, 0, read);
                    size += read;
                }

                output.Flush(flushToDisk: true);
                sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            if (size <= 0)
                throw new IOException("The board STEP file is empty.");

            var after = new FileInfo(sourcePath);
            if (!after.Exists
                || after.Length != before.Length
                || after.LastWriteTimeUtc != before.LastWriteTimeUtc
                || size != before.Length)
            {
                throw new AssetSourceChangedException();
            }

            var fileName = sha + ".step";
            var relativePath = BoardModelDescriptor.AssetDirectory + "/" + fileName;
            var finalPath = Path.Combine(
                tempDirectory,
                BoardModelDescriptor.AssetDirectory.Replace('/', Path.DirectorySeparatorChar),
                fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            if (File.Exists(finalPath))
            {
                File.Delete(partialPath);
            }
            else
            {
                File.Move(partialPath, finalPath);
            }

            return new StagedAsset(relativePath, sha, size, ComponentModelAssetFormat.StepAp214);
        }
        finally
        {
            if (File.Exists(partialPath))
                TryDeleteFile(partialPath);
        }
    }

    private static ComponentModelCatalog ResolveCatalog(
        ComponentModelsWriteRequest models,
        IReadOnlyDictionary<string, StagedAsset> stagedAssets,
        List<PackageDiagnostic> diagnostics)
    {
        var components = new List<ComponentModelEntry>();
        foreach (var component in models.Components ?? Array.Empty<ComponentModelWriteEntry>())
        {
            if (component is null)
                continue;

            var bodies = new List<ComponentBodyEntry>();
            foreach (var body in component.Bodies ?? Array.Empty<ComponentBodyWriteEntry>())
            {
                if (body is null)
                    continue;

                ComponentModelAssetFormat? format = null;
                string? assetPath = null;
                if (!string.IsNullOrEmpty(body.AssetKey))
                {
                    if (!stagedAssets.TryGetValue(body.AssetKey, out var staged))
                    {
                        diagnostics.Add(Error(
                            PackageDiagnosticCodes.InvalidModelAsset,
                            "models.components",
                            $"Body references an unknown asset key '{body.AssetKey}'."));
                    }
                    else
                    {
                        format = staged.Format;
                        assetPath = staged.RelativePath;
                    }
                }

                bodies.Add(new ComponentBodyEntry
                {
                    BodyOrdinal = body.BodyOrdinal,
                    Representation = body.Representation,
                    SourceOrigin = body.SourceOrigin,
                    AssetAcquisition = body.AssetAcquisition,
                    AssetFormat = format,
                    AssetPath = assetPath,
                    Origin = body.Origin ?? new ModelOrigin(),
                    State = body.State ?? new ModelState(),
                    ModelRotationDegRaw = body.ModelRotationDegRaw,
                    BodyProjection = body.BodyProjection,
                    StandoffHeight = body.StandoffHeight,
                    OverallHeight = body.OverallHeight,
                    ModelStateSource = body.ModelStateSource,
                });
            }

            components.Add(new ComponentModelEntry
            {
                ComponentId = component.ComponentId ?? string.Empty,
                Designator = component.Designator ?? string.Empty,
                Bodies = bodies,
            });
        }

        return new ComponentModelCatalog { Components = components };
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
        string snapshotSha,
        byte[]? catalogBytes,
        IReadOnlyList<StagedAsset> assets,
        byte[]? boardDescriptorBytes,
        StagedAsset? boardAsset)
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

        if (catalogBytes is not null)
        {
            var catalogPath = Path.Combine(
                tempDirectory,
                PackageManifest.ComponentModelsFileName.Replace('/', Path.DirectorySeparatorChar));
            if (!File.ReadAllBytes(catalogPath).SequenceEqual(catalogBytes))
                throw new IOException("component model catalog verification failed.");
        }

        if (boardDescriptorBytes is not null)
        {
            var boardDescriptorPath = Path.Combine(
                tempDirectory,
                PackageManifest.BoardModelFileName.Replace('/', Path.DirectorySeparatorChar));
            if (!File.ReadAllBytes(boardDescriptorPath).SequenceEqual(boardDescriptorBytes))
                throw new IOException("board model descriptor verification failed.");
        }

        if (boardAsset is not null)
        {
            var boardAssetPath = Path.Combine(
                tempDirectory,
                boardAsset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(boardAssetPath) || new FileInfo(boardAssetPath).Length != boardAsset.Size)
                throw new IOException("board asset size verification failed.");
            if (!string.Equals(
                    PackageChecksums.ComputeFileSha256(boardAssetPath),
                    boardAsset.Sha256,
                    StringComparison.Ordinal))
            {
                throw new IOException("board asset hash verification failed.");
            }
        }

        foreach (var asset in assets)
        {
            var fullPath = Path.Combine(
                tempDirectory,
                asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath) || new FileInfo(fullPath).Length != asset.Size)
                throw new IOException("asset size verification failed.");
            if (!string.Equals(PackageChecksums.ComputeFileSha256(fullPath), asset.Sha256, StringComparison.Ordinal))
                throw new IOException("asset hash verification failed.");
        }
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort cleanup of a staging file.
        }
    }

    private sealed record StagedAsset(
        string RelativePath,
        string Sha256,
        long Size,
        ComponentModelAssetFormat Format);

    private sealed class AssetSourceChangedException : Exception
    {
    }
}
