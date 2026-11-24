using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using Xunit;

namespace EcadMcadBridge.Core.Tests;

public sealed class SmokeSnapshotJsonTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Serialize_UsesFixedFieldOrderLfAndNoCr()
    {
        var json = SmokeSnapshotJson.Serialize(new SmokeSnapshot
        {
            DocumentFileName = "board.PcbDoc",
            ExportedAtUtc = Timestamp,
        });

        var expectedOrder = new[]
        {
            "schema", "schemaVersion", "cad", "cadVersion", "documentFileName", "sourceUnits",
            "units", "sampleCoordinate", "componentCount", "outlinePointCount", "exportedAtUtc", "warnings",
        };
        var previous = -1;
        foreach (var field in expectedOrder)
        {
            var index = json.IndexOf('"' + field + '"', StringComparison.Ordinal);
            Assert.True(index >= 0, $"Missing field: {field}");
            Assert.True(index > previous, $"Field out of order: {field}");
            previous = index;
        }

        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        Assert.EndsWith("\n", json, StringComparison.Ordinal);
        Assert.Contains("\"exportedAtUtc\": \"2026-11-24T00:00:00Z\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_EscapesDocumentFileName()
    {
        var json = SmokeSnapshotJson.Serialize(new SmokeSnapshot
        {
            DocumentFileName = "board\"x\\.PcbDoc",
            ExportedAtUtc = Timestamp,
        });

        Assert.Contains("\"documentFileName\": \"board\\u0022x\\\\.PcbDoc\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_ContainsWarnings()
    {
        var json = SmokeSnapshotJson.Serialize(new SmokeSnapshot
        {
            DocumentFileName = "board.PcbDoc",
            ExportedAtUtc = Timestamp,
            Warnings = new[] { "Board outline contains no contours." },
        });

        Assert.Contains("\"warnings\": [", json, StringComparison.Ordinal);
        Assert.Contains("Board outline contains no contours.", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_CreatesUtf8FileWithoutBomInTempDirectory()
    {
        var path = SnapshotFileWriter.Write(new SmokeSnapshot
        {
            DocumentFileName = "board.PcbDoc",
            ExportedAtUtc = Timestamp,
        });

        try
        {
            Assert.Equal("EcadMcadBridge", Path.GetFileName(Path.GetDirectoryName(path)));
            Assert.Contains("20261124T000000000Z", Path.GetFileName(path), StringComparison.Ordinal);

            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Assert.Contains("\"componentCount\": 0", System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
