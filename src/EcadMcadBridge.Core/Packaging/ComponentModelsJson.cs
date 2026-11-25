using System.Text;
using System.Text.Json;
using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Core.Packaging;

/// <summary>
/// Deterministic JSON for <see cref="ComponentModelCatalog"/>: UTF-8 without BOM, LF,
/// invariant numbers, stable component/body order and a fixed field order.
/// </summary>
public static class ComponentModelsJson
{
    private static readonly UTF8Encoding Utf8NoBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Serialize(ComponentModelCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", catalog.Schema);
            writer.WriteString("schemaVersion", catalog.SchemaVersion);

            writer.WriteStartArray("components");
            foreach (var component in catalog.Components
                         .OrderBy(item => item.ComponentId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("componentId", component.ComponentId);
                writer.WriteString("designator", component.Designator);
                writer.WriteStartArray("bodies");
                foreach (var body in component.Bodies.OrderBy(item => item.BodyOrdinal))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("bodyOrdinal", body.BodyOrdinal);
                    writer.WriteString("representation", FormatRepresentation(body.Representation));
                    writer.WriteString("sourceOrigin", FormatSourceOrigin(body.SourceOrigin));
                    writer.WriteString("assetAcquisition", FormatAssetAcquisition(body.AssetAcquisition));
                    WriteNullableString(writer, "assetFormat", FormatAssetFormat(body.AssetFormat));
                    WriteNullableString(writer, "assetPath", body.AssetPath);

                    writer.WriteStartObject("sdkOrigin");
                    WriteNullableNumber(writer, "xIu", body.Origin.XIu);
                    WriteNullableNumber(writer, "xMm", body.Origin.XMm);
                    WriteNullableNumber(writer, "yIu", body.Origin.YIu);
                    WriteNullableNumber(writer, "yMm", body.Origin.YMm);
                    WriteNullableNumber(writer, "zIu", body.Origin.ZIu);
                    WriteNullableNumber(writer, "zMm", body.Origin.ZMm);
                    writer.WriteEndObject();

                    writer.WriteStartObject("sdkState");
                    WriteNullableNumber(writer, "rotationXDegRaw", body.State.RotationXDegRaw);
                    WriteNullableNumber(writer, "rotationYDegRaw", body.State.RotationYDegRaw);
                    WriteNullableNumber(writer, "rotationZDegRaw", body.State.RotationZDegRaw);
                    WriteNullableNumber(writer, "dzIu", body.State.DzIu);
                    WriteNullableNumber(writer, "dzMm", body.State.DzMm);
                    writer.WriteEndObject();

                    WriteNullableNumber(writer, "modelRotationDegRaw", body.ModelRotationDegRaw);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var json = Utf8NoBom.GetString(buffer.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal);
        return json + "\n";
    }

    public static byte[] SerializeToUtf8(ComponentModelCatalog catalog) =>
        Utf8NoBom.GetBytes(Serialize(catalog));

    public static ComponentModelCatalog Deserialize(string json)
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
            throw new JsonException("Component model catalog root must be a JSON object.");

        var components = ChildArray(root, "components");
        var result = new List<ComponentModelEntry>();
        foreach (var component in components.EnumerateArray())
        {
            if (component.ValueKind != JsonValueKind.Object)
                throw new JsonException("'components' must contain JSON objects.");

            var bodies = new List<ComponentBodyEntry>();
            foreach (var body in ChildArray(component, "bodies").EnumerateArray())
                bodies.Add(ReadBody(body));

            result.Add(new ComponentModelEntry
            {
                ComponentId = RequiredString(component, "componentId"),
                Designator = RequiredString(component, "designator"),
                Bodies = bodies,
            });
        }

        return new ComponentModelCatalog
        {
            Schema = RequiredString(root, "schema"),
            SchemaVersion = RequiredString(root, "schemaVersion"),
            Components = result,
        };
    }

    private static ComponentBodyEntry ReadBody(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            throw new JsonException("'bodies' must contain JSON objects.");

        var origin = ChildObject(body, "sdkOrigin");
        var state = ChildObject(body, "sdkState");
        return new ComponentBodyEntry
        {
            BodyOrdinal = RequiredInt(body, "bodyOrdinal"),
            Representation = ParseRepresentation(RequiredString(body, "representation")),
            SourceOrigin = ParseSourceOrigin(RequiredString(body, "sourceOrigin")),
            AssetAcquisition = ParseAssetAcquisition(RequiredString(body, "assetAcquisition")),
            AssetFormat = ParseAssetFormat(OptionalString(body, "assetFormat")),
            AssetPath = OptionalString(body, "assetPath"),
            Origin = new ModelOrigin
            {
                XIu = NullableInt(origin, "xIu"),
                XMm = NullableDouble(origin, "xMm"),
                YIu = NullableInt(origin, "yIu"),
                YMm = NullableDouble(origin, "yMm"),
                ZIu = NullableInt(origin, "zIu"),
                ZMm = NullableDouble(origin, "zMm"),
            },
            State = new ModelState
            {
                RotationXDegRaw = NullableDouble(state, "rotationXDegRaw"),
                RotationYDegRaw = NullableDouble(state, "rotationYDegRaw"),
                RotationZDegRaw = NullableDouble(state, "rotationZDegRaw"),
                DzIu = NullableInt(state, "dzIu"),
                DzMm = NullableDouble(state, "dzMm"),
            },
            ModelRotationDegRaw = NullableDouble(body, "modelRotationDegRaw"),
        };
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
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

    private static JsonElement ChildArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new JsonException($"'{name}' must be an array.");
        return value;
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

    private static string? OptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new JsonException($"'{name}' must be a string or null.");
        return value.GetString();
    }

    private static int RequiredInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result))
        {
            throw new JsonException($"'{name}' must be a 32-bit integer.");
        }

        return result;
    }

    private static int? NullableInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw new JsonException($"'{name}' must be a 32-bit integer or null.");
        return result;
    }

    private static double? NullableDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new JsonException($"'{name}' must be a finite number or null.");
        return result;
    }

    private static string FormatRepresentation(ComponentModelRepresentation value) => value switch
    {
        ComponentModelRepresentation.Generic => "generic",
        ComponentModelRepresentation.Native => "native",
        ComponentModelRepresentation.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported representation."),
    };

    private static string FormatSourceOrigin(ComponentModelSourceOrigin value) => value switch
    {
        ComponentModelSourceOrigin.Embedded => "embedded",
        ComponentModelSourceOrigin.Native => "native",
        ComponentModelSourceOrigin.Linked => "linked",
        ComponentModelSourceOrigin.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported source origin."),
    };

    private static string FormatAssetAcquisition(ComponentModelAssetAcquisition value) => value switch
    {
        ComponentModelAssetAcquisition.SdkExport => "sdk-export",
        ComponentModelAssetAcquisition.SourceCopyVerified => "source-copy-verified",
        ComponentModelAssetAcquisition.NotAcquired => "not-acquired",
        ComponentModelAssetAcquisition.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported asset acquisition."),
    };

    private static string? FormatAssetFormat(ComponentModelAssetFormat? value) => value switch
    {
        null => null,
        ComponentModelAssetFormat.StepAp214 => "step-ap214",
        ComponentModelAssetFormat.ParasolidXT => "parasolid-x_t",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported asset format."),
    };

    private static ComponentModelRepresentation ParseRepresentation(string value) => value switch
    {
        "generic" => ComponentModelRepresentation.Generic,
        "native" => ComponentModelRepresentation.Native,
        "unknown" => ComponentModelRepresentation.Unknown,
        _ => throw new JsonException($"Unknown representation '{value}'."),
    };

    private static ComponentModelSourceOrigin ParseSourceOrigin(string value) => value switch
    {
        "embedded" => ComponentModelSourceOrigin.Embedded,
        "native" => ComponentModelSourceOrigin.Native,
        "linked" => ComponentModelSourceOrigin.Linked,
        "unknown" => ComponentModelSourceOrigin.Unknown,
        _ => throw new JsonException($"Unknown sourceOrigin '{value}'."),
    };

    private static ComponentModelAssetAcquisition ParseAssetAcquisition(string value) => value switch
    {
        "sdk-export" => ComponentModelAssetAcquisition.SdkExport,
        "source-copy-verified" => ComponentModelAssetAcquisition.SourceCopyVerified,
        "not-acquired" => ComponentModelAssetAcquisition.NotAcquired,
        "unknown" => ComponentModelAssetAcquisition.Unknown,
        _ => throw new JsonException($"Unknown assetAcquisition '{value}'."),
    };

    private static ComponentModelAssetFormat? ParseAssetFormat(string? value) => value switch
    {
        null => null,
        "step-ap214" => ComponentModelAssetFormat.StepAp214,
        "parasolid-x_t" => ComponentModelAssetFormat.ParasolidXT,
        _ => throw new JsonException($"Unknown assetFormat '{value}'."),
    };
}
