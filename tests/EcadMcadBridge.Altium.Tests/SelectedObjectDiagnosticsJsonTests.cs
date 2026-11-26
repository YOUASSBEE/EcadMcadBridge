using System.Text;
using System.Text.Json;
using EcadMcadBridge.Altium.Application;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class SelectedObjectDiagnosticsJsonTests
{
    private static readonly DateTimeOffset MeasuredAt =
        new(2026, 11, 26, 10, 11, 12, TimeSpan.Zero);

    [Fact]
    public void Serialize_WritesEmptySelectionEnvelope()
    {
        var json = SelectedObjectDiagnosticsJson.Serialize(Report(
            DiagnosticValue.Available("PCB"),
            Array.Empty<SelectedObjectDiagnostic>()));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("2026-11-26T10:11:12Z", root.GetProperty("measuredAtUtc").GetString());
        Assert.Equal("available", root.GetProperty("documentType").GetProperty("status").GetString());
        Assert.Equal("PCB", root.GetProperty("documentType").GetProperty("value").GetString());
        Assert.Equal(0, root.GetProperty("objectCount").GetInt32());
        Assert.Equal("available", root.GetProperty("readStatus").GetProperty("status").GetString());
        Assert.Empty(root.GetProperty("objects").EnumerateArray());
    }

    [Fact]
    public void Serialize_MarksUnavailablePropertiesWithReasons()
    {
        var unavailable = DiagnosticValue.Unavailable("SDK returned no value.");
        var record = new SelectedObjectDiagnostic(
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            DiagnosticCollection<BodyModelDiagnostic>.Unavailable("Not applicable to this object type."));

        var json = SelectedObjectDiagnosticsJson.Serialize(Report(
            DiagnosticValue.Available("PCB"),
            new[] { record }));

        using var document = JsonDocument.Parse(json);
        var item = document.RootElement.GetProperty("objects")[0];
        Assert.Equal("unavailable", item.GetProperty("objectType").GetProperty("status").GetString());
        Assert.Equal("SDK returned no value.", item.GetProperty("publicInstanceId").GetProperty("reason").GetString());
        Assert.Equal(
            "Not applicable to this object type.",
            item.GetProperty("bodyModels").GetProperty("reason").GetString());
        Assert.Empty(item.GetProperty("bodyModels").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void Serialize_WritesComponentSourceFieldsAndBodyModelLink()
    {
        var available = (string value) => DiagnosticValue.Available(value);
        var body = new BodyModelDiagnostic(
            available("body-1"),
            available("BODY1"),
            available("component-1"),
            available("U1"),
            available("part.step"),
            available("e3DModelType_Generic"));
        var record = new SelectedObjectDiagnostic(
            available("eComponentObject"),
            available("component-1"),
            available("U1"),
            available("R_0402"),
            available("source-1"),
            available("Capacitor - MLCC"),
            available("gortpowerlib"),
            available("vault-1"),
            available("item-1"),
            available("revision-1"),
            DiagnosticCollection<BodyModelDiagnostic>.Available(new[] { body }));

        var json = SelectedObjectDiagnosticsJson.Serialize(Report(
            DiagnosticValue.Available("PCB"),
            new[] { record }));

        using var document = JsonDocument.Parse(json);
        var item = document.RootElement.GetProperty("objects")[0];
        Assert.Equal("R_0402", item.GetProperty("pattern").GetProperty("value").GetString());
        Assert.Equal("source-1", item.GetProperty("source").GetProperty("sourceUniqueId").GetProperty("value").GetString());
        Assert.Equal(
            "revision-1",
            item.GetProperty("source").GetProperty("itemRevisionGuid").GetProperty("value").GetString());

        var model = item.GetProperty("bodyModels").GetProperty("items")[0];
        Assert.Equal("body-1", model.GetProperty("bodyInstanceId").GetProperty("value").GetString());
        Assert.Equal("U1", model.GetProperty("ownerIdentifier").GetProperty("value").GetString());
        Assert.Equal("part.step", model.GetProperty("modelFileName").GetProperty("value").GetString());
    }

    [Fact]
    public void Write_UsesTaskTempDirectoryAndLfUtf8()
    {
        var path = SelectedObjectDiagnosticsWriter.Write(Report(
            DiagnosticValue.Available("PCB"),
            Array.Empty<SelectedObjectDiagnostic>()));

        try
        {
            var expected = Path.Combine(
                Path.GetTempPath(),
                "EcadMcadBridge",
                "BRIDGE-000031",
                "diagnostics.json");
            Assert.Equal(expected, path);

            var bytes = File.ReadAllBytes(path);
            Assert.DoesNotContain((byte)'\r', bytes);
            Assert.StartsWith("{", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Context_AcceptsPcbDocumentWithProvenBoard()
    {
        Assert.True(SelectedObjectDiagnosticsReader.TryValidateContext(
            DiagnosticValue.Available("PCB"),
            boardOwnershipProven: true,
            out var reason));
        Assert.Null(reason);
    }

    [Fact]
    public void Context_RejectsIncompleteDocumentContext()
    {
        Assert.False(SelectedObjectDiagnosticsReader.TryValidateContext(
            DiagnosticValue.Unavailable("No owner document was supplied by the host."),
            boardOwnershipProven: true,
            out var reason));
        Assert.Contains("Document context is unavailable", reason);
    }

    [Fact]
    public void Context_RejectsNonPcbDocument()
    {
        Assert.False(SelectedObjectDiagnosticsReader.TryValidateContext(
            DiagnosticValue.Available("SCH"),
            boardOwnershipProven: true,
            out var reason));
        Assert.Contains("not a PCB document", reason);
    }

    [Fact]
    public void Context_RejectsUnprovenDocumentBoardLinkAndSerializesReason()
    {
        Assert.False(SelectedObjectDiagnosticsReader.TryValidateContext(
            DiagnosticValue.Available("PCB"),
            boardOwnershipProven: false,
            out var reason));
        Assert.Contains("cannot be proven", reason);

        var report = new SelectedObjectDiagnosticsReport(
            "1.0",
            MeasuredAt,
            DiagnosticValue.Available("PCB"),
            0,
            0,
            0,
            DiagnosticValue.Unavailable(reason!),
            Array.Empty<SelectedObjectDiagnostic>());

        using var document = JsonDocument.Parse(SelectedObjectDiagnosticsJson.Serialize(report));
        var status = document.RootElement.GetProperty("readStatus");
        Assert.Equal("unavailable", status.GetProperty("status").GetString());
        Assert.Equal(reason, status.GetProperty("reason").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("objectCount").GetInt32());
    }

    private static SelectedObjectDiagnosticsReport Report(
        DiagnosticValue documentType,
        IReadOnlyList<SelectedObjectDiagnostic> objects) =>
        new(
            "1.0",
            MeasuredAt,
            documentType,
            objects.Count,
            objects.Count,
            objects.Count,
            DiagnosticValue.Available("Selection count unchanged."),
            objects);
}
