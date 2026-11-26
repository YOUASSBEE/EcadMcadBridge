using System.Text;
using System.Text.Json;
using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Core.Packaging;

/// <summary>
/// Deterministic JSON for <see cref="BoardModelDescriptor"/>: UTF-8 without BOM, LF,
/// invariant numbers and a fixed field order.
/// </summary>
public static class BoardModelJson
{
    private static readonly UTF8Encoding Utf8NoBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Serialize(BoardModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", descriptor.Schema);
            writer.WriteString("schemaVersion", descriptor.SchemaVersion);
            writer.WriteString("assetPath", descriptor.AssetPath);
            writer.WriteString("assetFormat", FormatAssetFormat(descriptor.AssetFormat));
            writer.WriteString("sha256", descriptor.Sha256);
            writer.WriteNumber("size", descriptor.Size);
            writer.WriteString("units", descriptor.Units);

            writer.WriteStartObject("frame");
            writer.WriteString("origin", descriptor.Frame.Origin);
            writer.WriteString("xAxis", descriptor.Frame.XAxis);
            writer.WriteString("yAxis", descriptor.Frame.YAxis);
            writer.WriteString("zAxis", descriptor.Frame.ZAxis);
            writer.WriteString("substrateSide", descriptor.Frame.SubstrateSide);
            writer.WriteEndObject();

            writer.WriteStartObject("nominalThickness");
            WriteNullableNumber(writer, "iu", descriptor.NominalThickness.Iu);
            WriteNullableNumber(writer, "mm", descriptor.NominalThickness.Mm);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        var json = Utf8NoBom.GetString(buffer.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal);
        return json + "\n";
    }

    public static byte[] SerializeToUtf8(BoardModelDescriptor descriptor) =>
        Utf8NoBom.GetBytes(Serialize(descriptor));

    public static BoardModelDescriptor Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > 0 && json[0] == '\uFEFF')
            json = json[1..];

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Board model descriptor root must be a JSON object.");

        var frame = ChildObject(root, "frame");
        var thickness = ChildObject(root, "nominalThickness");
        return new BoardModelDescriptor
        {
            Schema = RequiredString(root, "schema"),
            SchemaVersion = RequiredString(root, "schemaVersion"),
            AssetPath = RequiredString(root, "assetPath"),
            AssetFormat = ParseAssetFormat(RequiredString(root, "assetFormat")),
            Sha256 = RequiredString(root, "sha256"),
            Size = RequiredLong(root, "size"),
            Units = RequiredString(root, "units"),
            Frame = new BoardModelFrame
            {
                Origin = RequiredString(frame, "origin"),
                XAxis = RequiredString(frame, "xAxis"),
                YAxis = RequiredString(frame, "yAxis"),
                ZAxis = RequiredString(frame, "zAxis"),
                SubstrateSide = RequiredString(frame, "substrateSide"),
            },
            NominalThickness = LengthValue.FromJson(
                RequiredNullableInt(thickness, "iu"),
                RequiredNullableDouble(thickness, "mm")),
        };
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, int? value)
    {
        if (value.HasValue)
            writer.WriteNumber(name, value.Value);
        else
            writer.WriteNull(name);
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value.HasValue)
            writer.WriteNumber(name, value.Value);
        else
            writer.WriteNull(name);
    }

    private static JsonElement ChildObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new JsonException($"'{name}' must be a JSON object.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new JsonException($"'{name}' must be a string.");
        return value.GetString() ?? string.Empty;
    }

    private static long RequiredLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var result))
        {
            throw new JsonException($"'{name}' must be a 64-bit integer.");
        }

        return result;
    }

    private static int? RequiredNullableInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
            throw new JsonException($"'{name}' is required.");
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw new JsonException($"'{name}' must be a 32-bit integer or null.");
        return result;
    }

    private static double? RequiredNullableDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
            throw new JsonException($"'{name}' is required.");
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new JsonException($"'{name}' must be a finite number or null.");
        return result;
    }

    private static string FormatAssetFormat(ComponentModelAssetFormat value) => value switch
    {
        ComponentModelAssetFormat.StepAp214 => "step-ap214",
        ComponentModelAssetFormat.ParasolidXT => "parasolid-x_t",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported asset format."),
    };

    private static ComponentModelAssetFormat ParseAssetFormat(string value) => value switch
    {
        "step-ap214" => ComponentModelAssetFormat.StepAp214,
        "parasolid-x_t" => ComponentModelAssetFormat.ParasolidXT,
        _ => throw new JsonException($"Unknown assetFormat '{value}'."),
    };
}
