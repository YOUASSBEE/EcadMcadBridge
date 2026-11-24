using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Core.Diagnostics;

/// <summary>Deterministic JSON for the diagnostic snapshot: UTF-8, LF, fixed field order.</summary>
public static class SmokeSnapshotJson
{
    public static string Serialize(SmokeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", snapshot.Schema);
            writer.WriteString("schemaVersion", snapshot.SchemaVersion);
            writer.WriteString("cad", snapshot.Cad);
            writer.WriteString("cadVersion", snapshot.CadVersion);
            writer.WriteString("documentFileName", snapshot.DocumentFileName);
            writer.WriteString("sourceUnits", snapshot.SourceUnits);
            writer.WriteString("units", snapshot.Units);

            writer.WriteStartObject("sampleCoordinate");
            writer.WriteNumber("xIu", snapshot.SampleCoordinate.XIu);
            writer.WriteNumber("yIu", snapshot.SampleCoordinate.YIu);
            writer.WriteNumber("xMm", snapshot.SampleCoordinate.XMm);
            writer.WriteNumber("yMm", snapshot.SampleCoordinate.YMm);
            writer.WriteEndObject();

            writer.WriteNumber("componentCount", snapshot.ComponentCount);
            writer.WriteNumber("outlinePointCount", snapshot.OutlinePointCount);
            writer.WriteString(
                "exportedAtUtc",
                snapshot.ExportedAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

            writer.WriteStartArray("warnings");
            foreach (var warning in snapshot.Warnings)
                writer.WriteStringValue(warning);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }
}
