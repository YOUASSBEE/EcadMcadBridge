using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using EcadMcadBridge.SolidWorks.Importing;
using Xunit;

namespace EcadMcadBridge.SolidWorks.Tests;

public sealed class SolidWorksImportPlanTests
{
    private static readonly Guid PackageId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Created = new(2026, 11, 25, 16, 0, 0, TimeSpan.Zero);
    private const string DocumentSha = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

    [Fact]
    public void ReadsPackageAndBuildsPlan()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = PlanTestFixture.Snapshot("component-1", "component-2");
        var stepA = WriteStep(temp, "a.step");
        var stepB = WriteStep(temp, "b.step");
        var package = WritePackage(
            temp,
            snapshot,
            new[]
            {
                Asset("a", stepA, ComponentModelAssetFormat.StepAp214),
                Asset("b", stepB, ComponentModelAssetFormat.StepAp214),
            },
            new[]
            {
                Component("component-1", "U1", Body(0, "a")),
                Component("component-2", "U2", Body(0, "b")),
            });

        var plan = SolidWorksImportPlanBuilder.Build(
            package.Read,
            package.Directory,
            Path.Combine(temp.Path, "native"));

        Assert.True(plan.IsValid);
        Assert.Equal(2, plan.Assets.Count);
        Assert.Equal(2, plan.BodyReferences.Count);
        Assert.All(plan.Assets, asset => Assert.True(File.Exists(asset.FullPath)));
        Assert.All(plan.Assets, asset => Assert.EndsWith(".SLDPRT", asset.NativePartPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeduplicatesSharedAssetReferences()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = PlanTestFixture.Snapshot("component-1", "component-2");
        var step = WriteStep(temp, "shared.step");
        var package = WritePackage(
            temp,
            snapshot,
            new[] { Asset("shared", step, ComponentModelAssetFormat.StepAp214) },
            new[]
            {
                Component(
                    "component-1",
                    "U1",
                    Body(0, "shared"),
                    Body(1, "shared")),
                Component("component-2", "U2", Body(0, "shared")),
            });

        var plan = SolidWorksImportPlanBuilder.Build(package.Read, package.Directory, Path.Combine(temp.Path, "native"));

        Assert.True(plan.IsValid);
        Assert.Single(plan.Assets);
        Assert.Equal(3, plan.BodyReferences.Count);
        Assert.Equal(3, plan.Assets[0].BodyReferences.Count);
    }

    [Fact]
    public void SkipsBodiesWithoutAssetWithWarning()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = PlanTestFixture.Snapshot("component-1");
        var step = WriteStep(temp, "a.step");
        var package = WritePackage(
            temp,
            snapshot,
            new[] { Asset("a", step, ComponentModelAssetFormat.StepAp214) },
            new[]
            {
                Component(
                    "component-1",
                    "U1",
                    Body(0, "a"),
                    new ComponentBodyWriteEntry
                    {
                        BodyOrdinal = 1,
                        Representation = ComponentModelRepresentation.Unknown,
                        SourceOrigin = ComponentModelSourceOrigin.Unknown,
                        AssetAcquisition = ComponentModelAssetAcquisition.NotAcquired,
                    }),
            });

        var plan = SolidWorksImportPlanBuilder.Build(package.Read, package.Directory, Path.Combine(temp.Path, "native"));

        Assert.True(plan.IsValid);
        Assert.Single(plan.Assets);
        Assert.Single(plan.BodyReferences);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLAN-006");
    }

    [Fact]
    public void RejectsUnsupportedAssetFormat()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = PlanTestFixture.Snapshot("component-1");
        var xT = Path.Combine(temp.Path, "input", "a.x_t");
        Directory.CreateDirectory(Path.GetDirectoryName(xT)!);
        File.WriteAllText(xT, "**PARASOLID**");
        var package = WritePackage(
            temp,
            snapshot,
            new[] { Asset("a", xT, ComponentModelAssetFormat.ParasolidXT) },
            new[] { Component("component-1", "U1", Body(0, "a")) });

