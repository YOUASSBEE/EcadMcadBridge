using EcadMcadBridge.Core.Diagnostics;
using Xunit;

namespace EcadMcadBridge.Core.Tests;

public sealed class AltiumUnitsTests
{
    [Theory]
    [InlineData(1_000_000, 2.54)]
    [InlineData(2_000_000, 5.08)]
    [InlineData(10_000_000, 25.4)]
    public void IuToMm_UsesControlValues(int iu, double expectedMm) =>
        Assert.Equal(expectedMm, AltiumUnits.IuToMm(iu), precision: 10);

    [Theory]
    [InlineData(2.54, 1_000_000)]
    [InlineData(5.08, 2_000_000)]
    [InlineData(25.4, 10_000_000)]
    public void MmToIu_UsesControlValues(double mm, int expectedIu) =>
        Assert.Equal(expectedIu, AltiumUnits.MmToIu(mm));

    [Fact]
    public void MmToIu_RoundTripsIu()
    {
        foreach (var iu in new[] { 0, 1, -1, 1_000_000, 2_000_000, 10_000_000 })
            Assert.Equal(iu, AltiumUnits.MmToIu(AltiumUnits.IuToMm(iu)));
    }
}
