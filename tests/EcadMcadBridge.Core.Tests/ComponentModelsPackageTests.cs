using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using Xunit;

namespace EcadMcadBridge.Core.Tests;

public sealed class ComponentModelsPackageTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PackageId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string DocumentSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void WriterAndReader_RoundTripCatalog()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();

        Assert.True(written.IsSuccess, Describe(written.Errors));
        var packageDirectory = written.PackageDirectory!;
        var manifest = PackageManifestJson.Deserialize(
            File.ReadAllText(Path.Combine(packageDirectory, PackageManifest.ManifestFileName)));
        Assert.Contains(PackageManifest.ComponentModelsCapability, manifest.Capabilities);
        Assert.Equal(PackageManifest.SchemaVersionValue, manifest.SchemaVersion);
        Assert.Contains(manifest.Files, file => file.Path == PackageManifest.ComponentModelsFileName);
        Assert.Equal(4, manifest.Files.Count);

        var read = ExchangePackageReader.Read(packageDirectory);

        Assert.True(read.IsSuccess, Describe(read.Errors));
        Assert.NotNull(read.Models);
        var components = read.Models!.Components.OrderBy(item => item.ComponentId, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, components.Length);

        var first = components[0];
        Assert.Equal("component-1", first.ComponentId);
        Assert.Equal("R1", first.Designator);
        Assert.Equal(2, first.Bodies.Count);

        var generic = first.Bodies[0];
        Assert.Equal(ComponentModelRepresentation.Generic, generic.Representation);
        Assert.Equal(ComponentModelSourceOrigin.Embedded, generic.SourceOrigin);
        Assert.Equal(ComponentModelAssetAcquisition.SdkExport, generic.AssetAcquisition);
        Assert.Equal(ComponentModelAssetFormat.StepAp214, generic.AssetFormat);
        Assert.StartsWith("assets/3d/", generic.AssetPath, StringComparison.Ordinal);
        Assert.EndsWith(".step", generic.AssetPath, StringComparison.Ordinal);
        Assert.Equal(360d, generic.State.RotationZDegRaw);
        Assert.Equal(1_234_567, generic.Origin.XIu);
        Assert.Equal(AltiumUnits.IuToMm(1_234_567), generic.Origin.XMm);
        Assert.Equal(393_700, generic.State.DzIu);
        Assert.Equal(AltiumUnits.IuToMm(393_700), generic.State.DzMm);
        Assert.Null(generic.Origin.ZIu);
        Assert.Null(generic.Origin.ZMm);

        var unknown = first.Bodies[1];
        Assert.Equal(ComponentModelRepresentation.Unknown, unknown.Representation);
        Assert.Equal(ComponentModelSourceOrigin.Unknown, unknown.SourceOrigin);
        Assert.Equal(ComponentModelAssetAcquisition.NotAcquired, unknown.AssetAcquisition);
        Assert.Null(unknown.AssetFormat);
        Assert.Null(unknown.AssetPath);
        Assert.Null(unknown.Origin.XIu);
        Assert.Null(unknown.Origin.YMm);
        Assert.Null(unknown.State.DzIu);
        Assert.Null(unknown.ModelRotationDegRaw);

        var second = components[1];
        Assert.Equal("component-2", second.ComponentId);
        var native = Assert.Single(second.Bodies);
        Assert.Equal(ComponentModelRepresentation.Native, native.Representation);
        Assert.Equal(ComponentModelSourceOrigin.Linked, native.SourceOrigin);
        Assert.Equal(ComponentModelAssetAcquisition.SourceCopyVerified, native.AssetAcquisition);
        Assert.Equal(ComponentModelAssetFormat.ParasolidXT, native.AssetFormat);
        Assert.EndsWith(".x_t", native.AssetPath, StringComparison.Ordinal);

        var catalogBytes = File.ReadAllBytes(
            Path.Combine(packageDirectory, PackageManifest.ComponentModelsFileName.Replace('/', Path.DirectorySeparatorChar)));
        var expectedCatalog = ComponentModelsJson.Deserialize(Encoding.UTF8.GetString(catalogBytes));
        Assert.Equal(
            ComponentModelsJson.SerializeToUtf8(expectedCatalog),
            ComponentModelsJson.SerializeToUtf8(read.Models));
    }

    [Fact]
    public void Reader_ReturnsNullCatalogForPackageWithoutModels()
    {
        using var temp = new TempDirectory();
        var written = ExchangePackageWriter.Write(
            temp.Path,
            BoardSnapshotFixture.CreateValid(),
            Options());

        Assert.True(written.IsSuccess, Describe(written.Errors));
        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.True(read.IsSuccess, Describe(read.Errors));
        Assert.Null(read.Models);
        Assert.DoesNotContain(
            PackageManifest.ComponentModelsCapability,
            read.Manifest!.Capabilities);
    }

    [Fact]
    public void Writer_RejectsCapabilityWithoutCatalog()
    {
        using var temp = new TempDirectory();
        var options = new PackageWriteOptions
        {
            PackageId = PackageId,
            CreatedAtUtc = Created,
            Producer = Producer(),
            Source = new PackageSource { Cad = "altium", DocumentSha256 = DocumentSha },
            Capabilities = new[] { PackageManifest.ComponentModelsCapability },
        };

        var result = ExchangePackageWriter.Write(temp.Path, BoardSnapshotFixture.CreateValid(), options);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidMetadata);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, PackageId.ToString("D"))));
    }

    [Fact]
    public void WriterAndReader_ShareAssetAcrossBodies()
    {
        using var scenario = new Scenario();
        var request = new ComponentModelsWriteRequest
        {
            Assets = new[]
            {
                new ComponentModelAssetSource
                {
                    AssetKey = "shared",
                    SourcePath = scenario.StepPath,
                    Format = ComponentModelAssetFormat.StepAp214,
                },
            },
            Components = new[]
            {
                new ComponentModelWriteEntry
                {
                    ComponentId = "component-1",
                    Designator = "R1",
                    Bodies = new[]
                    {
                        new ComponentBodyWriteEntry
                        {
                            BodyOrdinal = 0,
                            Representation = ComponentModelRepresentation.Generic,
                            SourceOrigin = ComponentModelSourceOrigin.Embedded,
                            AssetAcquisition = ComponentModelAssetAcquisition.SdkExport,
                            AssetKey = "shared",
                        },
                    },
                },
                new ComponentModelWriteEntry
                {
                    ComponentId = "component-2",
                    Designator = "C1",
                    Bodies = new[]
                    {
                        new ComponentBodyWriteEntry
                        {
                            BodyOrdinal = 0,
                            Representation = ComponentModelRepresentation.Generic,
                            SourceOrigin = ComponentModelSourceOrigin.Embedded,
                            AssetAcquisition = ComponentModelAssetAcquisition.SdkExport,
                            AssetKey = "shared",
                        },
                    },
                },
            },
        };

        var written = scenario.Write(request);

        Assert.True(written.IsSuccess, Describe(written.Errors));
        var manifest = PackageManifestJson.Deserialize(
            File.ReadAllText(Path.Combine(written.PackageDirectory!, PackageManifest.ManifestFileName)));
        Assert.Equal(3, manifest.Files.Count);
        var read = ExchangePackageReader.Read(written.PackageDirectory!);
        Assert.True(read.IsSuccess, Describe(read.Errors));
        var firstPath = read.Models!.Components[0].Bodies[0].AssetPath;
        var secondPath = read.Models.Components[1].Bodies[0].AssetPath;
        Assert.Equal(firstPath, secondPath);
        Assert.Single(Directory.GetFiles(Path.Combine(written.PackageDirectory!, "assets", "3d")));
    }

    [Fact]
    public void Reader_RejectsUnknownEnumValue()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        EditCatalog(written.PackageDirectory!, root =>
        {
            root["components"]![0]!["bodies"]![0]!["representation"] = "bogus";
        });

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelCatalog);
    }

    [Fact]
    public void Reader_RejectsNonFiniteNumber()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var text = ReadCatalogText(written.PackageDirectory!);
        text = text.Replace(
            "\"rotationZDegRaw\": 360",
            "\"rotationZDegRaw\": 1e999",
            StringComparison.Ordinal);
        WriteCatalogText(written.PackageDirectory!, text);

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelCatalog);
    }

    [Fact]
    public void Reader_RejectsUnpairedIuMmValue()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        EditCatalog(written.PackageDirectory!, root =>
        {
            root["components"]![0]!["bodies"]![0]!["sdkOrigin"]!["zMm"] = 1.0;
        });

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelCatalog);
    }

    [Fact]
    public void Writer_RejectsMissingAssetSource()
    {
        using var scenario = new Scenario();
        var request = scenario.Request();
        request = new ComponentModelsWriteRequest
        {
            Components = request.Components,
            Assets = new[]
            {
                new ComponentModelAssetSource
                {
                    AssetKey = "step-a",
                    SourcePath = Path.Combine(scenario.Temp.Path, "missing.step"),
                    Format = ComponentModelAssetFormat.StepAp214,
                },
                new ComponentModelAssetSource
                {
                    AssetKey = "xt-a",
                    SourcePath = scenario.XtPath,
                    Format = ComponentModelAssetFormat.ParasolidXT,
                },
            },
        };

        var result = scenario.Write(request);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelAsset);
        Assert.False(Directory.Exists(Path.Combine(scenario.Temp.Path, PackageId.ToString("D"))));
        Assert.Empty(TemporaryDirectories(scenario.Temp.Path));
    }

    [Fact]
    public void Writer_RejectsUnreferencedAsset()
    {
        using var scenario = new Scenario();
        var request = scenario.Request(includeXtAsset: true, referenceXtAsset: false);

        var result = scenario.Write(request);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelAsset);
        Assert.Empty(TemporaryDirectories(scenario.Temp.Path));
    }

    [Fact]
    public void Writer_RejectsCatalogMissingComponent()
    {
        using var scenario = new Scenario();
        var request = scenario.Request();
        request = new ComponentModelsWriteRequest
        {
            Components = request.Components.Where(item => item.ComponentId == "component-1").ToArray(),
            Assets = request.Assets,
        };

        var result = scenario.Write(request);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelReference);
        Assert.Empty(TemporaryDirectories(scenario.Temp.Path));
    }

    [Fact]
    public void Writer_DoesNotOverwriteExistingPackage()
    {
        using var scenario = new Scenario();
        var first = scenario.Write();
        Assert.True(first.IsSuccess, Describe(first.Errors));
        var manifestPath = Path.Combine(first.PackageDirectory!, PackageManifest.ManifestFileName);
        var original = File.ReadAllBytes(manifestPath);

        var second = scenario.Write();

        Assert.False(second.IsSuccess);
        Assert.Contains(second.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.ExistingPackage);
        Assert.Equal(original, File.ReadAllBytes(manifestPath));
    }

    [Fact]
    public void Writer_IsDeterministicWithModels()
    {
        using var scenario = new Scenario();
        using var first = new TempDirectory();
        using var second = new TempDirectory();

        var firstResult = scenario.Write(scenario.Request(), first.Path);
        var secondResult = scenario.Write(scenario.Request(), second.Path);

        Assert.True(firstResult.IsSuccess, Describe(firstResult.Errors));
        Assert.True(secondResult.IsSuccess, Describe(secondResult.Errors));
        foreach (var name in new[]
                 {
                     PackageManifest.ManifestFileName,
                     PackageManifest.SnapshotFileName,
                     PackageManifest.ChecksumsFileName,
                     PackageManifest.ComponentModelsFileName,
                 })
        {
            var firstPath = Path.Combine(firstResult.PackageDirectory!, name.Replace('/', Path.DirectorySeparatorChar));
            var secondPath = Path.Combine(secondResult.PackageDirectory!, name.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
        }

        foreach (var asset in Directory.GetFiles(
                     Path.Combine(firstResult.PackageDirectory!, "assets", "3d")))
        {
            var name = Path.GetFileName(asset);
            Assert.True(File.Exists(Path.Combine(secondResult.PackageDirectory!, "assets", "3d", name)));
        }
    }

    [Fact]
    public void WriterAndReader_SupportLargeStreamedAsset()
    {
        using var scenario = new Scenario();
        var largePath = Path.Combine(scenario.Temp.Path, "inputs", "large.step");
        var buffer = new byte[81920];
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = (byte)(i % 251);
        using (var stream = new FileStream(largePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            for (var i = 0; i < 100; i++)
                stream.Write(buffer);
        }

        var size = new FileInfo(largePath).Length;
        var request = scenario.RequestWithAssets(("step-a", largePath, ComponentModelAssetFormat.StepAp214));
        var written = scenario.Write(request);

        Assert.True(written.IsSuccess, Describe(written.Errors));
        var read = ExchangePackageReader.Read(written.PackageDirectory!);
        Assert.True(read.IsSuccess, Describe(read.Errors));
        var assetPath = Assert.Single(read.Models!.Components[0].Bodies, body => body.AssetPath is not null).AssetPath!;
        var fullPath = Path.Combine(written.PackageDirectory!, assetPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(size, new FileInfo(fullPath).Length);
        Assert.Equal(Sha(File.ReadAllBytes(fullPath)), Path.GetFileNameWithoutExtension(fullPath));
    }

    [Fact]
    public async Task Writer_RejectsAssetChangedDuringCopy()
    {
        using var scenario = new Scenario();
        var source = Path.Combine(scenario.Temp.Path, "inputs", "changing.step");
        var buffer = new byte[81920];
        using (var stream = new FileStream(source, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            for (var i = 0; i < 200; i++)
                stream.Write(buffer);
        }

        var request = scenario.RequestWithAssets(("step-a", source, ComponentModelAssetFormat.StepAp214));
        var appendTask = Task.Run(() =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (Directory.Exists(scenario.Temp.Path)
                        && Directory.GetDirectories(scenario.Temp.Path, ".staging", SearchOption.AllDirectories).Length > 0)
                    {
                        File.AppendAllText(source, "x");
                        return;
                    }
                }
                catch (IOException)
                {
                    // Retry until the deadline; the writer is still creating files.
                }

                Thread.Sleep(1);
            }
        });

        var result = scenario.Write(request);

        await appendTask.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelAsset);
        Assert.False(Directory.Exists(Path.Combine(scenario.Temp.Path, PackageId.ToString("D"))));
        Assert.Empty(TemporaryDirectories(scenario.Temp.Path));
    }

    [Fact]
    public void WriterAndReader_SupportEmptyBodies()
    {
        using var scenario = new Scenario();
        var written = scenario.Write(scenario.Request(includeXtAsset: false, referenceXtAsset: false));
        Assert.True(written.IsSuccess, Describe(written.Errors));

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.True(read.IsSuccess, Describe(read.Errors));
        var second = read.Models!.Components.Single(item => item.ComponentId == "component-2");
        Assert.Empty(second.Bodies);
    }

    [Fact]
    public void Reader_RejectsMissingCatalogFile()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        File.Delete(Path.Combine(
            written.PackageDirectory!,
            PackageManifest.ComponentModelsFileName.Replace('/', Path.DirectorySeparatorChar)));

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.MissingFile);
    }

    [Fact]
    public void Reader_RejectsCatalogWithoutCapability()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        RemoveCapability(written.PackageDirectory!);

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.ModelCatalogCapabilityMismatch);
    }

    [Fact]
    public void Reader_RejectsCapabilityWithoutCatalog()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        RemoveCatalogEntry(written.PackageDirectory!);

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.ModelCatalogCapabilityMismatch);
    }

    [Fact]
    public void Reader_RejectsMissingAssetFile()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var asset = AssetPaths(written.PackageDirectory!)[0];
        File.Delete(Path.Combine(written.PackageDirectory!, asset.Replace('/', Path.DirectorySeparatorChar)));

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.MissingFile);
    }

    [Fact]
    public void Reader_RejectsModifiedAsset()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var asset = AssetPaths(written.PackageDirectory!)[0];
        File.AppendAllText(Path.Combine(written.PackageDirectory!, asset.Replace('/', Path.DirectorySeparatorChar)), "x");

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.ChecksumMismatch);
    }

    [Fact]
    public void Reader_RejectsWrongManifestAssetSha()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var asset = AssetPaths(written.PackageDirectory!)[0];
        RewriteManifest(
            written.PackageDirectory!,
            manifest => CloneManifest(
                manifest,
                manifest.Files.Select(file => file.Path == asset
                    ? new PackageFileEntry { Path = file.Path, Sha256 = new string('0', 64), Size = file.Size }
                    : file).ToArray(),
                manifest.Capabilities));

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.ChecksumMismatch);
    }

    [Fact]
    public void Reader_RejectsWrongManifestAssetSize()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var asset = AssetPaths(written.PackageDirectory!)[0];
        RewriteManifest(
            written.PackageDirectory!,
            manifest => CloneManifest(
                manifest,
                manifest.Files.Select(file => file.Path == asset
                    ? new PackageFileEntry { Path = file.Path, Sha256 = file.Sha256, Size = file.Size + 1 }
                    : file).ToArray(),
                manifest.Capabilities));

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.SizeMismatch);
    }

    [Fact]
    public void Reader_RejectsAssetExtensionMismatch()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        EditCatalog(written.PackageDirectory!, root =>
        {
            var body = root["components"]![0]!["bodies"]![0]!;
            body["assetFormat"] = "parasolid-x_t";
        });

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelAsset);
    }

    [Theory]
    [InlineData("../escape.step")]
    [InlineData("/assets/3d/escape.step")]
    [InlineData("assets\\3d\\escape.step")]
    [InlineData("assets/3d/C:/escape.step")]
    public void Reader_RejectsUnsafeAssetPath(string path)
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        EditCatalog(written.PackageDirectory!, root =>
        {
            var body = root["components"]![0]!["bodies"]![0]!;
            body["assetPath"] = path;
        });

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelAsset);
    }

    [Fact]
    public void Reader_RejectsAssetNotInManifest()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var packageDirectory = written.PackageDirectory!;
        var oldAsset = AssetPaths(packageDirectory)[0];
        var newPath = "assets/3d/" + new string('a', 64) + ".step";
        EditCatalog(packageDirectory, root =>
        {
            var body = root["components"]![0]!["bodies"]![0]!;
            body["assetPath"] = newPath;
        });
        RewriteManifest(
            packageDirectory,
            manifest => CloneManifest(
                manifest,
                manifest.Files.Where(file => file.Path != oldAsset).ToArray(),
                manifest.Capabilities));
        File.Delete(Path.Combine(packageDirectory, oldAsset.Replace('/', Path.DirectorySeparatorChar)));

        var read = ExchangePackageReader.Read(packageDirectory);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelAsset);
    }

    [Fact]
    public void Reader_RejectsUnreferencedAssetFile()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var packageDirectory = written.PackageDirectory!;
        var content = Encoding.UTF8.GetBytes("orphan");
        var sha = Sha(content);
        var relative = "assets/3d/" + sha + ".step";
        var fullPath = Path.Combine(packageDirectory, "assets", "3d", sha + ".step");
        File.WriteAllBytes(fullPath, content);
        RewriteManifest(
            packageDirectory,
            manifest => CloneManifest(
                manifest,
                manifest.Files.Concat(new[]
                {
                    new PackageFileEntry { Path = relative, Sha256 = sha, Size = content.Length },
                }).ToArray(),
                manifest.Capabilities));

        var read = ExchangePackageReader.Read(packageDirectory);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelAsset);
    }

    [Fact]
    public void Reader_RejectsCorruptCatalogJson()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var bytes = Encoding.UTF8.GetBytes("{");
        File.WriteAllBytes(
            Path.Combine(written.PackageDirectory!, PackageManifest.ComponentModelsFileName.Replace('/', Path.DirectorySeparatorChar)),
            bytes);
        RewriteManifestFile(written.PackageDirectory!, PackageManifest.ComponentModelsFileName, bytes);

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelCatalog);
    }

    [Fact]
    public void Reader_RejectsUnsupportedCatalogMajorVersion()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        EditCatalog(written.PackageDirectory!, root => root["schemaVersion"] = "2.0.0");

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.UnsupportedModelSchema);
    }

    [Fact]
    public void Reader_RejectsUnknownComponentId()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        EditCatalog(written.PackageDirectory!, root => root["components"]![0]!["componentId"] = "missing-component");

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelReference);
    }

    [Fact]
    public void Reader_RejectsDesignatorMismatch()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        EditCatalog(written.PackageDirectory!, root => root["components"]![0]!["designator"] = "R2");

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelReference);
    }

    [Fact]
    public void Reader_RejectsDuplicateBodyOrdinal()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        EditCatalog(written.PackageDirectory!, root =>
        {
            var bodies = root["components"]![0]!["bodies"]!.AsArray();
            bodies[1]!["bodyOrdinal"] = bodies[0]!["bodyOrdinal"]!.GetValue<int>();
        });

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelReference);
    }

    [Fact]
    public void Reader_RejectsDuplicateComponentId()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        EditCatalog(written.PackageDirectory!, root =>
        {
            var components = root["components"]!.AsArray();
            components.Add(components[0]!.DeepClone());
        });

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidModelReference);
    }

    [Fact]
    public void Reader_RejectsCaseInsensitivePathConflict()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var packageDirectory = written.PackageDirectory!;
        var asset = AssetPaths(packageDirectory)[0];
        var upper = asset.ToUpperInvariant();
        Assert.NotEqual(asset, upper);
        RewriteManifest(
            packageDirectory,
            manifest => CloneManifest(
                manifest,
                manifest.Files.Concat(new[]
                {
                    new PackageFileEntry
                    {
                        Path = upper,
                        Sha256 = manifest.Files.Single(file => file.Path == asset).Sha256,
                        Size = manifest.Files.Single(file => file.Path == asset).Size,
                    },
                }).ToArray(),
                manifest.Capabilities));

        var read = ExchangePackageReader.Read(packageDirectory);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.DuplicatePath);
    }

    private static void EditCatalog(string packageDirectory, Action<JsonObject> edit)
    {
        var path = Path.Combine(
            packageDirectory,
            PackageManifest.ComponentModelsFileName.Replace('/', Path.DirectorySeparatorChar));
        var root = JsonNode.Parse(File.ReadAllText(path, new UTF8Encoding(false)))!.AsObject();
        edit(root);
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        WriteCatalogText(packageDirectory, json);
    }

    private static string ReadCatalogText(string packageDirectory) =>
        File.ReadAllText(
            Path.Combine(
                packageDirectory,
                PackageManifest.ComponentModelsFileName.Replace('/', Path.DirectorySeparatorChar)),
            new UTF8Encoding(false));

    private static void WriteCatalogText(string packageDirectory, string json)
    {
        var path = Path.Combine(
            packageDirectory,
            PackageManifest.ComponentModelsFileName.Replace('/', Path.DirectorySeparatorChar));
        var bytes = new UTF8Encoding(false).GetBytes(json);
        File.WriteAllBytes(path, bytes);
        RewriteManifestFile(packageDirectory, PackageManifest.ComponentModelsFileName, bytes);
    }

    private static void RewriteManifestFile(string packageDirectory, string path, byte[] content)
    {
        RewriteManifest(
            packageDirectory,
            manifest => CloneManifest(
                manifest,
                manifest.Files.Select(file => file.Path == path
                    ? new PackageFileEntry
                    {
                        Path = path,
                        Sha256 = Sha(content),
                        Size = content.LongLength,
                    }
                    : file).ToArray(),
                manifest.Capabilities));
    }

    private static void RemoveCapability(string packageDirectory) =>
        RewriteManifest(
            packageDirectory,
            manifest => CloneManifest(
                manifest,
                manifest.Files,
                manifest.Capabilities
                    .Where(item => item != PackageManifest.ComponentModelsCapability)
                    .ToArray()));

    private static void RemoveCatalogEntry(string packageDirectory)
    {
        var modelsPath = Path.Combine(
            packageDirectory,
            PackageManifest.ComponentModelsFileName.Replace('/', Path.DirectorySeparatorChar));
        RewriteManifest(
            packageDirectory,
            manifest => CloneManifest(
                manifest,
                manifest.Files
                    .Where(file => file.Path != PackageManifest.ComponentModelsFileName)
                    .ToArray(),
                manifest.Capabilities));
        File.Delete(modelsPath);
    }

    private static void RewriteManifest(string packageDirectory, Func<PackageManifest, PackageManifest> change)
    {
        var manifestPath = Path.Combine(packageDirectory, PackageManifest.ManifestFileName);
        var manifest = PackageManifestJson.Deserialize(File.ReadAllText(manifestPath, new UTF8Encoding(false)));
        var changed = change(manifest);
        var manifestBytes = PackageManifestJson.SerializeToUtf8(changed);
        File.WriteAllBytes(manifestPath, manifestBytes);

        var entries = new List<(string Hash, string Path)>
        {
            (Sha(manifestBytes), PackageManifest.ManifestFileName),
        };
        foreach (var file in changed.Files)
        {
            var fullPath = Path.Combine(packageDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar));
            entries.Add((Sha(File.ReadAllBytes(fullPath)), file.Path));
        }

        var text = string.Join(
            "\n",
            entries
                .OrderBy(entry => entry.Path, StringComparer.Ordinal)
                .Select(entry => entry.Hash + "  " + entry.Path))
            + "\n";
        File.WriteAllText(
            Path.Combine(packageDirectory, PackageManifest.ChecksumsFileName),
            text,
            new UTF8Encoding(false));
    }

    private static PackageManifest CloneManifest(
        PackageManifest manifest,
        IReadOnlyList<PackageFileEntry> files,
        IReadOnlyList<string> capabilities) =>
        new()
        {
            Schema = manifest.Schema,
            SchemaVersion = manifest.SchemaVersion,
            PackageId = manifest.PackageId,
            CreatedAtUtc = manifest.CreatedAtUtc,
            Producer = manifest.Producer,
            Source = manifest.Source,
            SourceUnits = manifest.SourceUnits,
            Units = manifest.Units,
            Files = files,
            Capabilities = capabilities,
        };

    private static string[] AssetPaths(string packageDirectory) =>
        Directory.GetFiles(Path.Combine(packageDirectory, "assets", "3d"))
            .Select(path => Path.GetRelativePath(packageDirectory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string[] TemporaryDirectories(string root) =>
        Directory.GetDirectories(root, ".tmp-*")
            .Select(path => Path.GetFileName(path))
            .Where(name => name is not null)
            .Select(name => name!)
            .ToArray();

    private static PackageProducer Producer() =>
        new() { Component = "EcadMcadBridge.Altium", Version = "0.4.0" };

    private static PackageWriteOptions Options() => new()
    {
        PackageId = PackageId,
        CreatedAtUtc = Created,
        Producer = Producer(),
        Source = new PackageSource { Cad = "altium", DocumentSha256 = DocumentSha },
        Capabilities = new[] { "board-outline", "component-placement" },
    };

    private static string Sha(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string Describe(IReadOnlyList<PackageDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(item => item.Code + " " + item.Path + ": " + item.Message));

    private sealed class Scenario : IDisposable
    {
        public Scenario()
        {
            Temp = new TempDirectory();
            var inputs = Path.Combine(Temp.Path, "inputs");
            Directory.CreateDirectory(inputs);
            StepPath = Path.Combine(inputs, "generic.step");
            XtPath = Path.Combine(inputs, "native.x_t");
            File.WriteAllText(StepPath, "ISO-10303-21;\nHEADER;\nENDSEC;\nEND-ISO-10303-21;\n");
            File.WriteAllText(XtPath, "**ABCDEFGHIJKLMNOPQRSTUVWXYZ**\n");
        }

        public TempDirectory Temp { get; }

        public string StepPath { get; }

        public string XtPath { get; }

        public ComponentModelsWriteRequest Request(bool includeXtAsset = true, bool referenceXtAsset = true) =>
            BuildRequest(includeXtAsset, referenceXtAsset, StepPath, XtPath);

        public ComponentModelsWriteRequest RequestWithAssets(
            params (string Key, string Path, ComponentModelAssetFormat Format)[] assets)
        {
            var sources = assets
                .Select(item => new ComponentModelAssetSource
                {
                    AssetKey = item.Key,
                    SourcePath = item.Path,
                    Format = item.Format,
                })
                .ToArray();
            var components = new[]
            {
                new ComponentModelWriteEntry
                {
                    ComponentId = "component-1",
                    Designator = "R1",
                    Bodies = new[]
                    {
                        new ComponentBodyWriteEntry
                        {
                            BodyOrdinal = 0,
                            Representation = ComponentModelRepresentation.Generic,
                            SourceOrigin = ComponentModelSourceOrigin.Embedded,
                            AssetAcquisition = ComponentModelAssetAcquisition.SdkExport,
                            AssetKey = assets[0].Key,
                            Origin = new ModelOrigin(),
                            State = new ModelState(),
                        },
                    },
                },
                new ComponentModelWriteEntry
                {
                    ComponentId = "component-2",
                    Designator = "C1",
                    Bodies = Array.Empty<ComponentBodyWriteEntry>(),
                },
            };
            return new ComponentModelsWriteRequest { Components = components, Assets = sources };
        }

        public PackageWriteResult Write(
            ComponentModelsWriteRequest? request = null,
            string? root = null) =>
            ExchangePackageWriter.Write(
                root ?? Temp.Path,
                BoardSnapshotFixture.CreateValid(),
                Options(),
                request ?? Request());

        public void Dispose() => Temp.Dispose();

        private static ComponentModelsWriteRequest BuildRequest(
            bool includeXtAsset,
            bool referenceXtAsset,
            string stepPath,
            string xtPath)
        {
            var assets = new List<ComponentModelAssetSource>
            {
                new()
                {
                    AssetKey = "step-a",
                    SourcePath = stepPath,
                    Format = ComponentModelAssetFormat.StepAp214,
                },
            };
            if (includeXtAsset)
            {
                assets.Add(new ComponentModelAssetSource
                {
                    AssetKey = "xt-a",
                    SourcePath = xtPath,
                    Format = ComponentModelAssetFormat.ParasolidXT,
                });
            }

            var secondBodies = referenceXtAsset
                ? new[]
                {
                    new ComponentBodyWriteEntry
                    {
                        BodyOrdinal = 0,
                        Representation = ComponentModelRepresentation.Native,
                        SourceOrigin = ComponentModelSourceOrigin.Linked,
                        AssetAcquisition = ComponentModelAssetAcquisition.SourceCopyVerified,
                        AssetKey = "xt-a",
                        Origin = new ModelOrigin
                        {
                            XIu = -2_000_000,
                            XMm = AltiumUnits.IuToMm(-2_000_000),
                            YIu = 2_000_000,
                            YMm = AltiumUnits.IuToMm(2_000_000),
                        },
                        State = new ModelState
                        {
                            RotationXDegRaw = 0,
                            RotationYDegRaw = 0,
                            RotationZDegRaw = 90,
                            DzIu = 0,
                            DzMm = 0,
                        },
                        ModelRotationDegRaw = 90,
                    },
                }
                : Array.Empty<ComponentBodyWriteEntry>();

            return new ComponentModelsWriteRequest
            {
                Assets = assets,
                Components = new[]
                {
                    new ComponentModelWriteEntry
                    {
                        ComponentId = "component-1",
                        Designator = "R1",
                        Bodies = new[]
                        {
                            new ComponentBodyWriteEntry
                            {
                                BodyOrdinal = 0,
                                Representation = ComponentModelRepresentation.Generic,
                                SourceOrigin = ComponentModelSourceOrigin.Embedded,
                                AssetAcquisition = ComponentModelAssetAcquisition.SdkExport,
                                AssetKey = "step-a",
                                Origin = new ModelOrigin
                                {
                                    XIu = 1_234_567,
                                    XMm = AltiumUnits.IuToMm(1_234_567),
                                    YIu = -200_000,
                                    YMm = AltiumUnits.IuToMm(-200_000),
                                    ZIu = null,
                                    ZMm = null,
                                },
                                State = new ModelState
                                {
                                    RotationXDegRaw = 0,
                                    RotationYDegRaw = 0,
                                    RotationZDegRaw = 360,
                                    DzIu = 393_700,
                                    DzMm = AltiumUnits.IuToMm(393_700),
                                },
                                ModelRotationDegRaw = 0,
                            },
                            new ComponentBodyWriteEntry
                            {
                                BodyOrdinal = 1,
                                Representation = ComponentModelRepresentation.Unknown,
                                SourceOrigin = ComponentModelSourceOrigin.Unknown,
                                AssetAcquisition = ComponentModelAssetAcquisition.NotAcquired,
                                AssetKey = null,
                                Origin = new ModelOrigin(),
                                State = new ModelState(),
                                ModelRotationDegRaw = null,
                            },
                        },
                    },
                    new ComponentModelWriteEntry
                    {
                        ComponentId = "component-2",
                        Designator = "C1",
                        Bodies = secondBodies,
                    },
                },
            };
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "EcadMcadBridge.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
