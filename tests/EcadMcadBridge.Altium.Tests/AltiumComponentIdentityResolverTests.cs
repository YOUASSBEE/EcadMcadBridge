using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class AltiumComponentIdentityResolverTests
{
    [Fact]
    public void ResolvesByUniqueId()
    {
        var resolver = Build(Identity("component-1", uniqueId: "UID-1"));
        var result = resolver.Resolve(Key(uniqueId: "UID-1", designator: "changed"));

        Assert.Equal(AltiumComponentResolutionStatus.Resolved, result.Status);
        Assert.Equal("component-1", result.ComponentId);
        Assert.Equal(AltiumComponentResolutionMethod.UniqueId, result.Method);
    }

    [Fact]
    public void ResolvesBySourceUniqueId()
    {
        var resolver = Build(Identity("component-1", sourceUniqueId: "SRC-1"));
        var result = resolver.Resolve(Key(sourceUniqueId: "SRC-1", designator: "changed"));

        Assert.Equal(AltiumComponentResolutionStatus.Resolved, result.Status);
        Assert.Equal("component-1", result.ComponentId);
        Assert.Equal(AltiumComponentResolutionMethod.SourceUniqueId, result.Method);
    }

    [Fact]
    public void ResolvesByUniqueFallback()
    {
        var resolver = Build(Identity("component-1", designator: "R1", x: 10, y: 20, rotation: 90));
        var result = resolver.Resolve(Key(designator: "R1", x: 10, y: 20, rotation: 90));

        Assert.Equal(AltiumComponentResolutionStatus.Resolved, result.Status);
        Assert.Equal("component-1", result.ComponentId);
        Assert.Equal(AltiumComponentResolutionMethod.Fallback, result.Method);
    }

    [Fact]
    public void RejectsAmbiguousFallback()
    {
        var resolver = Build(
            Identity("component-1", designator: "R1", x: 10, y: 20),
            Identity("component-2", designator: "R1", x: 10, y: 20));
        var result = resolver.Resolve(Key(designator: "R1", x: 10, y: 20));

        Assert.Equal(AltiumComponentResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.ComponentId);
    }

    [Fact]
    public void RejectsUnknownOwner()
    {
        var resolver = Build(Identity("component-1", designator: "R1", x: 10, y: 20));
        var result = resolver.Resolve(Key(designator: "R2", x: 10, y: 20));

        Assert.Equal(AltiumComponentResolutionStatus.Unknown, result.Status);
        Assert.Null(result.ComponentId);
    }

    [Fact]
    public void DuplicateUniqueKeyDoesNotOverwriteOrPickLast()
    {
        var resolver = Build(
            Identity("component-1", uniqueId: "UID-DUP"),
            Identity("component-2", uniqueId: "UID-DUP"));
        var result = resolver.Resolve(Key(uniqueId: "UID-DUP", designator: "anything"));

        Assert.Equal(AltiumComponentResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.ComponentId);
    }

    [Fact]
    public void DuplicateSameIdentityStaysResolved()
    {
        var resolver = Build(
            Identity("component-1", uniqueId: "UID-1"),
            Identity("component-1", uniqueId: "UID-1"));
        var result = resolver.Resolve(Key(uniqueId: "UID-1"));

        Assert.Equal(AltiumComponentResolutionStatus.Resolved, result.Status);
        Assert.Equal("component-1", result.ComponentId);
    }

    private static AltiumComponentIdentityResolver Build(params AltiumComponentIdentity[] identities) =>
        AltiumComponentIdentityResolver.Build(identities);

    private static AltiumComponentIdentity Identity(
        string componentId,
        string? uniqueId = null,
        string? sourceUniqueId = null,
        string designator = "R1",
        int x = 1,
        int y = 2,
        double rotation = 0,
        BoardSide side = BoardSide.Top) =>
        new(
            componentId,
            Key(uniqueId, sourceUniqueId, designator, x, y, rotation, side));

    private static AltiumComponentKey Key(
        string? uniqueId = null,
        string? sourceUniqueId = null,
        string designator = "R1",
        int x = 1,
        int y = 2,
        double rotation = 0,
        BoardSide side = BoardSide.Top) =>
        new(uniqueId, sourceUniqueId, designator, x, y, rotation, side);
}
