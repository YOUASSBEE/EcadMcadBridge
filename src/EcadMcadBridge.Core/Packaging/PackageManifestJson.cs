using System.Globalization;
using System.Text;
using System.Text.Json;

namespace EcadMcadBridge.Core.Packaging;

/// <summary>Deterministic manifest JSON: UTF-8 without BOM, LF, invariant culture.</summary>
public static class PackageManifestJson
{
    public static string Serialize(PackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", manifest.Schema);
            writer.WriteString("schemaVersion", manifest.SchemaVersion);
            writer.WriteString("packageId", manifest.PackageId.ToString("D"));
            writer.WriteString(
                "createdAtUtc",
                manifest.CreatedAtUtc.UtcDateTime.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                    CultureInfo.InvariantCulture));

            writer.WriteStartObject("producer");
            writer.WriteString("component", manifest.Producer.Component);
            writer.WriteString("version", manifest.Producer.Version);
            writer.WriteEndObject();

            writer.WriteStartObject("source");
            writer.WriteString("cad", manifest.Source.Cad);
            writer.WriteString("documentSha256", manifest.Source.DocumentSha256);
            writer.WriteEndObject();

            writer.WriteString("sourceUnits", manifest.SourceUnits);
            writer.WriteString("units", manifest.Units);

            writer.WriteStartArray("files");
            foreach (var file in manifest.Files.OrderBy(item => item.Path, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", file.Path);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteNumber("size", file.Size);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("capabilities");
            foreach (var capability in manifest.Capabilities
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(item => item, StringComparer.Ordinal))
            {
                writer.WriteStringValue(capability);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        var json = Encoding.UTF8.GetString(buffer.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal);
        return json + "\n";
    }

    public static byte[] SerializeToUtf8(PackageManifest manifest) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Serialize(manifest));

    public static PackageManifest Deserialize(string json)
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
            throw new JsonException("Package manifest root must be a JSON object.");

        var producer = RequiredObject(root, "producer");
        var source = RequiredObject(root, "source");
        return new PackageManifest
        {
            Schema = RequiredString(root, "schema"),
            SchemaVersion = RequiredString(root, "schemaVersion"),
            PackageId = ReadGuid(root, "packageId"),
            CreatedAtUtc = ReadDateTime(root, "createdAtUtc"),
            Producer = new PackageProducer
            {
                Component = RequiredString(producer, "component"),
                Version = RequiredString(producer, "version"),
            },
            Source = new PackageSource
            {
                Cad = RequiredString(source, "cad"),
                DocumentSha256 = RequiredString(source, "documentSha256"),
            },
            SourceUnits = RequiredString(root, "sourceUnits"),
            Units = RequiredString(root, "units"),
            Files = ReadFiles(root),
            Capabilities = ReadCapabilities(root),
        };
    }

    private static IReadOnlyList<PackageFileEntry> ReadFiles(JsonElement root)
    {
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            throw new JsonException("'files' must be an array.");

        var result = new List<PackageFileEntry>();
        foreach (var item in files.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new JsonException("'files' must contain JSON objects.");

            if (!item.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number
                || !size.TryGetInt64(out var sizeValue) || sizeValue < 0)
            {
                throw new JsonException("A file 'size' must be a non-negative integer.");
            }

            result.Add(new PackageFileEntry
            {
                Path = RequiredString(item, "path"),
                Sha256 = RequiredString(item, "sha256"),
                Size = sizeValue,
            });
        }

        return result;
    }

    private static IReadOnlyList<string> ReadCapabilities(JsonElement root)
    {
        if (!root.TryGetProperty("capabilities", out var capabilities)
            || capabilities.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("'capabilities' must be an array.");
        }

        var result = new List<string>();
        foreach (var item in capabilities.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new JsonException("'capabilities' must contain strings.");
            result.Add(item.GetString() ?? string.Empty);
        }

        return result;
    }

    private static Guid ReadGuid(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (!Guid.TryParse(value, out var result))
            throw new JsonException($"'{name}' must be a UUID.");
        return result;
    }

    private static DateTimeOffset ReadDateTime(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var result))
        {
            throw new JsonException($"'{name}' must be an ISO-8601 timestamp.");
        }

        return result;
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
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
}
