using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using PCB;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class PadShapeMappingTests
{
    [Theory]
    [InlineData(TShape.eRectangular, PadShapeKind.Rectangular)]
    [InlineData(TShape.eCircleShape, PadShapeKind.Circular)]
    public void ExactShapes_MapWithoutSimplification(TShape shape, PadShapeKind expected)
    {
        var actual = AltiumBoardSnapshotAdapter.MapPadShape(shape, 10, 10, out var simplified);

        Assert.Equal(expected, actual);
        Assert.False(simplified);
    }

    [Fact]
    public void RoundShapeWithEqualSides_IsCircular()
    {
        var actual = AltiumBoardSnapshotAdapter.MapPadShape(TShape.eRounded, 10, 10, out var simplified);

        Assert.Equal(PadShapeKind.Circular, actual);
        Assert.False(simplified);
    }

    [Theory]
    [InlineData(TShape.eOctagonal)]
    [InlineData(TShape.eRoundRectShape)]
    [InlineData(TShape.eRoundedRectangular)]
    public void KnownApproximations_AreFlagged(TShape shape)
    {
        var actual = AltiumBoardSnapshotAdapter.MapPadShape(shape, 10, 8, out var simplified);

        Assert.NotNull(actual);
        Assert.True(simplified);
    }

    [Fact]
    public void RoundedOval_IsFlaggedAsSimplified()
    {
        var actual = AltiumBoardSnapshotAdapter.MapPadShape(TShape.eRounded, 12, 6, out var simplified);

        Assert.Equal(PadShapeKind.RoundedRectangle, actual);
        Assert.True(simplified);
    }

    [Theory]
    [InlineData(TShape.eRotatedRectShape)]
    [InlineData(TShape.eArcShape)]
    [InlineData(TShape.eTerminator)]
    [InlineData(TShape.eCustomShape)]
    [InlineData(TShape.eNoShape)]
    public void UnsupportedShapes_AreNotGuessed(TShape shape)
    {
        var actual = AltiumBoardSnapshotAdapter.MapPadShape(shape, 10, 8, out var simplified);

        Assert.Null(actual);
        Assert.False(simplified);
    }
}
