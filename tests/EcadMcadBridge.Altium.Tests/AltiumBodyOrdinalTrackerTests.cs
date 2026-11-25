using EcadMcadBridge.Altium.Adapter;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class AltiumBodyOrdinalTrackerTests
{
    [Fact]
    public void OrdinalsAreIndependentPerComponent()
    {
        var tracker = new AltiumBodyOrdinalTracker();

        Assert.Equal(0, tracker.Next("component-1"));
        Assert.Equal(0, tracker.Next("component-2"));
        Assert.Equal(1, tracker.Next("component-1"));
        Assert.Equal(1, tracker.Next("component-2"));
        Assert.Equal(2, tracker.Next("component-1"));
    }
}
