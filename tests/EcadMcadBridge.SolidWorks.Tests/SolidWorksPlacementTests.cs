using System.Text;
using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using EcadMcadBridge.Core.Placement;
using EcadMcadBridge.SolidWorks.Importing;
using EcadMcadBridge.SolidWorks.Placement;
using Xunit;

namespace EcadMcadBridge.SolidWorks.Tests;

public sealed class SolidWorksPlacementTests
{
    private const string DocumentSha = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private static readonly Guid PackageId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Created = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Vector2Mm TopLocalOffset = new(-4.314346, 1.740448);
    private const double ThicknessMm = 2.028002;

    [Fact]
    public void BuildsR3TopTransformsFromPackageMetadata()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = R3Snapshot();
        var package = WritePackage(temp, snapshot, component =>
        {
            var origin = ModelOriginFor(component);
            return Body(origin, component.Placement.RotationDeg!.Value, ComponentModelStateSource.PlacedBodyEffective);
        });

        var plan = SolidWorksPlacementRequestBuilder.Build(
            package.Read,
            ImportPlan(package, temp),
            designators: new[] { "U101", "U102", "U103", "U104" });

        Assert.True(plan.IsValid, Describe(plan.Diagnostics));
        Assert.Equal(4, plan.Requests.Count);
        Assert.Empty(plan.MissingDesignators);

