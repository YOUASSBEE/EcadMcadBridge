using EcadMcadBridge.Altium.Adapter;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class AltiumModelStateClassifierTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 2)]
    [InlineData(null, false, 3)]
    [InlineData(false, null, 3)]
    [InlineData(null, null, 3)]
    [InlineData(true, null, 1)]
    [InlineData(null, true, 2)]
    public void ClassifiesTriStateModelState(
        bool? isEmpty,
        bool? isStale,
        int expected)
    {
        Assert.Equal((AltiumModelStateStatus)expected, AltiumModelStateClassifier.Classify(isEmpty, isStale));
    }
}
