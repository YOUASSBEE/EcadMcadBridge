using EcadMcadBridge.Altium.Adapter;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class AltiumStepExportFileTests
{
    [Fact]
    public void AcceptsNonEmptyStepFile()
    {
        using var temp = new TestTempDirectory();
        var path = temp.File("body.step");
        File.WriteAllText(path, "ISO-10303-21;\nEND-ISO-10303-21;\n");

        Assert.True(AltiumStepExportFile.IsValid(path));
    }

    [Fact]
    public void RejectsEmptyStepFile()
    {
        using var temp = new TestTempDirectory();
        var path = temp.File("body.step");
        File.WriteAllBytes(path, Array.Empty<byte>());

        Assert.False(AltiumStepExportFile.IsValid(path));
    }

    [Fact]
    public void RejectsWrongExtension()
    {
        using var temp = new TestTempDirectory();
        var path = temp.File("body.txt");
        File.WriteAllText(path, "not a step");

        Assert.False(AltiumStepExportFile.IsValid(path));
    }

    [Fact]
    public void RejectsMissingFile()
    {
        using var temp = new TestTempDirectory();

        Assert.False(AltiumStepExportFile.IsValid(temp.File("missing.step")));
    }

    [Fact]
    public void DeletesPartialFile()
    {
        using var temp = new TestTempDirectory();
        var path = temp.File("body.step");
        File.WriteAllText(path, "partial");

        AltiumStepExportFile.TryDelete(path);

        Assert.False(File.Exists(path));
    }
}
