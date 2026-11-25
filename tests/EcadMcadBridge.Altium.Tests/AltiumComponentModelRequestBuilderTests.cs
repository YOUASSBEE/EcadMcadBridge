using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class AltiumComponentModelRequestBuilderTests
{
    [Fact]
    public void BuildsEntryForEverySnapshotComponent()
    {
        var snapshot = Snapshot();
        var bodies = new[]
        {
            Body("component-1", 0, ComponentModelAssetAcquisition.SdkExport, "body-1"),
            Body("component-1", 1, ComponentModelAssetAcquisition.NotAcquired, null),
            Body("component-3", 0, ComponentModelAssetAcquisition.NotAcquired, null),
        };
        var assets = new[]
        {
            new ComponentModelAssetSource
            {
                AssetKey = "body-1",
                SourcePath = "body-1.step",
                Format = ComponentModelAssetFormat.StepAp214,
            },
        };

        var request = AltiumComponentModelRequestBuilder.Build(snapshot, bodies, assets);

        Assert.Equal(3, request.Components.Count);
        var first = request.Components.Single(item => item.ComponentId == "component-1");
        Assert.Equal("R1", first.Designator);
        Assert.Equal(new[] { 0, 1 }, first.Bodies.Select(body => body.BodyOrdinal));
        Assert.Equal("body-1", first.Bodies[0].AssetKey);
        Assert.Equal(ComponentModelAssetAcquisition.SdkExport, first.Bodies[0].AssetAcquisition);
        Assert.Equal(ComponentBodyProjection.Top, first.Bodies[0].BodyProjection);
        Assert.Equal(0, first.Bodies[0].StandoffHeight.Iu);
        Assert.Equal(1_181_102, first.Bodies[0].OverallHeight.Iu);
        Assert.Equal(ComponentModelStateSource.PlacedBodyEffective, first.Bodies[0].ModelStateSource);
        Assert.Null(first.Bodies[1].AssetKey);
        Assert.Equal(ComponentModelAssetAcquisition.NotAcquired, first.Bodies[1].AssetAcquisition);

        var second = request.Components.Single(item => item.ComponentId == "component-2");
        Assert.Empty(second.Bodies);

        var third = request.Components.Single(item => item.ComponentId == "component-3");
        Assert.Single(third.Bodies);
        Assert.Single(request.Assets);
    }

    [Fact]
    public void MultiBodyComponentKeepsEveryBody()
    {
        var bodies = new[]
        {
            Body("component-1", 0, ComponentModelAssetAcquisition.SdkExport, "body-1"),
            Body("component-1", 1, ComponentModelAssetAcquisition.SdkExport, "body-2"),
            Body("component-1", 2, ComponentModelAssetAcquisition.SdkExport, "body-3"),
            Body("component-1", 3, ComponentModelAssetAcquisition.SdkExport, "body-4"),
        };

        var request = AltiumComponentModelRequestBuilder.Build(
            Snapshot(),
            bodies,
            Array.Empty<ComponentModelAssetSource>());

        var component = request.Components.Single(item => item.ComponentId == "component-1");
        Assert.Equal(4, component.Bodies.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, component.Bodies.Select(body => body.BodyOrdinal));
    }

    [Fact]
    public void RejectsUnknownComponent()
    {
        var bodies = new[]
        {
            Body("missing-component", 0, ComponentModelAssetAcquisition.NotAcquired, null),
        };

        Assert.Throws<InvalidOperationException>(() =>
            AltiumComponentModelRequestBuilder.Build(
                Snapshot(),
                bodies,
                Array.Empty<ComponentModelAssetSource>()));
    }

    [Fact]
    public void RejectsDuplicateBodyOrdinal()
    {
        var bodies = new[]
        {
            Body("component-1", 0, ComponentModelAssetAcquisition.NotAcquired, null),
            Body("component-1", 0, ComponentModelAssetAcquisition.NotAcquired, null),
        };

        Assert.Throws<InvalidOperationException>(() =>
            AltiumComponentModelRequestBuilder.Build(
                Snapshot(),
                bodies,
                Array.Empty<ComponentModelAssetSource>()));
    }

    [Fact]
    public void RejectsNegativeBodyOrdinal()
    {
        var bodies = new[]
        {
            Body("component-1", -1, ComponentModelAssetAcquisition.NotAcquired, null),
        };

        Assert.Throws<InvalidOperationException>(() =>
            AltiumComponentModelRequestBuilder.Build(
                Snapshot(),
                bodies,
                Array.Empty<ComponentModelAssetSource>()));
    }

    private static BoardSnapshot Snapshot() => ModelTestSnapshot.Create(
        ModelTestSnapshot.Seed("component-1", "R1"),
        ModelTestSnapshot.Seed("component-2", "C1"),
        ModelTestSnapshot.Seed("component-3", "U1"));

    private static AltiumBodyInventoryEntry Body(
        string componentId,
        int ordinal,
        ComponentModelAssetAcquisition acquisition,
        string? assetKey) =>
        new(
            componentId,
            ordinal,
            ComponentModelRepresentation.Unknown,
            ComponentModelSourceOrigin.Unknown,
            new ModelOrigin(),
            new ModelState(),
            null,
            acquisition,
            assetKey,
            ComponentBodyProjection.Top,
            LengthValue.FromIu(0),
            LengthValue.FromIu(1_181_102),
            ComponentModelStateSource.PlacedBodyEffective);
}