        var expected = new[]
        {
            (23.085654, 54.540448, 0.0),
            (65.659552, 48.485654, 90.0),
            (111.714346, 51.059552, -180.0),
            (149.140448, 57.114346, -90.0),
        };
        for (var i = 0; i < plan.Requests.Count; i++)
        {
            var request = plan.Requests[i];
            var transform = request.Transform;
            Assert.InRange(Math.Abs(transform[0, 3] - expected[i].Item1), 0, 0.0001);
            Assert.InRange(Math.Abs(transform[1, 3] - expected[i].Item2), 0, 0.0001);
            Assert.InRange(Math.Abs(transform[2, 3] - ThicknessMm / 2.0), 0, 0.0001);
            Assert.Equal(1.0, transform.Determinant3x3(), precision: 12);
            Assert.Equal(expected[i].Item3, RotationAroundZ(transform), precision: 9);
        }
    }

    [Fact]
    public void SharesNativePartForRepeatedAsset()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = TwoComponentSnapshot();
        var package = WritePackage(temp, snapshot, component =>
            Body(ModelOriginFor(component), 0, ComponentModelStateSource.PlacedBodyEffective));

        var plan = SolidWorksPlacementRequestBuilder.Build(
            package.Read,
            ImportPlan(package, temp),
            maxComponents: 10);

        Assert.True(plan.IsValid, Describe(plan.Diagnostics));
        Assert.Equal(2, plan.Requests.Count);
        Assert.Single(plan.Requests.Select(request => request.AssetPath).Distinct(StringComparer.Ordinal));
        Assert.Single(plan.Requests.Select(request => request.NativePartPath).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void RequestedDesignatorMustExist()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var package = WritePackage(temp, TwoComponentSnapshot(), component =>
            Body(ModelOriginFor(component), 0, ComponentModelStateSource.PlacedBodyEffective));

        var plan = SolidWorksPlacementRequestBuilder.Build(
            package.Read,
            ImportPlan(package, temp),
            designators: new[] { "U999" });

        Assert.False(plan.IsValid);
        Assert.Single(plan.MissingDesignators);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLC-017");
    }

    [Fact]
    public void MissingBoardThicknessIsExplicit()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = TwoComponentSnapshot(thickness: null);
        var package = WritePackage(temp, snapshot, component =>
            Body(ModelOriginFor(component), 0, ComponentModelStateSource.PlacedBodyEffective));

        var plan = SolidWorksPlacementRequestBuilder.Build(package.Read, ImportPlan(package, temp));

        Assert.False(plan.IsValid);
        Assert.Empty(plan.Requests);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLC-002");
    }

    [Fact]
    public void MissingModelOriginMakesBodyUnsupported()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var package = WritePackage(temp, TwoComponentSnapshot(), component =>
            Body(new ModelOrigin(), 0, ComponentModelStateSource.PlacedBodyEffective));

        var plan = SolidWorksPlacementRequestBuilder.Build(
            package.Read,
            ImportPlan(package, temp),
            designators: new[] { "U101" });

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLC-014");
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLC-024");
        Assert.Equal(0, plan.SupportedBodyReferences);
    }

    [Fact]
    public void NonZeroModelRotationIsNotIgnored()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var package = WritePackage(temp, TwoComponentSnapshot(), component =>
            Body(
                ModelOriginFor(component),
                0,
                ComponentModelStateSource.PlacedBodyEffective,
                modelRotationDegRaw: 3.0));

        var plan = SolidWorksPlacementRequestBuilder.Build(
            package.Read,
            ImportPlan(package, temp),
            designators: new[] { "U101" });

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLC-016");
    }

    [Fact]
    public void UnsupportedProjectionAndNativeAreNotPlaced()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var package = WritePackage(temp, TwoComponentSnapshot(), component =>
            Body(
                ModelOriginFor(component),
                0,
                ComponentModelStateSource.PlacedBodyEffective,
                projection: component.Designator == "U101"
                    ? ComponentBodyProjection.Bottom
                    : ComponentBodyProjection.Top,
                representation: component.Designator == "U101"
                    ? ComponentModelRepresentation.Generic
                    : ComponentModelRepresentation.Native));

        var plan = SolidWorksPlacementRequestBuilder.Build(package.Read, ImportPlan(package, temp));

        Assert.False(plan.IsValid);
        Assert.Empty(plan.Requests);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLC-007");
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLC-008");
    }

    [Fact]
    public void PlacedBodyEffectiveNormalizesComponentRotation()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = CreateSnapshot(
            LengthValue.FromIu(AltiumUnits.MmToIu(ThicknessMm)),
            new[] { Component("component-1", "U101", 240, 220, 90) });
        var package = WritePackage(temp, snapshot, component =>
            Body(ModelOriginFor(component), 90, ComponentModelStateSource.PlacedBodyEffective));

        var plan = SolidWorksPlacementRequestBuilder.Build(
            package.Read,
            ImportPlan(package, temp),
            designators: new[] { "U101" });

        Assert.True(plan.IsValid, Describe(plan.Diagnostics));
        var request = Assert.Single(plan.Requests);
        Assert.InRange(Math.Abs(request.ModelState.Rz), 0, 1e-9);
        Assert.InRange(Math.Abs(request.Transform[0, 3] - 238.259552), 0, 0.0001);
        Assert.InRange(Math.Abs(request.Transform[1, 3] - 215.685654), 0, 0.0001);
    }

    [Fact]
    public void MissingStandoffOrStateIsExplicit()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var package = WritePackage(temp, TwoComponentSnapshot(), component =>
            Body(
                ModelOriginFor(component),
                0,
                ComponentModelStateSource.PlacedBodyEffective,
                standoffHeight: LengthValue.FromIu(null),
                state: new ModelState()));

        var plan = SolidWorksPlacementRequestBuilder.Build(package.Read, ImportPlan(package, temp));

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Diagnostics, item => item.Code == "SW-PLC-015");
    }

    [Fact]
    public void ConverterUsesTransposedRotationAndMetres()
    {
        var matrix = new Matrix4x4D(
            0, -1, 0, 10,
            1, 0, 0, -20,
            0, 0, 1, 30,
            0, 0, 0, 1);

        Assert.True(SolidWorksTransformConverter.TryToArrayData(matrix, out var data, out var error), error);
        Assert.True(new double[] { 0, 1, 0, -1, 0, 0, 0, 0, 1 }.SequenceEqual(data[..9]));
        Assert.Equal(0.01, data[9], precision: 12);
        Assert.Equal(-0.02, data[10], precision: 12);
        Assert.Equal(0.03, data[11], precision: 12);
        Assert.Equal(1.0, data[12], precision: 12);

        Assert.True(SolidWorksTransformConverter.TryFromArrayData(data, out var roundTrip, out error), error);
        Assert.True(roundTrip.NearlyEquals(matrix, 1e-9));
    }

    [Fact]
    public void ConverterRejectsScaleAndReflection()
    {
        var scaled = new Matrix4x4D(
            2, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
        Assert.False(SolidWorksTransformConverter.TryToArrayData(scaled, out _, out _));

        var reflected = new Matrix4x4D(
            1, 0, 0, 0,
            0, -1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
        Assert.False(SolidWorksTransformConverter.TryToArrayData(reflected, out _, out _));
    }

    [Fact]
    public void ConverterMeasuresTranslationAndRotationResiduals()
    {
        var expected = Matrix4x4D.CreateTranslation(1, 2, 3);
        var actual = Matrix4x4D.Multiply(
            Matrix4x4D.CreateTranslation(1, 2, 3),
            Matrix4x4D.CreateRotationZ(0.001));
        var residual = SolidWorksTransformConverter.Measure(expected, actual);

        Assert.InRange(residual.TranslationMm, 0, 1e-12);
        Assert.InRange(residual.RotationBasisMax, 1e-8, 1e-3);
        Assert.Equal(1.0, residual.Determinant, precision: 12);
    }

    [Fact]
    public void ExplicitSelectionRejectsPartiallySupportedMultiBodyComponent()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = CreateSnapshot(
            LengthValue.FromIu(AltiumUnits.MmToIu(ThicknessMm)),
            new[] { Component("component-1", "U101", 240, 220, 0) });
        var package = WritePackage(temp, snapshot, component => new[]
        {
            Body(ModelOriginFor(component), 0, ComponentModelStateSource.PlacedBodyEffective, bodyOrdinal: 0),
            Body(
                ModelOriginFor(component),
                0,
                ComponentModelStateSource.PlacedBodyEffective,
                projection: ComponentBodyProjection.Bottom,
                bodyOrdinal: 1),
        });

        var plan = SolidWorksPlacementRequestBuilder.Build(
            package.Read,
            ImportPlan(package, temp),
            designators: new[] { "U101" });

        Assert.False(plan.IsValid);
        Assert.Empty(plan.Requests);
        Assert.Contains(plan.Diagnostics, item =>
            item.Code == "SW-PLC-024" &&
            item.Path.Contains("U101/1", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitSelectionRejectsOneUnsupportedDesignator()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = CreateSnapshot(
            LengthValue.FromIu(AltiumUnits.MmToIu(ThicknessMm)),
            new[]
            {
                Component("component-1", "U101", 240, 220, 0),
                Component("component-2", "U102", 280, 220, 0),
            });
        var package = WritePackage(temp, snapshot, component =>
            Body(
                ModelOriginFor(component),
                0,
                ComponentModelStateSource.PlacedBodyEffective,
                projection: component.Designator == "U101"
                    ? ComponentBodyProjection.Top
                    : ComponentBodyProjection.Bottom));

        var plan = SolidWorksPlacementRequestBuilder.Build(
            package.Read,
            ImportPlan(package, temp),
            designators: new[] { "U101", "U102" });

        Assert.False(plan.IsValid);
        Assert.Single(plan.Requests);
        Assert.Equal("U101", plan.Requests[0].Designator);
        Assert.Contains(plan.Diagnostics, item =>
            item.Code == "SW-PLC-024" &&
            item.Path.Contains("U102/0", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitSelectionPlacesAllBodiesOfMultiBodyComponent()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = CreateSnapshot(
            LengthValue.FromIu(AltiumUnits.MmToIu(ThicknessMm)),
            new[] { Component("component-1", "U101", 240, 220, 0) });
        var package = WritePackage(temp, snapshot, component => new[]
        {
            Body(ModelOriginFor(component), 0, ComponentModelStateSource.PlacedBodyEffective, bodyOrdinal: 0),
            Body(ModelOriginFor(component), 0, ComponentModelStateSource.PlacedBodyEffective, bodyOrdinal: 1),
        });

        var plan = SolidWorksPlacementRequestBuilder.Build(
            package.Read,
            ImportPlan(package, temp),
            designators: new[] { "U101" });

        Assert.True(plan.IsValid, Describe(plan.Diagnostics));
        Assert.Equal(2, plan.Requests.Count);
        Assert.Equal(
            new[] { "component-1|0", "component-1|1" },
            plan.Requests
                .Select(request => request.ComponentId + "|" + request.BodyOrdinal)
                .OrderBy(item => item, StringComparer.Ordinal));
    }

    [Fact]
    public void ExplicitSelectionRejectsBodyWithoutStepAsset()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = CreateSnapshot(
            LengthValue.FromIu(AltiumUnits.MmToIu(ThicknessMm)),
            new[] { Component("component-1", "U101", 240, 220, 0) });
        var package = WritePackage(temp, snapshot, component =>
            Body(
                ModelOriginFor(component),
                0,
                ComponentModelStateSource.PlacedBodyEffective,
                assetKey: null,
                acquisition: ComponentModelAssetAcquisition.NotAcquired));

        var plan = SolidWorksPlacementRequestBuilder.Build(
            package.Read,
            ImportPlan(package, temp),
            designators: new[] { "U101" });

        Assert.False(plan.IsValid);
        Assert.Empty(plan.Requests);
        Assert.Contains(plan.Diagnostics, item =>
            item.Code == "SW-PLC-024" &&
            item.Path.Contains("U101/0", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitSelectionRejectsBodyWithoutImportReference()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = CreateSnapshot(
            LengthValue.FromIu(AltiumUnits.MmToIu(ThicknessMm)),
            new[] { Component("component-1", "U101", 240, 220, 0) });
        var package = WritePackage(temp, snapshot, component =>
            Body(ModelOriginFor(component), 0, ComponentModelStateSource.PlacedBodyEffective));
        var full = ImportPlan(package, temp);
        var withoutReferences = new SolidWorksImportPlan(
            full.Assets,
            Array.Empty<SolidWorksBodyReference>(),
            full.Diagnostics);

        var plan = SolidWorksPlacementRequestBuilder.Build(
            package.Read,
            withoutReferences,
            designators: new[] { "U101" });

        Assert.False(plan.IsValid);
        Assert.Empty(plan.Requests);
        Assert.Contains(plan.Diagnostics, item =>
            item.Code == "SW-PLC-024" &&
            item.Path.Contains("U101/0", StringComparison.Ordinal));
    }

    [Fact]
    public void AutomaticSelectionExcludesPartiallySupportedMultiBodyComponent()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var snapshot = CreateSnapshot(
            LengthValue.FromIu(AltiumUnits.MmToIu(ThicknessMm)),
            new[]
            {
                Component("component-1", "U101", 240, 220, 0),
                Component("component-2", "U102", 280, 220, 0),
            });
        var package = WritePackage(temp, snapshot, component =>
            component.Designator == "U101"
                ? new[]
                {
                    Body(ModelOriginFor(component), 0, ComponentModelStateSource.PlacedBodyEffective, bodyOrdinal: 0),
                    Body(
                        ModelOriginFor(component),
                        0,
                        ComponentModelStateSource.PlacedBodyEffective,
                        projection: ComponentBodyProjection.Bottom,
                        bodyOrdinal: 1),
                }
                : new[]
                {
                    Body(ModelOriginFor(component), 0, ComponentModelStateSource.PlacedBodyEffective),
                });

        var plan = SolidWorksPlacementRequestBuilder.Build(
            package.Read,
            ImportPlan(package, temp),
            maxComponents: 10);

        Assert.True(plan.IsValid, Describe(plan.Diagnostics));
        Assert.Single(plan.Requests);
        Assert.Equal("U102", plan.Requests[0].Designator);
        Assert.DoesNotContain(plan.Requests, request => request.Designator == "U101");
    }

    private static TestPackage WritePackage(
        PlanTestFixture.TempDirectory temp,
        BoardSnapshot snapshot,
        Func<ComponentInstance, ComponentBodyWriteEntry> bodyFactory) =>
        WritePackage(temp, snapshot, component => new[] { bodyFactory(component) });

    private static TestPackage WritePackage(
        PlanTestFixture.TempDirectory temp,
        BoardSnapshot snapshot,
        Func<ComponentInstance, IReadOnlyList<ComponentBodyWriteEntry>> bodyFactory)
    {
        var stepPath = Path.Combine(temp.Path, "placement.step");
        File.WriteAllText(stepPath, "ISO-10303-21;\nHEADER;\nENDSEC;\nEND-ISO-10303-21;\n", new UTF8Encoding(false));
        var components = snapshot.Components
            .Select(component => new ComponentModelWriteEntry
            {
                ComponentId = component.Id,
                Designator = component.Designator,
                Bodies = bodyFactory(component),
            })
            .ToArray();
        var request = new ComponentModelsWriteRequest
        {
            Assets = components
                .SelectMany(component => component.Bodies)
                .Any(body => string.Equals(body.AssetKey, "shared", StringComparison.Ordinal))
                ? new[]
                {
                    new ComponentModelAssetSource
                    {
                        AssetKey = "shared",
                        SourcePath = stepPath,
                        Format = ComponentModelAssetFormat.StepAp214,
                    },
                }
                : Array.Empty<ComponentModelAssetSource>(),
            Components = components,
        };
        var written = ExchangePackageWriter.Write(
            temp.Path,
            snapshot,
            new PackageWriteOptions
            {
                PackageId = PackageId,
                CreatedAtUtc = Created,
                Producer = new PackageProducer { Component = "EcadMcadBridge.Altium", Version = "0.4.0" },
                Source = new PackageSource { Cad = "altium", DocumentSha256 = DocumentSha },
                Capabilities = new[] { "board-outline", "component-placement" },
            },
            request);
        Assert.True(written.IsSuccess, Describe(written.Errors));
        return new TestPackage(ExchangePackageReader.Read(written.PackageDirectory!), written.PackageDirectory!);
    }

    private static SolidWorksImportPlan ImportPlan(TestPackage package, PlanTestFixture.TempDirectory temp) =>
        SolidWorksImportPlanBuilder.Build(
            package.Read,
            package.Directory,
            Path.Combine(temp.Path, "native"));

    private static ComponentBodyWriteEntry Body(
        ModelOrigin origin,
        double stateRotationZ,
        ComponentModelStateSource stateSource,
        ComponentBodyProjection projection = ComponentBodyProjection.Top,
        ComponentModelRepresentation representation = ComponentModelRepresentation.Generic,
        double? modelRotationDegRaw = 0,
        LengthValue? standoffHeight = null,
        ModelState? state = null,
        int bodyOrdinal = 0,
        string? assetKey = "shared",
        ComponentModelAssetAcquisition acquisition = ComponentModelAssetAcquisition.SdkExport) =>
        new()
        {
            BodyOrdinal = bodyOrdinal,
            Representation = representation,
            SourceOrigin = ComponentModelSourceOrigin.Embedded,
            AssetAcquisition = acquisition,
            AssetKey = assetKey,
            Origin = origin,
            State = state ?? new ModelState
            {
                RotationXDegRaw = 0,
                RotationYDegRaw = 0,
                RotationZDegRaw = stateRotationZ,
                DzIu = 0,
                DzMm = 0,
            },
            ModelRotationDegRaw = modelRotationDegRaw,
            BodyProjection = projection,
            ModelStateSource = stateSource,
            StandoffHeight = standoffHeight ?? LengthValue.FromIu(0),
            OverallHeight = LengthValue.FromIu(AltiumUnits.MmToIu(3)),
        };

    private static ModelOrigin ModelOriginFor(ComponentInstance component)
    {
        var rotation = component.Placement.RotationDeg!.Value;
        var radians = rotation * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var local = new Vector2Mm(
            cos * TopLocalOffset.X - sin * TopLocalOffset.Y,
            sin * TopLocalOffset.X + cos * TopLocalOffset.Y);
        var x = component.Placement.Position.XMm!.Value + local.X;
        var y = component.Placement.Position.YMm!.Value + local.Y;
        var xIu = AltiumUnits.MmToIu(x);
        var yIu = AltiumUnits.MmToIu(y);
        return new ModelOrigin
        {
            XIu = xIu,
            XMm = AltiumUnits.IuToMm(xIu),
            YIu = yIu,
            YMm = AltiumUnits.IuToMm(yIu),
            ZIu = null,
            ZMm = null,
        };
    }

    private static BoardSnapshot R3Snapshot() =>
        CreateSnapshot(
            LengthValue.FromIu(AltiumUnits.MmToIu(ThicknessMm)),
            new[]
            {
                Component("component-u101", "U101", 240, 220, 0),
                Component("component-u102", "U102", 280, 220, 90),
                Component("component-u103", "U103", 320, 220, -180),
                Component("component-u104", "U104", 360, 220, -90),
            },
            originX: 212.6,
            originY: 167.2);

    private static BoardSnapshot TwoComponentSnapshot(double? thickness = ThicknessMm)
    {
        var value = thickness.HasValue
            ? LengthValue.FromIu(AltiumUnits.MmToIu(thickness.Value))
            : LengthValue.FromIu(null);
        return CreateSnapshot(
            value,
            new[]
            {
                Component("component-1", "U101", 240, 220, 0),
                Component("component-2", "U102", 280, 220, 90),
            });
    }

    private static BoardSnapshot CreateSnapshot(
        LengthValue thickness,
        IReadOnlyList<ComponentInstance> components,
        double originX = 0,
        double originY = 0)
    {
        var baseline = PlanTestFixture.Snapshot("base");
        return new BoardSnapshot
        {
            Source = baseline.Source,
            Properties = new BoardProperties
            {
                Id = "board-placement-test",
                Name = "Placement test",
                Thickness = thickness,
            },
            Outline = originX == 0 && originY == 0
                ? baseline.Outline
                : BuildOutline(originX, originY),
            Layers = baseline.Layers,
            Components = components,
        };
    }

    private static BoardOutline BuildOutline(double x, double y)
    {
        var x1 = AltiumUnits.MmToIu(x);
        var y1 = AltiumUnits.MmToIu(y);
        var x2 = AltiumUnits.MmToIu(x + 175);
        var y2 = AltiumUnits.MmToIu(y + 155);
        return new BoardOutline
        {
            Id = "outline-placement-test",
            Contours = new[]
            {
                new Contour
                {
                    Id = "outer",
                    Role = ContourRole.Outer,
                    Segments = new ContourSegment[]
                    {
                        Line(x1, y1, x2, y1),
                        Line(x2, y1, x2, y2),
                        Line(x2, y2, x1, y2),
                        Line(x1, y2, x1, y1),
                    },
                },
            },
        };
    }

    private static LineSegment Line(int x1, int y1, int x2, int y2) =>
        new()
        {
            Start = Point2D.FromIu(x1, y1),
            End = Point2D.FromIu(x2, y2),
        };

    private static ComponentInstance Component(
        string id,
        string designator,
        double x,
        double y,
        double rotation) =>
        new()
        {
            Id = id,
            Designator = designator,
            LayerId = "layer-top",
            Placement = new Transform
            {
                Position = Point2D.FromIu(AltiumUnits.MmToIu(x), AltiumUnits.MmToIu(y)),
                RotationDeg = rotation,
                Side = BoardSide.Top,
            },
        };

    private static double RotationAroundZ(Matrix4x4D matrix) =>
        Math.Atan2(matrix[1, 0], matrix[0, 0]) * 180.0 / Math.PI;

    private static string Describe(IReadOnlyList<SolidWorksDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(item => item.Code + " " + item.Path + ": " + item.Message));

    private static string Describe(IReadOnlyList<PackageDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(item => item.Code + " " + item.Path + ": " + item.Message));

    private sealed record TestPackage(PackageReadResult Read, string Directory);
}
