using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Core.Serialization;

/// <summary>
/// Deterministic JSON contract for <see cref="BoardSnapshot"/>: UTF-8 without BOM, LF,
/// invariant numbers and a fixed field order. The diagnostic smoke format is separate.
/// </summary>
public static class BoardSnapshotJson
{
    public static string Serialize(BoardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(snapshot.SourceUnits, "altium-iu", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported sourceUnits '{snapshot.SourceUnits}'.");
        if (!string.Equals(snapshot.Units, "mm", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported units '{snapshot.Units}'.");

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", BoardSnapshot.SchemaName);
            writer.WriteString("schemaVersion", BoardSnapshot.SchemaVersionValue);

            writer.WritePropertyName("source");
            WriteSource(writer, snapshot.Source ?? new BoardSnapshotSource());

            writer.WriteString("sourceUnits", snapshot.SourceUnits);
            writer.WriteString("units", snapshot.Units);

            writer.WriteStartObject("board");
            writer.WritePropertyName("properties");
            WriteBoardProperties(writer, snapshot.Properties ?? new BoardProperties());
            writer.WritePropertyName("outline");
            WriteBoardOutline(writer, snapshot.Outline ?? new BoardOutline());
            writer.WriteEndObject();

            WriteArray(writer, "layers", snapshot.Layers, layer => layer.Id, WriteLayerFields);
            WriteArray(writer, "components", snapshot.Components, component => component.Id, WriteComponentFields);
            WriteArray(writer, "padStacks", snapshot.PadStacks, stack => stack.Id, WritePadStackFields);
            WriteArray(writer, "pads", snapshot.Pads, pad => pad.Id, WritePadFields);
            WriteArray(writer, "nets", snapshot.Nets, net => net.Id, WriteNetFields);
            WriteArray(writer, "tracks", snapshot.Tracks, track => track.Id, WriteTrackFields);
            WriteArray(writer, "vias", snapshot.Vias, via => via.Id, WriteViaFields);
            WriteArray(writer, "regions", snapshot.Regions, region => region.Id, WriteRegionFields);
            WriteArray(writer, "texts", snapshot.Texts, text => text.Id, WriteTextFields);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    /// <summary>UTF-8 bytes without a BOM, suitable for writing the exchange snapshot.</summary>
    public static byte[] SerializeToUtf8(BoardSnapshot snapshot) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Serialize(snapshot));

    public static BoardSnapshot Deserialize(string json)
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
            throw new JsonException("Board snapshot root must be a JSON object.");

        var schema = ReadString(root, "schema");
        if (!string.Equals(schema, BoardSnapshot.SchemaName, StringComparison.Ordinal))
            throw new JsonException($"Unsupported schema '{schema}'.");

        var version = ReadString(root, "schemaVersion");
        if (!string.Equals(version, BoardSnapshot.SchemaVersionValue, StringComparison.Ordinal))
            throw new JsonException($"Unsupported schema version '{version}'.");

        var sourceUnits = ReadString(root, "sourceUnits")
            ?? throw new JsonException("sourceUnits is required.");
        if (!string.Equals(sourceUnits, "altium-iu", StringComparison.Ordinal))
            throw new JsonException($"Unsupported sourceUnits '{sourceUnits}'.");

        var units = ReadString(root, "units")
            ?? throw new JsonException("units is required.");
        if (!string.Equals(units, "mm", StringComparison.Ordinal))
            throw new JsonException($"Unsupported units '{units}'.");

        var board = ChildObject(root, "board");
        return new BoardSnapshot
        {
            Source = ReadSource(ChildObject(root, "source")),
            SourceUnits = sourceUnits,
            Units = units,
            Properties = ReadBoardProperties(ChildObject(board, "properties")),
            Outline = ReadBoardOutline(ChildObject(board, "outline")),
            Layers = ReadArray(root, "layers", ReadLayer),
            Components = ReadArray(root, "components", ReadComponent),
            PadStacks = ReadArray(root, "padStacks", ReadPadStack),
            Pads = ReadArray(root, "pads", ReadPad),
            Nets = ReadArray(root, "nets", ReadNet),
            Tracks = ReadArray(root, "tracks", ReadTrack),
            Vias = ReadArray(root, "vias", ReadVia),
            Regions = ReadArray(root, "regions", ReadRegion),
            Texts = ReadArray(root, "texts", ReadText),
        };
    }

    private static void WriteSource(Utf8JsonWriter writer, BoardSnapshotSource source)
    {
        writer.WriteStartObject();
        writer.WriteString("cad", source.Cad);
        WriteNullableString(writer, "cadVersion", source.CadVersion);
        WriteNullableString(writer, "documentFileName", source.DocumentFileName);
        WriteNullableDateTime(writer, "exportedAtUtc", source.ExportedAtUtc);
        WriteNullableString(writer, "toolVersion", source.ToolVersion);
        writer.WriteEndObject();
    }

    private static void WriteBoardProperties(Utf8JsonWriter writer, BoardProperties properties)
    {
        writer.WriteStartObject();
        writer.WriteString("id", properties.Id);
        WriteNullableString(writer, "name", properties.Name);
        WriteNullableString(writer, "material", properties.Material);
        WriteLengthPair(writer, "thickness", properties.Thickness);
        writer.WriteEndObject();
    }

    private static void WriteBoardOutline(Utf8JsonWriter writer, BoardOutline outline)
    {
        writer.WriteStartObject();
        writer.WriteString("id", outline.Id);
        WriteContours(writer, "contours", outline.Contours);
        writer.WriteEndObject();
    }

    private static void WriteLayerFields(Utf8JsonWriter writer, LayerRef layer)
    {
        writer.WriteString("id", layer.Id);
        writer.WriteString("name", layer.Name);
        WriteNullableEnum(writer, "kind", layer.Kind, FormatLayerKind);
        WriteNullableEnum(writer, "side", layer.Side, FormatBoardSide);
    }

    private static void WriteComponentFields(Utf8JsonWriter writer, ComponentInstance component)
    {
        writer.WriteString("id", component.Id);
        writer.WriteString("designator", component.Designator);
        WriteNullableString(writer, "comment", component.Comment);
        WriteNullableString(writer, "footprint", component.Footprint);
        WriteNullableString(writer, "sourceUniqueId", component.SourceUniqueId);
        writer.WriteString("layerId", component.LayerId);

        writer.WritePropertyName("placement");
        WriteTransform(writer, component.Placement ?? new Transform());

        WriteLengthPair(writer, "height", component.Height);
        WriteNullableBool(writer, "fitted", component.Fitted);
    }

    private static void WriteTransform(Utf8JsonWriter writer, Transform transform)
    {
        writer.WriteStartObject();
        WritePoint(writer, "position", transform.Position);
        WriteNullableDouble(writer, "rotationDeg", transform.RotationDeg);
        WriteNullableEnum(writer, "side", transform.Side, FormatBoardSide);
        writer.WriteEndObject();
    }

    private static void WritePadStackFields(Utf8JsonWriter writer, PadStack stack)
    {
        writer.WriteString("id", stack.Id);
        WriteArray(writer, "shapes", stack.Shapes, shape => shape.Id, WritePadShapeFields);
    }

    private static void WritePadShapeFields(Utf8JsonWriter writer, PadShape shape)
    {
        writer.WriteString("id", shape.Id);
        WriteNullableEnum(writer, "kind", shape.Kind, FormatPadShapeKind);
        WriteNullableString(writer, "layerId", shape.LayerId);
        WriteLengthPair(writer, "width", shape.Width);
        WriteLengthPair(writer, "height", shape.Height);
        WriteLengthPair(writer, "cornerRadius", shape.CornerRadius);

        writer.WriteStartArray("vertices");
        foreach (var vertex in shape.Vertices)
        {
            writer.WriteStartObject();
            WritePointValue(writer, vertex);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WritePadFields(Utf8JsonWriter writer, Pad pad)
    {
        writer.WriteString("id", pad.Id);
        writer.WriteString("componentId", pad.ComponentId);
        WriteNullableString(writer, "netId", pad.NetId);
        writer.WriteString("layerId", pad.LayerId);
        WriteNullableString(writer, "padStackId", pad.PadStackId);
        WritePoint(writer, "position", pad.Position);
        WriteNullableDouble(writer, "rotationDeg", pad.RotationDeg);
    }

    private static void WriteNetFields(Utf8JsonWriter writer, Net net)
    {
        writer.WriteString("id", net.Id);
        writer.WriteString("name", net.Name);
        WriteNullableString(writer, "netClass", net.NetClass);
    }

    private static void WriteTrackFields(Utf8JsonWriter writer, Track track)
    {
        writer.WriteString("id", track.Id);
        WriteNullableString(writer, "netId", track.NetId);
        writer.WriteString("layerId", track.LayerId);
        WritePoint(writer, "start", track.Start);
        WritePoint(writer, "end", track.End);
        WriteLengthPair(writer, "width", track.Width);
    }

    private static void WriteViaFields(Utf8JsonWriter writer, Via via)
    {
        writer.WriteString("id", via.Id);
        WriteNullableString(writer, "netId", via.NetId);
        WritePoint(writer, "position", via.Position);
        WriteLengthPair(writer, "diameter", via.Diameter);
        WriteLengthPair(writer, "holeDiameter", via.HoleDiameter);
        writer.WriteString("startLayerId", via.StartLayerId);
        writer.WriteString("endLayerId", via.EndLayerId);
    }

    private static void WriteRegionFields(Utf8JsonWriter writer, Region region)
    {
        writer.WriteString("id", region.Id);
        WriteNullableString(writer, "netId", region.NetId);
        writer.WriteString("layerId", region.LayerId);
        WriteNullableEnum(writer, "kind", region.Kind, FormatRegionKind);

        writer.WritePropertyName("polygon");
        WritePolygon(writer, region.Polygon ?? new PolygonGeometry());
    }

    private static void WriteTextFields(Utf8JsonWriter writer, TextItem text)
    {
        writer.WriteString("id", text.Id);
        WriteNullableString(writer, "componentId", text.ComponentId);
        writer.WriteString("layerId", text.LayerId);
        WritePoint(writer, "position", text.Position);
        writer.WriteString("text", text.Text);
        WriteLengthPair(writer, "height", text.Height);
        WriteLengthPair(writer, "strokeWidth", text.StrokeWidth);
        WriteNullableDouble(writer, "rotationDeg", text.RotationDeg);
    }

    private static void WritePolygon(Utf8JsonWriter writer, PolygonGeometry polygon)
    {
        writer.WriteStartObject();
        WriteContours(writer, "contours", polygon.Contours);
        writer.WriteEndObject();
    }

    private static void WriteContours(Utf8JsonWriter writer, string name, IEnumerable<Contour>? contours)
    {
        writer.WriteStartArray(name);
        if (contours is not null)
        {
            foreach (var contour in contours.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                WriteContourFields(writer, contour);
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();
    }

    private static void WriteContourFields(Utf8JsonWriter writer, Contour contour)
    {
        writer.WriteString("id", contour.Id);
        WriteNullableEnum(writer, "role", contour.Role, FormatContourRole);

        writer.WriteStartArray("segments");
        foreach (var segment in contour.Segments)
        {
            writer.WriteStartObject();
            switch (segment)
            {
                case LineSegment line:
                    writer.WriteString("kind", "line");
                    WritePoint(writer, "start", line.Start);
                    WritePoint(writer, "end", line.End);
                    break;
                case ArcSegment arc:
                    writer.WriteString("kind", "arc");
                    WritePoint(writer, "start", arc.Start);
                    WritePoint(writer, "end", arc.End);
                    WritePoint(writer, "center", arc.Center);
                    WriteLengthPair(writer, "radius", arc.Radius);
                    WriteNullableEnum(writer, "sweepDirection", arc.SweepDirection, FormatArcSweepDirection);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported contour segment type '{segment.GetType().Name}'.");
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WritePoint(Utf8JsonWriter writer, string name, Point2D point)
    {
        writer.WriteStartObject(name);
        WritePointValue(writer, point);
        writer.WriteEndObject();
    }

    private static void WritePointValue(Utf8JsonWriter writer, Point2D point)
    {
        if (!point.X.HasConsistentMm(1e-9) || !point.Y.HasConsistentMm(1e-9))
            throw new InvalidOperationException("IU and mm values are inconsistent for a point.");

        if (point.X.Iu is int xIu)
            writer.WriteNumber("xIu", xIu);
        else
            writer.WriteNull("xIu");

        if (point.Y.Iu is int yIu)
            writer.WriteNumber("yIu", yIu);
        else
            writer.WriteNull("yIu");

        if (point.X.Mm is double xMm)
            WriteFiniteNumber(writer, "xMm", xMm);
        else
            writer.WriteNull("xMm");

        if (point.Y.Mm is double yMm)
            WriteFiniteNumber(writer, "yMm", yMm);
        else
            writer.WriteNull("yMm");
    }

    private static void WriteLengthPair(Utf8JsonWriter writer, string prefix, LengthValue value)
    {
        if (!value.HasConsistentMm(1e-9))
            throw new InvalidOperationException($"IU and mm values are inconsistent for '{prefix}'.");

        if (value.Iu is int iu)
            writer.WriteNumber(prefix + "Iu", iu);
        else
            writer.WriteNull(prefix + "Iu");

        if (value.Mm is double mm)
            WriteFiniteNumber(writer, prefix + "Mm", mm);
        else
            writer.WriteNull(prefix + "Mm");
    }

    private static void WriteArray<T>(
        Utf8JsonWriter writer,
        string name,
        IEnumerable<T>? items,
        Func<T, string> id,
        Action<Utf8JsonWriter, T> writeFields)
    {
        writer.WriteStartArray(name);
        if (items is not null)
        {
            foreach (var item in items.OrderBy(id, StringComparer.Ordinal))
            {
                if (item is null)
                    continue;

                writer.WriteStartObject();
                writeFields(writer, item);
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
    }

    private static void WriteNullableBool(Utf8JsonWriter writer, string name, bool? value)
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteBoolean(name, value.Value);
    }

    private static void WriteNullableDouble(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is null)
            writer.WriteNull(name);
        else
            WriteFiniteNumber(writer, name, value.Value);
    }

    private static void WriteNullableDateTime(Utf8JsonWriter writer, string name, DateTimeOffset? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteString(
            name,
            value.Value.UtcDateTime.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                CultureInfo.InvariantCulture));
    }

    private static void WriteNullableEnum<T>(
        Utf8JsonWriter writer,
        string name,
        T? value,
        Func<T, string> format)
        where T : struct
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, format(value.Value));
    }

    private static void WriteFiniteNumber(Utf8JsonWriter writer, string name, double value)
    {
        if (!double.IsFinite(value))
            throw new InvalidOperationException($"JSON cannot represent a non-finite value for '{name}'.");

        writer.WriteNumber(name, value);
    }

    private static BoardSnapshotSource ReadSource(JsonElement element)
    {
        return new BoardSnapshotSource
        {
            Cad = ReadString(element, "cad") ?? "altium",
            CadVersion = ReadString(element, "cadVersion"),
            DocumentFileName = ReadString(element, "documentFileName"),
            ExportedAtUtc = ReadDateTime(element, "exportedAtUtc"),
            ToolVersion = ReadString(element, "toolVersion"),
        };
    }

    private static BoardProperties ReadBoardProperties(JsonElement element)
    {
        return new BoardProperties
        {
            Id = ReadString(element, "id") ?? string.Empty,
            Name = ReadString(element, "name"),
            Material = ReadString(element, "material"),
            Thickness = ReadLengthPair(element, "thicknessIu", "thicknessMm"),
        };
    }

    private static BoardOutline ReadBoardOutline(JsonElement element)
    {
        return new BoardOutline
        {
            Id = ReadString(element, "id") ?? string.Empty,
            Contours = ReadContours(element),
        };
    }

    private static LayerRef ReadLayer(JsonElement element)
    {
        return new LayerRef
        {
            Id = ReadString(element, "id") ?? string.Empty,
            Name = ReadString(element, "name") ?? string.Empty,
            Kind = ReadEnum(element, "kind", ParseLayerKind),
            Side = ReadEnum(element, "side", ParseBoardSide),
        };
    }

    private static ComponentInstance ReadComponent(JsonElement element)
    {
        return new ComponentInstance
        {
            Id = ReadString(element, "id") ?? string.Empty,
            Designator = ReadString(element, "designator") ?? string.Empty,
            Comment = ReadString(element, "comment"),
            Footprint = ReadString(element, "footprint"),
            SourceUniqueId = ReadString(element, "sourceUniqueId"),
            LayerId = ReadString(element, "layerId") ?? string.Empty,
            Placement = ReadTransform(ChildObject(element, "placement")),
            Height = ReadLengthPair(element, "heightIu", "heightMm"),
            Fitted = ReadBool(element, "fitted"),
        };
    }

    private static Transform ReadTransform(JsonElement element)
    {
        return new Transform
        {
            Position = ReadPoint(ChildObject(element, "position")),
            RotationDeg = ReadDouble(element, "rotationDeg"),
            Side = ReadEnum(element, "side", ParseBoardSide),
        };
    }

    private static PadStack ReadPadStack(JsonElement element)
    {
        return new PadStack
        {
            Id = ReadString(element, "id") ?? string.Empty,
            Shapes = ReadArray(element, "shapes", ReadPadShape),
        };
    }

    private static PadShape ReadPadShape(JsonElement element)
    {
        return new PadShape
        {
            Id = ReadString(element, "id") ?? string.Empty,
            Kind = ReadEnum(element, "kind", ParsePadShapeKind),
            LayerId = ReadString(element, "layerId"),
            Width = ReadLengthPair(element, "widthIu", "widthMm"),
            Height = ReadLengthPair(element, "heightIu", "heightMm"),
            CornerRadius = ReadLengthPair(element, "cornerRadiusIu", "cornerRadiusMm"),
            Vertices = ReadArray(element, "vertices", ReadPoint),
        };
    }

    private static Pad ReadPad(JsonElement element)
    {
        return new Pad
        {
            Id = ReadString(element, "id") ?? string.Empty,
            ComponentId = ReadString(element, "componentId") ?? string.Empty,
            NetId = ReadString(element, "netId"),
            LayerId = ReadString(element, "layerId") ?? string.Empty,
            PadStackId = ReadString(element, "padStackId"),
            Position = ReadPoint(ChildObject(element, "position")),
            RotationDeg = ReadDouble(element, "rotationDeg"),
        };
    }

    private static Net ReadNet(JsonElement element)
    {
        return new Net
        {
            Id = ReadString(element, "id") ?? string.Empty,
            Name = ReadString(element, "name") ?? string.Empty,
            NetClass = ReadString(element, "netClass"),
        };
    }

    private static Track ReadTrack(JsonElement element)
    {
        return new Track
        {
            Id = ReadString(element, "id") ?? string.Empty,
            NetId = ReadString(element, "netId"),
            LayerId = ReadString(element, "layerId") ?? string.Empty,
            Start = ReadPoint(ChildObject(element, "start")),
            End = ReadPoint(ChildObject(element, "end")),
            Width = ReadLengthPair(element, "widthIu", "widthMm"),
        };
    }

    private static Via ReadVia(JsonElement element)
    {
        return new Via
        {
            Id = ReadString(element, "id") ?? string.Empty,
            NetId = ReadString(element, "netId"),
            Position = ReadPoint(ChildObject(element, "position")),
            Diameter = ReadLengthPair(element, "diameterIu", "diameterMm"),
            HoleDiameter = ReadLengthPair(element, "holeDiameterIu", "holeDiameterMm"),
            StartLayerId = ReadString(element, "startLayerId") ?? string.Empty,
            EndLayerId = ReadString(element, "endLayerId") ?? string.Empty,
        };
    }

    private static Region ReadRegion(JsonElement element)
    {
        return new Region
        {
            Id = ReadString(element, "id") ?? string.Empty,
            NetId = ReadString(element, "netId"),
            LayerId = ReadString(element, "layerId") ?? string.Empty,
            Kind = ReadEnum(element, "kind", ParseRegionKind),
            Polygon = ReadPolygon(ChildObject(element, "polygon")),
        };
    }

    private static PolygonGeometry ReadPolygon(JsonElement element)
    {
        return new PolygonGeometry
        {
            Contours = ReadContours(element),
        };
    }

    private static TextItem ReadText(JsonElement element)
    {
        return new TextItem
        {
            Id = ReadString(element, "id") ?? string.Empty,
            ComponentId = ReadString(element, "componentId"),
            LayerId = ReadString(element, "layerId") ?? string.Empty,
            Position = ReadPoint(ChildObject(element, "position")),
            Text = ReadString(element, "text") ?? string.Empty,
            Height = ReadLengthPair(element, "heightIu", "heightMm"),
            StrokeWidth = ReadLengthPair(element, "strokeWidthIu", "strokeWidthMm"),
            RotationDeg = ReadDouble(element, "rotationDeg"),
        };
    }

    private static IReadOnlyList<Contour> ReadContours(JsonElement element) =>
        ReadArray(element, "contours", ReadContour);

    private static Contour ReadContour(JsonElement element)
    {
        return new Contour
        {
            Id = ReadString(element, "id") ?? string.Empty,
            Role = ReadEnum(element, "role", ParseContourRole),
            Segments = ReadArray(element, "segments", ReadSegment),
        };
    }

    private static ContourSegment ReadSegment(JsonElement element)
    {
        var kind = ReadString(element, "kind");
        return kind switch
        {
            "line" => new LineSegment
            {
                Start = ReadPoint(ChildObject(element, "start")),
                End = ReadPoint(ChildObject(element, "end")),
            },
            "arc" => new ArcSegment
            {
                Start = ReadPoint(ChildObject(element, "start")),
                End = ReadPoint(ChildObject(element, "end")),
                Center = ReadPoint(ChildObject(element, "center")),
                Radius = ReadLengthPair(element, "radiusIu", "radiusMm"),
                SweepDirection = ReadEnum(element, "sweepDirection", ParseArcSweepDirection),
            },
            _ => throw new JsonException($"Unsupported contour segment kind '{kind}'."),
        };
    }

    private static Point2D ReadPoint(JsonElement element)
    {
        return new Point2D(
            ReadLengthPair(element, "xIu", "xMm"),
            ReadLengthPair(element, "yIu", "yMm"));
    }

    private static IReadOnlyList<T> ReadArray<T>(
        JsonElement parent,
        string name,
        Func<JsonElement, T> read)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var array)
            || array.ValueKind == JsonValueKind.Null)
        {
            return Array.Empty<T>();
        }

        if (array.ValueKind != JsonValueKind.Array)
            throw new JsonException($"'{name}' must be an array.");

        var result = new List<T>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new JsonException($"'{name}' must contain JSON objects.");

            result.Add(read(item));
        }

        return result;
    }

    private static JsonElement ChildObject(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return default;
        }

        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException($"'{name}' must be a JSON object.");

        return value;
    }

    private static string? ReadString(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
            throw new JsonException($"'{name}' must be a string or null.");

        return value.GetString();
    }

    private static int? ReadInt(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadIntValue(value, name);
    }

    private static int ReadIntValue(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw new JsonException($"'{name}' must be a 32-bit integer.");

        return result;
    }

    private static double? ReadDouble(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadDoubleValue(value, name);
    }

    private static double ReadDoubleValue(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var result)
            || !double.IsFinite(result))
        {
            throw new JsonException($"'{name}' must be a finite number.");
        }

