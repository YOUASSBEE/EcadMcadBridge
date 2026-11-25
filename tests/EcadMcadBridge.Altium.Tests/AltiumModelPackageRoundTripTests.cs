using System.Text;
using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class AltiumModelPackageRoundTripTests
{
    private static readonly Guid PackageId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Created = new(2026, 11, 25, 15, 30, 0, TimeSpan.Zero);
    private const string DocumentSha = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void AltiumModelRequestRoundTripsThroughCorePackage()
    {
        using var temp = new TestTempDirectory();
        var snapshot = ModelTestSnapshot.Create(
            ModelTestSnapshot.Seed("component-1", "R1"),
            ModelTestSnapshot.Seed("component-2", "C1"),
            ModelTestSnapshot.Seed("component-3", "U1"));
        var firstStep = temp.File("first.step");
        var secondStep = temp.File("second.step");
        File.WriteAllText(firstStep, "ISO-10303-21;\nFIRST;\nEND-ISO-10303-21;\n");
        File.WriteAllText(secondStep, "ISO-10303-21;\nSECOND;\nEND-ISO-10303-21;\n");

        var bodies = new[]
        {
            new AltiumBodyInventoryEntry(
                "component-1",
                0,
                ComponentModelRepresentation.Generic,
                ComponentModelSourceOrigin.Embedded,
                new ModelOrigin
                {
                    XIu = 1_000_000,
                    XMm = AltiumUnits.IuToMm(1_000_000),
                    YIu = 2_000_000,
                    YMm = AltiumUnits.IuToMm(2_000_000),
                },
                new ModelState
                {
                    RotationXDegRaw = 0,
                    RotationYDegRaw = 0,
                    RotationZDegRaw = 360,
                    DzIu = 393_700,
                    DzMm = AltiumUnits.IuToMm(393_700),
                },
                0,
                ComponentModelAssetAcquisition.SdkExport,
                "body-1"),
            new AltiumBodyInventoryEntry(
                "component-1",
                1,
                ComponentModelRepresentation.Native,
                ComponentModelSourceOrigin.Native,
                new ModelOrigin(),
                new ModelState(),
                null,
                ComponentModelAssetAcquisition.NotAcquired,
                null),
            new AltiumBodyInventoryEntry(
                "component-3",
                0,
                ComponentModelRepresentation.Unknown,
                ComponentModelSourceOrigin.Unknown,
                new ModelOrigin(),
                new ModelState(),
                null,
                ComponentModelAssetAcquisition.SdkExport,
                "body-2"),
        };
        var assets = new[]
        {
            new ComponentModelAssetSource
            {
                AssetKey = "body-1",
                SourcePath = firstStep,
                Format = ComponentModelAssetFormat.StepAp214,
            },
            new ComponentModelAssetSource
            {
                AssetKey = "body-2",
                SourcePath = secondStep,
                Format = ComponentModelAssetFormat.StepAp214,
            },
        };
        var request = AltiumComponentModelRequestBuilder.Build(snapshot, bodies, assets);

        var written = ExchangePackageWriter.Write(
            temp.Path,
            snapshot,
            new PackageWriteOptions
            {
                PackageId = PackageId,
                CreatedAtUtc = Created,
                Producer = new PackageProducer
                {
                    Component = "EcadMcadBridge.Altium",
                    Version = "0.4.0",
                },
                Source = new PackageSource
                {
                    Cad = "altium",
                    DocumentSha256 = DocumentSha,
                },
                Capabilities = new[] { "board-outline", "component-placement" },
            },
            request);

        Assert.True(written.IsSuccess, Describe(written.Errors));
        var packageDirectory = written.PackageDirectory!;
        var manifest = PackageManifestJson.Deserialize(
            File.ReadAllText(Path.Combine(packageDirectory, PackageManifest.ManifestFileName)));
        Assert.Equal(1, manifest.Capabilities.Count(item => item == PackageManifest.ComponentModelsCapability));
        Assert.Contains(PackageManifest.ComponentModelsCapability, manifest.Capabilities);

        var read = ExchangePackageReader.Read(packageDirectory);
        Assert.True(read.IsSuccess, Describe(read.Errors));
        Assert.NotNull(read.Models);
        Assert.Equal(3, read.Models!.Components.Count);
        Assert.Equal(
            snapshot.Components.Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal),
            read.Models.Components.Select(item => item.ComponentId).OrderBy(item => item, StringComparer.Ordinal));

        var first = read.Models.Components.Single(item => item.ComponentId == "component-1");
        Assert.Equal(2, first.Bodies.Count);
        Assert.Equal(360, first.Bodies[0].State.RotationZDegRaw);
        Assert.NotNull(first.Bodies[0].AssetPath);
        Assert.Equal(ComponentModelAssetAcquisition.NotAcquired, first.Bodies[1].AssetAcquisition);
        Assert.Null(first.Bodies[1].AssetPath);

        var second = read.Models.Components.Single(item => item.ComponentId == "component-2");
        Assert.Empty(second.Bodies);

        var third = read.Models.Components.Single(item => item.ComponentId == "component-3");
        Assert.NotNull(Assert.Single(third.Bodies).AssetPath);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(packageDirectory, "assets", "3d")).Length);

        var catalogText = File.ReadAllText(
            Path.Combine(packageDirectory, PackageManifest.ComponentModelsFileName.Replace('/', Path.DirectorySeparatorChar)),
            new UTF8Encoding(false));
        Assert.DoesNotContain(temp.Path, catalogText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".PcbDoc", catalogText, StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(IReadOnlyList<PackageDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(item => item.Code + " " + item.Path + ": " + item.Message));
}