        var plan = SolidWorksImportPlanBuilder.Build(package.Read, package.Directory, Path.Combine(temp.Path, "native"));

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLAN-007");
    }

    [Fact]
    public void RejectsMissingModels()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = PlanTestFixture.Snapshot("component-1");
        var package = WritePackageWithoutModels(temp, snapshot);

        var plan = SolidWorksImportPlanBuilder.Build(package.Read, package.Directory, Path.Combine(temp.Path, "native"));

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLAN-002");
    }

    [Fact]
    public void RejectsMissingAssetFile()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = PlanTestFixture.Snapshot("component-1");
        var step = WriteStep(temp, "a.step");
        var package = WritePackage(
            temp,
            snapshot,
            new[] { Asset("a", step, ComponentModelAssetFormat.StepAp214) },
            new[] { Component("component-1", "U1", Body(0, "a")) });
        var assetPath = Path.Combine(package.Directory, "assets", "3d");
        File.Delete(Directory.GetFiles(assetPath).Single());

        var plan = SolidWorksImportPlanBuilder.Build(package.Read, package.Directory, Path.Combine(temp.Path, "native"));

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLAN-009");
    }

    [Fact]
    public void RejectsEmptyAssetFile()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = PlanTestFixture.Snapshot("component-1");
        var step = WriteStep(temp, "a.step");
        var package = WritePackage(
            temp,
            snapshot,
            new[] { Asset("a", step, ComponentModelAssetFormat.StepAp214) },
            new[] { Component("component-1", "U1", Body(0, "a")) });
        var assetPath = Path.Combine(package.Directory, "assets", "3d");
        File.WriteAllBytes(Directory.GetFiles(assetPath).Single(), Array.Empty<byte>());

        var plan = SolidWorksImportPlanBuilder.Build(package.Read, package.Directory, Path.Combine(temp.Path, "native"));

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLAN-009");
    }

    [Fact]
    public void NativeNameUsesContentAddressedStem()
    {
        var sha = new string('a', 64);
        Assert.Equal(sha + ".SLDPRT", SolidWorksImportPlanBuilder.BuildNativeName("assets/3d/" + sha + ".step"));

        var unsafeName = SolidWorksImportPlanBuilder.BuildNativeName("../bad name.step");
        Assert.DoesNotContain("bad name", unsafeName, StringComparison.Ordinal);
        Assert.EndsWith(".SLDPRT", unsafeName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectAssetsKeepsOnlyRequestedReferences()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = PlanTestFixture.Snapshot("component-1", "component-2");
        var stepA = WriteStep(temp, "a.step");
        var stepB = WriteStep(temp, "b.step");
        var package = WritePackage(
            temp,
            snapshot,
            new[]
            {
                Asset("a", stepA, ComponentModelAssetFormat.StepAp214),
                Asset("b", stepB, ComponentModelAssetFormat.StepAp214),
            },
            new[]
            {
                Component("component-1", "U1", Body(0, "a")),
                Component("component-2", "U2", Body(0, "b")),
            });
        var plan = SolidWorksImportPlanBuilder.Build(package.Read, package.Directory, Path.Combine(temp.Path, "native"));
        var selectedPath = plan.Assets[0].AssetPath;

        var selected = plan.SelectAssets(new[] { selectedPath });

        Assert.Single(selected.Assets);
        Assert.Single(selected.BodyReferences);
    }

    private static CreatedPackage WritePackage(
        PlanTestFixture.TempDirectory temp,
        BoardSnapshot snapshot,
        IReadOnlyList<ComponentModelAssetSource> assets,
        IReadOnlyList<ComponentModelWriteEntry> components)
    {
        var request = new ComponentModelsWriteRequest
        {
            Assets = assets,
            Components = components,
        };
        var result = ExchangePackageWriter.Write(
            temp.Path,
            snapshot,
            Options(),
            request);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(item => item.Code + ": " + item.Message)));
        var read = ExchangePackageReader.Read(result.PackageDirectory!);
        Assert.True(read.IsSuccess, string.Join("; ", read.Errors.Select(item => item.Code + ": " + item.Message)));
        return new CreatedPackage(read, result.PackageDirectory!);
    }

    private static CreatedPackage WritePackageWithoutModels(
        PlanTestFixture.TempDirectory temp,
        BoardSnapshot snapshot)
    {
        var result = ExchangePackageWriter.Write(temp.Path, snapshot, Options());
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(item => item.Code + ": " + item.Message)));
        var read = ExchangePackageReader.Read(result.PackageDirectory!);
        Assert.True(read.IsSuccess, string.Join("; ", read.Errors.Select(item => item.Code + ": " + item.Message)));
        return new CreatedPackage(read, result.PackageDirectory!);
    }

    private sealed record CreatedPackage(PackageReadResult Read, string Directory);

    private static PackageWriteOptions Options() => new()
    {
        PackageId = PackageId,
        CreatedAtUtc = Created,
        Producer = new PackageProducer { Component = "EcadMcadBridge.SolidWorks.Tests", Version = "0.1.0" },
        Source = new PackageSource { Cad = "altium", DocumentSha256 = DocumentSha },
        Capabilities = new[] { "board-outline", "component-placement" },
    };

    private static ComponentModelAssetSource Asset(
        string key,
        string path,
        ComponentModelAssetFormat format) =>
        new()
        {
            AssetKey = key,
            SourcePath = path,
            Format = format,
        };

    private static ComponentModelWriteEntry Component(
        string componentId,
        string designator,
        params ComponentBodyWriteEntry[] bodies) =>
        new()
        {
            ComponentId = componentId,
            Designator = designator,
            Bodies = bodies,
        };

    private static ComponentBodyWriteEntry Body(int ordinal, string assetKey) => new()
    {
        BodyOrdinal = ordinal,
        Representation = ComponentModelRepresentation.Generic,
        SourceOrigin = ComponentModelSourceOrigin.Embedded,
        AssetAcquisition = ComponentModelAssetAcquisition.SdkExport,
        AssetKey = assetKey,
    };

    private static string WriteStep(PlanTestFixture.TempDirectory temp, string name)
    {
        var path = Path.Combine(temp.Path, "input", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "ISO-10303-21;\nMANIFOLD_SOLID_BREP;\n" + name + "\nEND-ISO-10303-21;\n");
        return path;
    }
}