        return result;
    }

    private static bool? ReadBool(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new JsonException($"'{name}' must be a boolean or null.");

        return value.GetBoolean();
    }

    private static DateTimeOffset? ReadDateTime(JsonElement parent, string name)
    {
        var value = ReadString(parent, name);
        if (value is null)
            return null;

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

    private static T? ReadEnum<T>(JsonElement parent, string name, Func<string, T?> parse)
        where T : struct
    {
        var value = ReadString(parent, name);
        if (value is null)
            return null;

        var parsed = parse(value);
        if (parsed is null)
            throw new JsonException($"Unsupported value '{value}' for '{name}'.");

        return parsed;
    }

    private static LengthValue ReadLengthPair(JsonElement parent, string iuName, string mmName)
    {
        var hasIu = TryGetNonNull(parent, iuName, out var iuElement);
        var hasMm = TryGetNonNull(parent, mmName, out var mmElement);

        if (!hasIu && !hasMm)
            return LengthValue.FromIu(null);

        int? iu = hasIu ? ReadIntValue(iuElement, iuName) : null;
        double? mm = hasMm ? ReadDoubleValue(mmElement, mmName) : null;
        return LengthValue.FromJson(iu, mm);
    }

    private static bool TryGetNonNull(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var candidate)
            || candidate.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static string FormatBoardSide(BoardSide value) => value switch
    {
        BoardSide.Top => "top",
        BoardSide.Bottom => "bottom",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static BoardSide? ParseBoardSide(string value) => value switch
    {
        "top" => BoardSide.Top,
        "bottom" => BoardSide.Bottom,
        _ => null,
    };

    private static string FormatLayerKind(LayerKind value) => value switch
    {
        LayerKind.Copper => "copper",
        LayerKind.Mechanical => "mechanical",
        LayerKind.Silkscreen => "silkscreen",
        LayerKind.SolderMask => "solderMask",
        LayerKind.PasteMask => "pasteMask",
        LayerKind.Drill => "drill",
        LayerKind.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static LayerKind? ParseLayerKind(string value) => value switch
    {
        "copper" => LayerKind.Copper,
        "mechanical" => LayerKind.Mechanical,
        "silkscreen" => LayerKind.Silkscreen,
        "solderMask" => LayerKind.SolderMask,
        "pasteMask" => LayerKind.PasteMask,
        "drill" => LayerKind.Drill,
        "other" => LayerKind.Other,
        _ => null,
    };

    private static string FormatContourRole(ContourRole value) => value switch
    {
        ContourRole.Outer => "outer",
        ContourRole.Hole => "hole",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static ContourRole? ParseContourRole(string value) => value switch
    {
        "outer" => ContourRole.Outer,
        "hole" => ContourRole.Hole,
        _ => null,
    };

    private static string FormatArcSweepDirection(ArcSweepDirection value) => value switch
    {
        ArcSweepDirection.Clockwise => "clockwise",
        ArcSweepDirection.CounterClockwise => "counterClockwise",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static ArcSweepDirection? ParseArcSweepDirection(string value) => value switch
    {
        "clockwise" => ArcSweepDirection.Clockwise,
        "counterClockwise" => ArcSweepDirection.CounterClockwise,
        _ => null,
    };

    private static string FormatPadShapeKind(PadShapeKind value) => value switch
    {
        PadShapeKind.Circular => "circular",
        PadShapeKind.Rectangular => "rectangular",
        PadShapeKind.RoundedRectangle => "roundedRectangle",
        PadShapeKind.Octagonal => "octagonal",
        PadShapeKind.Custom => "custom",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static PadShapeKind? ParsePadShapeKind(string value) => value switch
    {
        "circular" => PadShapeKind.Circular,
        "rectangular" => PadShapeKind.Rectangular,
        "roundedRectangle" => PadShapeKind.RoundedRectangle,
        "octagonal" => PadShapeKind.Octagonal,
        "custom" => PadShapeKind.Custom,
        _ => null,
    };

    private static string FormatRegionKind(RegionKind value) => value switch
    {
        RegionKind.Copper => "copper",
        RegionKind.KeepOut => "keepOut",
        RegionKind.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static RegionKind? ParseRegionKind(string value) => value switch
    {
        "copper" => RegionKind.Copper,
        "keepOut" => RegionKind.KeepOut,
        "other" => RegionKind.Other,
        _ => null,
    };
}
