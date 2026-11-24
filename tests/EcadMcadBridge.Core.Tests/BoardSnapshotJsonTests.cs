using System.Text.Json.Nodes;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Serialization;
using EcadMcadBridge.Core.Validation;
using Xunit;

namespace EcadMcadBridge.Core.Tests;

public sealed class BoardSnapshotJsonTests
{
    [Fact]
    public void Serialize_AllTypes_RoundTripsExactly()
    {
        var snapshot = BoardSnapshotFixture.CreateValid();
        var json = BoardSnapshotJson.Serialize(snapshot);

        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        Assert.EndsWith("\n", json, StringComparison.Ordinal);
        Assert.Contains("\"schema\": \"ecad-mcad-bridge/board-snapshot\"", json, StringComparison.Ordinal);
        Assert.Contains("\"padStacks\": [", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"arc\"", json, StringComparison.Ordinal);
        Assert.Contains("\"role\": \"hole\"", json, StringComparison.Ordinal);
        Assert.Contains("\"vertices\": [", json, StringComparison.Ordinal);

        var roundTrip = BoardSnapshotJson.Deserialize(json);
        var serializedAgain = BoardSnapshotJson.Serialize(roundTrip);
        Assert.Equal(json, serializedAgain);
    }

    [Fact]
    public void SerializeToUtf8_HasNoBom()
    {
        var bytes = BoardSnapshotJson.SerializeToUtf8(BoardSnapshotFixture.CreateValid());

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
    }

    [Fact]
    public void Serialize_UsesFixedTopLevelFieldOrder()
    {
        var json = BoardSnapshotJson.Serialize(BoardSnapshotFixture.CreateValid());
        var fields = new[]
        {
            "schema", "schemaVersion", "source", "sourceUnits", "units", "board",
            "layers", "components", "padStacks", "pads", "nets", "tracks", "vias", "regions", "texts",
        };

        var previous = -1;
        foreach (var field in fields)
        {
            var index = json.IndexOf('"' + field + '"', StringComparison.Ordinal);
            Assert.True(index >= 0, $"Missing field: {field}");
            Assert.True(index > previous, $"Field out of order: {field}");
            previous = index;
        }

        var xIu = json.IndexOf("\"xIu\"", StringComparison.Ordinal);
        var yIu = json.IndexOf("\"yIu\"", StringComparison.Ordinal);
        var xMm = json.IndexOf("\"xMm\"", StringComparison.Ordinal);
        var yMm = json.IndexOf("\"yMm\"", StringComparison.Ordinal);
        Assert.True(xIu >= 0 && xIu < yIu && yIu < xMm && xMm < yMm);
    }

    [Fact]
    public void Serialize_IsDeterministicAndSortsCollectionsById()
    {
        var normal = BoardSnapshotJson.Serialize(BoardSnapshotFixture.CreateValid());
        var repeated = BoardSnapshotJson.Serialize(BoardSnapshotFixture.CreateValid());
        var reversed = BoardSnapshotJson.Serialize(BoardSnapshotFixture.CreateValidReversed());

        Assert.Equal(normal, repeated);
        Assert.Equal(normal, reversed);
    }

    [Fact]
    public void Serialize_WritesNullableFieldsAsNull()
    {
        var json = BoardSnapshotJson.Serialize(BoardSnapshotFixture.CreateMinimal());

        Assert.Contains("\"name\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"material\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"thicknessIu\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"thicknessMm\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"comment\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"footprint\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"heightIu\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"heightMm\": null", json, StringComparison.Ordinal);

        var roundTrip = BoardSnapshotJson.Deserialize(json);
        Assert.Null(roundTrip.Properties.Name);
        Assert.False(roundTrip.Properties.Thickness.HasValue);
        Assert.Null(roundTrip.Components[0].Comment);
        Assert.False(roundTrip.Components[0].Height.HasValue);
    }

