using EcadMcadBridge.Altium.Adapter;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class AltiumBoardModelExporterTests
{
    [Fact]
    public void Export_RejectsMissingBoard()
    {
        using var temp = new TestTempDirectory();

        var result = AltiumBoardModelExporter.Export(null!, temp.Path);

        Assert.False(result.IsSuccess);
        Assert.Null(result.StepPath);
        Assert.Contains("board", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Export_RejectsMissingDirectory()
    {
        var result = AltiumBoardModelExporter.Export(null!, string.Empty);

        Assert.False(result.IsSuccess);
        Assert.Contains("directory", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));\nENDSEC;\n",
        true)]
    [InlineData("ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('CONFIG_CONTROL_DESIGN'));\nENDSEC;\n", false)]
    [InlineData("not a step file", false)]
    public void HasAp214Header_RequiresAutomotiveDesign214(string content, bool expected)
    {
        using var temp = new TestTempDirectory();
        var path = Path.Combine(temp.Path, "probe.step");
        File.WriteAllText(path, content);

        Assert.Equal(expected, AltiumBoardModelExporter.HasAp214Header(path));
    }
}
