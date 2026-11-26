using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace EcadMcadBridge.Altium.Application;

internal sealed record DiagnosticValue(string Status, string? Value, string? Reason)
{
    public static DiagnosticValue Available(string value) => new("available", value, null);

    public static DiagnosticValue Unavailable(string reason) => new("unavailable", null, reason);
}

internal sealed record DiagnosticCollection<T>(string Status, string? Reason, IReadOnlyList<T> Items)
{
    public static DiagnosticCollection<T> Available(IReadOnlyList<T> items) => new("available", null, items);

    public static DiagnosticCollection<T> Unavailable(string reason) =>
        new("unavailable", reason, Array.Empty<T>());
}

internal sealed record BodyModelDiagnostic(
    DiagnosticValue BodyInstanceId,
    DiagnosticValue BodyIdentifier,
    DiagnosticValue OwnerInstanceId,
    DiagnosticValue OwnerIdentifier,
    DiagnosticValue ModelFileName,
    DiagnosticValue ModelType);

internal sealed record SelectedObjectDiagnostic(
    DiagnosticValue ObjectType,
    DiagnosticValue PublicInstanceId,
    DiagnosticValue Identifier,
    DiagnosticValue Pattern,
    DiagnosticValue SourceUniqueId,
    DiagnosticValue SourceFootprintLibrary,
    DiagnosticValue SourceComponentLibrary,
    DiagnosticValue VaultGuid,
    DiagnosticValue ItemGuid,
    DiagnosticValue ItemRevisionGuid,
    DiagnosticCollection<BodyModelDiagnostic> BodyModels);

internal sealed record SelectedObjectDiagnosticsReport(
    string SchemaVersion,
    DateTimeOffset MeasuredAtUtc,
    DiagnosticValue DocumentType,
    int ObjectCount,
    int SelectionCountBefore,
    int SelectionCountAfter,
    DiagnosticValue ReadStatus,
    IReadOnlyList<SelectedObjectDiagnostic> Objects);

internal static class SelectedObjectDiagnosticsJson
{
    public static string Serialize(SelectedObjectDiagnosticsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", report.SchemaVersion);
            writer.WriteString(
                "measuredAtUtc",
                report.MeasuredAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            WriteValue(writer, "documentType", report.DocumentType);
            writer.WriteNumber("objectCount", report.ObjectCount);
            writer.WriteNumber("selectionCountBefore", report.SelectionCountBefore);
            writer.WriteNumber("selectionCountAfter", report.SelectionCountAfter);
            WriteValue(writer, "readStatus", report.ReadStatus);

            writer.WriteStartArray("objects");
            foreach (var item in report.Objects)
            {
                writer.WriteStartObject();
                WriteValue(writer, "objectType", item.ObjectType);
                WriteValue(writer, "publicInstanceId", item.PublicInstanceId);
                WriteValue(writer, "identifier", item.Identifier);
                WriteValue(writer, "pattern", item.Pattern);

                writer.WriteStartObject("source");
                WriteValue(writer, "sourceUniqueId", item.SourceUniqueId);
                WriteValue(writer, "sourceFootprintLibrary", item.SourceFootprintLibrary);
                WriteValue(writer, "sourceComponentLibrary", item.SourceComponentLibrary);
                WriteValue(writer, "vaultGuid", item.VaultGuid);
                WriteValue(writer, "itemGuid", item.ItemGuid);
                WriteValue(writer, "itemRevisionGuid", item.ItemRevisionGuid);
                writer.WriteEndObject();

                WriteBodyModels(writer, item.BodyModels);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, DiagnosticValue value)
    {
        writer.WriteStartObject(name);
        writer.WriteString("status", value.Status);
        if (value.Status == "available")
            writer.WriteString("value", value.Value);
        else
            writer.WriteString("reason", value.Reason);
        writer.WriteEndObject();
    }

    private static void WriteBodyModels(Utf8JsonWriter writer, DiagnosticCollection<BodyModelDiagnostic> collection)
    {
        writer.WriteStartObject("bodyModels");
        writer.WriteString("status", collection.Status);
        if (collection.Status != "available")
            writer.WriteString("reason", collection.Reason);

        writer.WriteStartArray("items");
        if (collection.Status == "available")
        {
            foreach (var body in collection.Items)
            {
                writer.WriteStartObject();
                WriteValue(writer, "bodyInstanceId", body.BodyInstanceId);
                WriteValue(writer, "bodyIdentifier", body.BodyIdentifier);
                WriteValue(writer, "ownerInstanceId", body.OwnerInstanceId);
                WriteValue(writer, "ownerIdentifier", body.OwnerIdentifier);
                WriteValue(writer, "modelFileName", body.ModelFileName);
                WriteValue(writer, "modelType", body.ModelType);
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

internal static class SelectedObjectDiagnosticsWriter
{
    public const string DirectoryName = "BRIDGE-000031";
    public const string FileName = "diagnostics.json";

    public static string Write(SelectedObjectDiagnosticsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var directory = Path.Combine(Path.GetTempPath(), "EcadMcadBridge", DirectoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        File.WriteAllText(path, SelectedObjectDiagnosticsJson.Serialize(report), new UTF8Encoding(false));
        return path;
    }
}
