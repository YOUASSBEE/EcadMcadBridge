using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class AltiumComponentIdBuilderTests
{
    [Fact]
    public void DuplicateUniqueIdGetsDistinctIdsButResolverIsAmbiguous()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var firstId = AltiumComponentIdBuilder.Build(
            "DUP", null, "R1", 0, used, warnings, new AltiumAdapterDiagnostics());
        var secondId = AltiumComponentIdBuilder.Build(
            "DUP", null, "R2", 1, used, warnings, new AltiumAdapterDiagnostics());

        Assert.NotEqual(firstId, secondId);
        var resolver = AltiumComponentIdentityResolver.Build(new[]
        {
            new AltiumComponentIdentity(firstId, Key("DUP", null, "R1", 0)),
            new AltiumComponentIdentity(secondId, Key("DUP", null, "R2", 1)),
        });

        var result = resolver.Resolve(Key("DUP", null, "R1", 0));

        Assert.Equal(AltiumComponentResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.ComponentId);
    }

    [Fact]
    public void DuplicateSourceUniqueIdWithoutUniqueIdAlsoStaysAmbiguous()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var firstId = AltiumComponentIdBuilder.Build(
            null, "SRC-DUP", "R1", 0, used, warnings, new AltiumAdapterDiagnostics());
        var secondId = AltiumComponentIdBuilder.Build(
            null, "SRC-DUP", "R2", 1, used, warnings, new AltiumAdapterDiagnostics());

        Assert.NotEqual(firstId, secondId);
        var resolver = AltiumComponentIdentityResolver.Build(new[]
        {
            new AltiumComponentIdentity(firstId, Key(null, "SRC-DUP", "R1", 0)),
            new AltiumComponentIdentity(secondId, Key(null, "SRC-DUP", "R2", 1)),
        });

        var result = resolver.Resolve(Key(null, "SRC-DUP", "R1", 0));

        Assert.Equal(AltiumComponentResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.ComponentId);
    }

    [Fact]
    public void AmbiguousDuplicateOwnerIsRejectedBeforePublishing()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var firstId = AltiumComponentIdBuilder.Build(
            "DUP", null, "R1", 0, used, warnings, new AltiumAdapterDiagnostics());
        var secondId = AltiumComponentIdBuilder.Build(
            "DUP", null, "R2", 1, used, warnings, new AltiumAdapterDiagnostics());
        var resolver = AltiumComponentIdentityResolver.Build(new[]
        {
            new AltiumComponentIdentity(firstId, Key("DUP", null, "R1", 0)),
            new AltiumComponentIdentity(secondId, Key("DUP", null, "R2", 1)),
        });

        var result = resolver.Resolve(Key("DUP", null, "R1", 0));

        Assert.Throws<AltiumModelInventoryException>(() =>
            AltiumComponentResolutionGuard.RequireComponentId(result));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespaceSourceUniqueIdIsAbsent(string sourceUniqueId)
    {
        Assert.Null(AltiumComponentIdBuilder.NormalizeKey(sourceUniqueId));
        var used = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var id = AltiumComponentIdBuilder.Build(
            null, sourceUniqueId, "R1", 0, used, warnings, new AltiumAdapterDiagnostics());

        Assert.Equal("altium:component:fallback:R1:0", id);
    }

    [Fact]
    public void ComponentsWithoutBothIdentifiersGetDistinctFallbackIds()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var firstId = AltiumComponentIdBuilder.Build(
            null, null, "R1", 0, used, warnings, new AltiumAdapterDiagnostics());
        var secondId = AltiumComponentIdBuilder.Build(
            null, null, "R1", 1, used, warnings, new AltiumAdapterDiagnostics());

        Assert.NotEqual(firstId, secondId);
        Assert.Contains(":0", firstId, StringComparison.Ordinal);
        Assert.Contains(":1", secondId, StringComparison.Ordinal);
    }

    [Fact]
    public void UniqueIdentifiersKeepTheEstablishedShape()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();

        var id = AltiumComponentIdBuilder.Build(
            "UID-1", "SRC-1", "R1", 0, used, warnings, new AltiumAdapterDiagnostics());

        Assert.Equal("altium:component:UID-1", id);
    }

    private static AltiumComponentKey Key(
        string? uniqueId,
        string? sourceUniqueId,
        string designator,
        int index) =>
        new(uniqueId, sourceUniqueId, designator, index, index + 1, 0, BoardSide.Top);
}