    [Fact]
    public void Serialize_EmptyCollections_RoundTripsAsEmptyArrays()
    {
        var json = BoardSnapshotJson.Serialize(BoardSnapshotFixture.CreateEmpty());

        Assert.Contains("\"layers\": []", json, StringComparison.Ordinal);
        Assert.Contains("\"components\": []", json, StringComparison.Ordinal);
        Assert.Contains("\"padStacks\": []", json, StringComparison.Ordinal);
        Assert.Contains("\"pads\": []", json, StringComparison.Ordinal);
        Assert.Contains("\"nets\": []", json, StringComparison.Ordinal);
        Assert.Contains("\"tracks\": []", json, StringComparison.Ordinal);
        Assert.Contains("\"vias\": []", json, StringComparison.Ordinal);
        Assert.Contains("\"regions\": []", json, StringComparison.Ordinal);
        Assert.Contains("\"texts\": []", json, StringComparison.Ordinal);

        var roundTrip = BoardSnapshotJson.Deserialize(json);
        Assert.Empty(roundTrip.Layers);
        Assert.Empty(roundTrip.Components);
        Assert.Empty(roundTrip.PadStacks);
        Assert.Empty(roundTrip.Pads);
        Assert.Empty(roundTrip.Nets);
        Assert.Empty(roundTrip.Tracks);
        Assert.Empty(roundTrip.Vias);
        Assert.Empty(roundTrip.Regions);
        Assert.Empty(roundTrip.Texts);
    }

    [Fact]
    public void Serialize_UsesD015ControlValuesForPointPairs()
    {
        var snapshot = BoardSnapshotFixture.CreateMinimal();
        var component = new ComponentInstance
        {
            Id = "component-units",
            Designator = "U1",
            LayerId = "layer-top",
            Placement = new Transform
            {
                Position = Point2D.FromIu(1_000_000, 2_000_000),
                RotationDeg = 0,
                Side = BoardSide.Top,
            },
        };

        var json = BoardSnapshotJson.Serialize(
            BoardSnapshotFixture.With(snapshot, components: new[] { component }));

        Assert.Contains("\"xIu\": 1000000", json, StringComparison.Ordinal);
        Assert.Contains("\"xMm\": 2.54", json, StringComparison.Ordinal);
        Assert.Contains("\"yIu\": 2000000", json, StringComparison.Ordinal);
        Assert.Contains("\"yMm\": 5.08", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Contours_KeepLineAndArcSegmentsAndHoles()
    {
        var snapshot = BoardSnapshotFixture.CreateValid();
        var json = BoardSnapshotJson.Serialize(snapshot);

        Assert.Contains("\"kind\": \"line\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"arc\"", json, StringComparison.Ordinal);
        Assert.Contains("\"center\": {", json, StringComparison.Ordinal);
        Assert.Contains("\"radiusIu\": 5000000", json, StringComparison.Ordinal);
        Assert.Contains("\"sweepDirection\": \"counterClockwise\"", json, StringComparison.Ordinal);
        Assert.Contains("\"role\": \"outer\"", json, StringComparison.Ordinal);
        Assert.Contains("\"role\": \"hole\"", json, StringComparison.Ordinal);

        Assert.Equal(2, snapshot.Outline.Contours.Count);
        Assert.Single(snapshot.Outline.Contours, contour => contour.Role == ContourRole.Hole);
        Assert.Equal(2, snapshot.Regions[0].Polygon.Contours.Count);
        Assert.Single(snapshot.Regions[0].Polygon.Contours, contour => contour.Role == ContourRole.Hole);
    }

    [Fact]
    public void Deserialize_InconsistentUnitPair_IsReportedByValidator()
    {
        var json = BoardSnapshotJson.Serialize(BoardSnapshotFixture.CreateValid());
        var root = JsonNode.Parse(json)!.AsObject();
        var position = root["components"]!.AsArray()[0]!["placement"]!["position"]!.AsObject();
        position["xMm"] = 999.0;

        var snapshot = BoardSnapshotJson.Deserialize(root.ToJsonString());
        var result = BoardSnapshotValidator.Validate(snapshot);

        Assert.Contains(
            result.Errors,
            diagnostic => diagnostic.Code == BoardDiagnosticCodes.InconsistentUnitPair
                && diagnostic.Path == "components[0].placement.position.x");
        Assert.Throws<InvalidOperationException>(() => BoardSnapshotJson.Serialize(snapshot));
    }
}
