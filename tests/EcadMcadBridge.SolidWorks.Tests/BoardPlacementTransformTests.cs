using EcadMcadBridge.SolidWorks.Importing;
using EcadMcadBridge.SolidWorks.Placement;
using Xunit;

namespace EcadMcadBridge.SolidWorks.Tests;

public sealed class BoardPlacementTransformTests
{
    [Fact]
    public void AlignsMeasuredTopAndBoardLocalOrigin()
    {
        var geometry = Geometry(minX: -2, minY: 3, minZ: -2.028, maxZ: 0, thickness: 2.028);

        var result = BoardPlacementTransformBuilder.Build(2.00800208, geometry);

        Assert.True(result.IsSupported);
        Assert.Equal(2.0, result.TranslationXMm, 9);
        Assert.Equal(-3.0, result.TranslationYMm, 9);
        Assert.Equal(1.00400104, result.TranslationZMm, 9);
        Assert.Equal(1.00400104, result.ResultingTopSurfaceZMm, 9);
        var point = result.Transform!.Value.TransformPoint(new EcadMcadBridge.Core.Placement.Vector3Mm(0, 0, 0));
        Assert.Equal(2.0, point.X, 9);
        Assert.Equal(-3.0, point.Y, 9);
        Assert.Equal(1.00400104, point.Z, 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void RejectsInvalidNominalThickness(double thickness)
    {
        var result = BoardPlacementTransformBuilder.Build(
            thickness,
            Geometry(0, 0, -2, 0, 2));

        Assert.False(result.IsSupported);
        Assert.Contains(result.Diagnostics, item => item.Code == "SW-BRD-020");
    }

    [Fact]
    public void RejectsNonFiniteMeasuredBounds()
    {
        var result = BoardPlacementTransformBuilder.Build(
            2.0,
            Geometry(double.NaN, 0, -2, 0, 2));

        Assert.False(result.IsSupported);
        Assert.Contains(result.Diagnostics, item => item.Code == "SW-BRD-021");
    }

    private static SolidWorksBoardGeometry Geometry(
        double minX,
        double minY,
        double minZ,
        double maxZ,
        double thickness) =>
        new(
            1,
            minX,
            minY,
            minZ,
            10,
            20,
            maxZ,
            10 - minX,
            20 - minY,
            thickness,
            2.0,
            thickness - 2.0,
            100,
            100,
            0,
            10 - minX,
            20 - minY,
            0,
            0,
            Array.Empty<SolidWorksBoardProbe>());
}
