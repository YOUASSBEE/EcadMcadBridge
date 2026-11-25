using EcadMcadBridge.SolidWorks.Interop;
using SolidWorks.Interop.swconst;
using Xunit;

namespace EcadMcadBridge.SolidWorks.Tests;

public sealed class SolidWorksInteropTests
{
    [Fact]
    public void DecodesAnimatorCameraViewsWarning128()
    {
        var flags = SolidWorksWarningDecoder.DecodeSaveWarnings(128);

        var flag = Assert.Single(flags);
        Assert.Equal(128, flag.Value);
        Assert.Equal("swFileSaveWarning_AnimatorCameraViews", flag.Name);
        Assert.Contains("AnimatorCameraViews", SolidWorksWarningDecoder.Describe(flags, 128));
    }

    [Fact]
    public void DecodesCombinedSaveWarnings()
    {
        var flags = SolidWorksWarningDecoder.DecodeSaveWarnings(1 | 128);

        Assert.Contains(flags, item => item.Name == "swFileSaveWarning_RebuildError");
        Assert.Contains(flags, item => item.Name == "swFileSaveWarning_AnimatorCameraViews");
    }

    [Fact]
    public void DecodesAlreadyOpenLoadWarning()
    {
        var flags = SolidWorksWarningDecoder.DecodeLoadWarnings(
            (int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen);

        var flag = Assert.Single(flags);
        Assert.Equal(128, flag.Value);
        Assert.Equal("swFileLoadWarning_AlreadyOpen", flag.Name);
    }

    [Fact]
    public void ZeroWarningsDecodeToEmpty()
    {
        Assert.Empty(SolidWorksWarningDecoder.DecodeSaveWarnings(0));
        Assert.Empty(SolidWorksWarningDecoder.DecodeLoadWarnings(0));
    }

    [Fact]
    public void PathComparisonIsCaseInsensitiveAndNormalized()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var path = Path.Combine(temp.Path, "part.SLDPRT");
        var alternate = path.Replace('\\', '/').ToUpperInvariant();

        Assert.True(SolidWorksPath.Same(path, alternate));
        Assert.True(SolidWorksPath.Same(SolidWorksPath.Normalize(path), SolidWorksPath.Normalize(alternate)));
        Assert.Null(SolidWorksPath.Normalize(" "));
    }

    [Fact]
    public void ExistingTargetIsRejectedWithoutOverwrite()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var path = Path.Combine(temp.Path, "existing.SLDPRT");
        File.WriteAllText(path, "existing");

        var allowed = SolidWorksFileGuard.TryValidateNewTarget(path, null!, out var reason);

        Assert.False(allowed);
        Assert.Contains("already exists", reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SaveResultRequiresNoErrorsAndNonEmptyFile()
    {
        using var temp = new PlanTestFixture.TempDirectory();
        var path = Path.Combine(temp.Path, "saved.SLDPRT");
        File.WriteAllText(path, "part");

        Assert.True(SolidWorksFileGuard.IsSaveSuccessful(true, 0, path));
        Assert.False(SolidWorksFileGuard.IsSaveSuccessful(true, 2, path));
        Assert.False(SolidWorksFileGuard.IsSaveSuccessful(false, 0, path));
        Assert.False(SolidWorksFileGuard.IsSaveSuccessful(true, 0, Path.Combine(temp.Path, "missing.SLDPRT")));
    }
}
