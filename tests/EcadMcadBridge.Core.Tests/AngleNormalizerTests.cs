using EcadMcadBridge.Core.Diagnostics;
using Xunit;

namespace EcadMcadBridge.Core.Tests;

public sealed class AngleNormalizerTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(90.0, 90.0)]
    [InlineData(180.0, -180.0)]
    [InlineData(-180.0, -180.0)]
    [InlineData(270.0, -90.0)]
    [InlineData(450.0, 90.0)]
    [InlineData(-450.0, -90.0)]
    public void NormalizeDegrees_UsesHalfOpenRange(double input, double expected)
    {
        Assert.Equal(expected, AngleNormalizer.NormalizeDegrees(input), precision: 10);
    }

    [Fact]
    public void NormalizeDegrees_RejectsNonFiniteValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AngleNormalizer.NormalizeDegrees(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => AngleNormalizer.NormalizeDegrees(double.PositiveInfinity));
    }
}
