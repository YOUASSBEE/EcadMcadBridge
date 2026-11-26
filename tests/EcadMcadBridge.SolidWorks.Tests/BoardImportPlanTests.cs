using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using EcadMcadBridge.SolidWorks.Importing;
using Xunit;

namespace EcadMcadBridge.SolidWorks.Tests;

public sealed class BoardImportPlanTests
{
    private static readonly Guid PackageId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Created = new(2026, 11, 26, 5, 0, 0, TimeSpan.Zero);
    private const string DocumentSha = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

    [Fact]
    public void BuildsBoardAssetFromPackage()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = PlanTestFixture.Snapshot("component-1");
        var step = WriteFile(temp, "a.step");
        var boardStep = WriteFile(temp, "board.step");
        var result = ExchangePackageWriter.Write(
            temp.Path,
            snapshot,
            Options(),
            new ComponentModelsWriteRequest
            {
                Assets = new[]
                {
                    new ComponentModelAssetSource
                    {
                        AssetKey = "a",
                        SourcePath = step,
                        Format = ComponentModelAssetFormat.StepAp214,
                    },
                },
                Components = new[]
                {
                    new ComponentModelWriteEntry
                    {
                        ComponentId = "component-1",
                        Designator = "U1",
                        Bodies = new[]
                        {
                            new ComponentBodyWriteEntry
                            {
                                BodyOrdinal = 0,
                                Representation = ComponentModelRepresentation.Generic,
                                SourceOrigin = ComponentModelSourceOrigin.Embedded,
                                AssetAcquisition = ComponentModelAssetAcquisition.SdkExport,
                                AssetKey = "a",
                            },
                        },
                    },
                },
            },
            new BoardModelWriteRequest { SourcePath = boardStep });
        Assert.True(result.IsSuccess, Describe(result.Errors));

        var read = ExchangePackageReader.Read(result.PackageDirectory!);
        Assert.True(read.IsSuccess, Describe(read.Errors));
        var plan = SolidWorksImportPlanBuilder.Build(
            read,
            result.PackageDirectory!,
            Path.Combine(temp.Path, "native"));

        Assert.True(plan.IsValid);
        Assert.NotNull(plan.BoardAsset);
        Assert.StartsWith("assets/board/", plan.BoardAsset!.AssetPath, StringComparison.Ordinal);
        Assert.EndsWith("board.SLDPRT", plan.BoardAsset.NativePartPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new FileInfo(plan.BoardAsset.FullPath).Length, plan.BoardAsset.ExpectedSize);
        Assert.NotNull(plan.Snapshot);

        var selected = plan.SelectAssets(Array.Empty<string>());
        Assert.NotNull(selected.BoardAsset);
        Assert.Empty(selected.Assets);
    }

    [Fact]
    public void RejectsMissingBoardAssetFile()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var packageRoot = Path.Combine(temp.Path, "package");
        Directory.CreateDirectory(Path.Combine(packageRoot, "assets", "board"));
        var sha = new string('a', 64);
        var descriptor = new BoardModelDescriptor
        {
            AssetPath = "assets/board/" + sha + ".step",
            Sha256 = sha,
            Size = 10,
        };

        var plan = SolidWorksImportPlanBuilder.Build(
            PlanTestFixture.Snapshot("component-1"),
            new ComponentModelCatalog { Components = Array.Empty<ComponentModelEntry>() },
            packageRoot,
            Path.Combine(temp.Path, "native"),
            descriptor);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLAN-032");
    }

    [Fact]
    public void RejectsUnsafeBoardAssetPath()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var packageRoot = Path.Combine(temp.Path, "package");
        Directory.CreateDirectory(packageRoot);
        var descriptor = new BoardModelDescriptor
        {
            AssetPath = "../escape.step",
            Sha256 = new string('a', 64),
            Size = 10,
        };

        var plan = SolidWorksImportPlanBuilder.Build(
            PlanTestFixture.Snapshot("component-1"),
            new ComponentModelCatalog { Components = Array.Empty<ComponentModelEntry>() },
            packageRoot,
            Path.Combine(temp.Path, "native"),
            descriptor);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLAN-031");
    }

    [Fact]
    public void RejectsNonStepBoardAsset()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var packageRoot = Path.Combine(temp.Path, "package");
        Directory.CreateDirectory(packageRoot);
        var descriptor = new BoardModelDescriptor
        {
            AssetPath = "assets/board/" + new string('a', 64) + ".x_t",
            AssetFormat = ComponentModelAssetFormat.ParasolidXT,
            Sha256 = new string('a', 64),
            Size = 10,
        };

        var plan = SolidWorksImportPlanBuilder.Build(
            PlanTestFixture.Snapshot("component-1"),
            new ComponentModelCatalog { Components = Array.Empty<ComponentModelEntry>() },
            packageRoot,
            Path.Combine(temp.Path, "native"),
            descriptor);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLAN-030");
    }

    private static PackageWriteOptions Options() => new()
    {
        PackageId = PackageId,
        CreatedAtUtc = Created,
        Producer = new PackageProducer { Component = "test", Version = "0.1.0" },
        Source = new PackageSource { Cad = "altium", DocumentSha256 = DocumentSha },
    };

    private static string Describe(IReadOnlyList<PackageDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(item => item.Code + ": " + item.Message));

    private static string WriteFile(PlanTestFixture.TempDirectory temp, string name)
    {
        var path = Path.Combine(temp.Path, "input", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n");
        return path;
    }
}
