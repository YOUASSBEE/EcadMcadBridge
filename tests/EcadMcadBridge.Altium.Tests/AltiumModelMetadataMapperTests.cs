using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Diagnostics;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class AltiumModelMetadataMapperTests
{
    [Fact]
    public void MapsOriginXAndYAsIuMmPairsAndLeavesZNull()
    {
        var origin = AltiumModelMetadataMapper.MapOrigin(1_000_000, -2_000_000);

        Assert.Equal(1_000_000, origin.XIu);
        Assert.Equal(AltiumUnits.IuToMm(1_000_000), origin.XMm);
        Assert.Equal(-2_000_000, origin.YIu);
        Assert.Equal(AltiumUnits.IuToMm(-2_000_000), origin.YMm);
        Assert.Null(origin.ZIu);
        Assert.Null(origin.ZMm);
    }

    [Fact]
    public void MissingOriginFieldsStayNull()
    {
        var origin = AltiumModelMetadataMapper.MapOrigin(null, null);

        Assert.Null(origin.XIu);
        Assert.Null(origin.XMm);
        Assert.Null(origin.YIu);
        Assert.Null(origin.YMm);
        Assert.Null(origin.ZIu);
        Assert.Null(origin.ZMm);
    }

    [Fact]
    public void PreservesRawAnglesIncluding360()
    {
        var state = AltiumModelMetadataMapper.MapState(30, 45, 360, 393_700);

        Assert.Equal(30, state.RotationXDegRaw);
        Assert.Equal(45, state.RotationYDegRaw);
        Assert.Equal(360, state.RotationZDegRaw);
        Assert.Equal(393_700, state.DzIu);
        Assert.Equal(AltiumUnits.IuToMm(393_700), state.DzMm);
    }

    [Fact]
    public void MissingStateStaysNull()
    {
        var state = AltiumModelMetadataMapper.MapState(null, null, null, null);

        Assert.Null(state.RotationXDegRaw);
        Assert.Null(state.RotationYDegRaw);
        Assert.Null(state.RotationZDegRaw);
        Assert.Null(state.DzIu);
        Assert.Null(state.DzMm);
    }

    [Fact]
    public void RotationIsSeparateAndFiniteOnly()
    {
        Assert.Equal(90, AltiumModelMetadataMapper.MapRotation(90));
        Assert.Null(AltiumModelMetadataMapper.MapRotation(double.NaN));
        Assert.Null(AltiumModelMetadataMapper.MapRotation(double.PositiveInfinity));
    }
}
