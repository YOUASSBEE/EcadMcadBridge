using System.IO;
using DXP;
using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using PCB;
using Region = EcadMcadBridge.Core.Model.Region;

namespace EcadMcadBridge.Altium.Adapter;

internal sealed record AltiumBoardSnapshotResult(
    BoardSnapshot Snapshot,
    IReadOnlyList<string> Warnings,
    AltiumAdapterDiagnostics Diagnostics);

internal sealed class AltiumAdapterDiagnostics
{
    public int ExactOutlineContours { get; set; }
    public int FallbackOutlineContours { get; set; }
    public int OmittedOutlineContours { get; set; }
    public int OutlineLoopErrors { get; set; }
    public int UnmatchedAnalyticLoops { get; set; }
    public int AmbiguousOutlineMatches { get; set; }
    public int PartialOutlineMatches { get; set; }
    public int GeometricContourCount { get; set; }
    public int GeometricPointCount { get; set; }
    public int GeometricHoleCount { get; set; }
    public int ExactPadShapes { get; set; }
    public int SimplifiedPadShapes { get; set; }
    public int UnsupportedPadShapes { get; set; }
    public int OmittedPadStacks { get; set; }

    public Dictionary<TShape, int> PadShapeCounts { get; } = new();

    public int CountPadShape(TShape shape)
    {
        PadShapeCounts.TryGetValue(shape, out var count);
        count++;
        PadShapeCounts[shape] = count;
        return count;
    }

    public string Summary()
    {
        var shapes = string.Join(
            ",",
            PadShapeCounts.OrderBy(item => item.Key.ToString(), StringComparer.Ordinal)
                .Select(item => item.Key + ":" + item.Value));
        return $"outlineExact={ExactOutlineContours} outlineFallback={FallbackOutlineContours} " +
            $"outlineOmitted={OmittedOutlineContours} outlineLoopErrors={OutlineLoopErrors} " +
            $"outlineUnmatchedLoops={UnmatchedAnalyticLoops} " +
            $"outlineAmbiguous={AmbiguousOutlineMatches} outlinePartial={PartialOutlineMatches} " +
            $"geometricContours={GeometricContourCount} geometricPoints={GeometricPointCount} " +
            $"geometricHoles={GeometricHoleCount} " +
            $"padShapesExact={ExactPadShapes} padShapesSimplified={SimplifiedPadShapes} " +
            $"padShapesUnsupported={UnsupportedPadShapes} padStacksEmpty={OmittedPadStacks} " +
            $"shapeCounts=[{shapes}]";
    }
}

/// <summary>
/// Read-only adapter from the active Altium PCB to the neutral Core BoardSnapshot.
/// It contains no JSON serialization and never mutates board objects.
/// </summary>
internal static class AltiumBoardSnapshotAdapter
{
    private const string ToolVersion = "0.2.0";

    public static AltiumBoardSnapshotResult Read(IServerDocumentView? view)
    {
        var pcbServer = PCB.GlobalVars.PCBServer;
        if (pcbServer is null)
            throw new InvalidOperationException("The PCB server is not available.");

        var board = pcbServer.GetCurrentPCBBoard();
        if (board is null)
            throw new InvalidOperationException("No active PCB document. Open a .PcbDoc and try again.");

        var document = view?.GetOwnerDocument();
        if (document is not null &&
            !string.Equals(document.GetKind(), "PCB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The active document is not a PCB document.");

        var warnings = new List<string>();
        var diagnostics = new AltiumAdapterDiagnostics();
        var layers = ReadLayers(board, warnings);
        var nets = new NetCatalog();
        ReadNets(board, nets, warnings);

        var components = new List<ComponentInstance>();
        var padStacks = new List<PadStack>();
        var pads = new List<Pad>();
        ReadComponents(board, layers, nets, components, padStacks, pads, warnings, diagnostics);

        var outline = AltiumOutlineReader.Read(board, warnings, diagnostics);
        var fileName = Path.GetFileName(board.GetState_FileName() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "(unnamed)";
            warnings.Add("The board file name is unavailable.");
        }

        var thickness = ReadBoardThickness(board, layers, warnings);
        var snapshot = new BoardSnapshot
        {
            Source = new BoardSnapshotSource
            {
                Cad = "altium",
                CadVersion = "26",
                DocumentFileName = fileName,
                ExportedAtUtc = DateTimeOffset.UtcNow,
                ToolVersion = ToolVersion,
            },
            Properties = new BoardProperties
            {
                Id = "altium:board",
                Name = Path.GetFileNameWithoutExtension(fileName),
                Thickness = thickness,
            },
            Outline = outline,
            Layers = layers.Layers.Values.OrderBy(layer => layer.Id, StringComparer.Ordinal).ToArray(),
            Components = components.OrderBy(component => component.Id, StringComparer.Ordinal).ToArray(),
            PadStacks = padStacks.OrderBy(stack => stack.Id, StringComparer.Ordinal).ToArray(),
            Pads = pads.OrderBy(pad => pad.Id, StringComparer.Ordinal).ToArray(),
            Nets = nets.Items.OrderBy(net => net.Id, StringComparer.Ordinal).ToArray(),
            Tracks = Array.Empty<Track>(),
            Vias = Array.Empty<Via>(),
            Regions = Array.Empty<Region>(),
            Texts = Array.Empty<TextItem>(),
        };

        return new AltiumBoardSnapshotResult(snapshot, warnings, diagnostics);
    }

    private static LayerCatalog ReadLayers(IPCB_Board board, List<string> warnings)
    {
        var catalog = new LayerCatalog();
        var iterator = board.LayerIterator();
        if (iterator is null)
        {
            warnings.Add("Layer iterator is unavailable.");
            return catalog;
        }

        for (var more = iterator.First(); more; more = iterator.Next())
        {
            var layer = iterator.LayerObject();
            if (layer is null)
                continue;

            var layerId = (int)layer.V6_LayerID();
            if (layerId is < 1 or > 74)
                continue;

            var inStack = layer.IsInLayerStack();
            var used = layer.GetState_UsedByPrims();
            var isMechanical = layerId is >= 57 and <= 72;
            if (!inStack && !used && !isMechanical)
                continue;

            catalog.Add(layerId, layer);
        }

        if (catalog.Layers.Count == 0)
            warnings.Add("No board layers were returned by the layer iterator.");

        return catalog;
    }

    private static void ReadNets(IPCB_Board board, NetCatalog nets, List<string> warnings)
    {
        var iterator = board.BoardIterator_Create();
        try
        {
            var objectSet = new TObjectSet { TObjectId.eNetObject };
            iterator.AddFilter_ObjectSet(objectSet);
            iterator.AddFilter_Method(TIterationMethod.eProcessAll);

            for (var primitive = iterator.FirstPCBObject(); primitive is not null; primitive = iterator.NextPCBObject())
            {
                if (primitive is IPCB_Net net)
                    nets.GetOrAdd(net, warnings);
            }
        }
        finally
        {
            board.BoardIterator_Destroy(ref iterator);
        }

        if (nets.Items.Count == 0)
            warnings.Add("The board net iterator returned no nets.");
    }

    private static void ReadComponents(
        IPCB_Board board,
        LayerCatalog layers,
        NetCatalog nets,
        List<ComponentInstance> components,
        List<PadStack> padStacks,
        List<Pad> pads,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var iterator = board.BoardIterator_Create();
        try
        {
            var objectSet = new TObjectSet { TObjectId.eComponentObject };
            iterator.AddFilter_ObjectSet(objectSet);
            iterator.AddFilter_Method(TIterationMethod.eProcessAll);

            var index = 0;
            var holeWarning = false;
            for (var primitive = iterator.FirstPCBObject(); primitive is not null; primitive = iterator.NextPCBObject())
            {
                if (primitive is not IPCB_Component component)
                    continue;

                var designator = component.GetState_Name()?.GetState_Text() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(designator))
                {
                    designator = "UNNAMED_" + index;
                    warnings.Add("A component has no designator; a synthetic designator was used.");
                }

                var sourceUniqueId = component.GetState_SourceUniqueId();
                var uniqueId = component.GetState_UniqueId();
                var componentId = MakeId("component", uniqueId, sourceUniqueId ?? designator + ":" + index, warnings);
                var rawLayer = (int)component.GetState_Layer();
                var side = rawLayer == 32
                    ? BoardSide.Bottom
                    : rawLayer == 1
                        ? BoardSide.Top
                        : component.GetState_FlippedOnLayer()
                            ? BoardSide.Bottom
                            : BoardSide.Top;
                var layer = side == BoardSide.Top ? TV6_Layer.eV6_TopLayer : TV6_Layer.eV6_BottomLayer;
                var layerId = layers.GetOrAdd((int)layer, null, warnings);

                var height = component.GetState_Height();
                var componentInstance = new ComponentInstance
                {
                    Id = componentId,
                    Designator = designator,
                    Comment = component.GetState_Comment()?.GetState_Text(),
                    Footprint = component.GetState_Pattern(),
                    SourceUniqueId = sourceUniqueId,
                    LayerId = layerId,
                    Placement = new Transform
                    {
                        Position = Point2D.FromIu(component.GetState_XLocation(), component.GetState_YLocation()),
                        RotationDeg = AngleNormalizer.NormalizeDegrees(component.GetState_Rotation()),
                        Side = side,
                    },
                    Height = height > 0 ? LengthValue.FromIu(height) : LengthValue.FromIu(null),
                    Fitted = null,
                };

                components.Add(componentInstance);
                ReadPads(
                    component,
                    componentId,
                    layers,
                    nets,
                    padStacks,
                    pads,
                    warnings,
                    ref holeWarning,
                    diagnostics);
                index++;
            }
        }
        finally
        {
            board.BoardIterator_Destroy(ref iterator);
        }
    }

    private static void ReadPads(
        IPCB_Component component,
        string componentId,
        LayerCatalog layers,
        NetCatalog nets,
        List<PadStack> padStacks,
        List<Pad> pads,
        List<string> warnings,
        ref bool holeWarning,
        AltiumAdapterDiagnostics diagnostics)
    {
        var iterator = component.GroupIterator_Create();
        if (iterator is null)
        {
            warnings.Add($"Pad iterator is unavailable for component '{componentId}'.");
            return;
        }

        try
        {
            var objectSet = new TObjectSet { TObjectId.ePadObject };
            iterator.AddFilter_ObjectSet(objectSet);
            iterator.AddFilter_AllLayers();

            var index = 0;
            for (var primitive = iterator.FirstPCBObject(); primitive is not null; primitive = iterator.NextPCBObject())
            {
                if (primitive is not IPCB_Pad pad)
                    continue;

                var padName = pad.GetState_Name();
                var padId = MakeId(
                    "pad",
                    pad.GetState_UniqueId(),
                    componentId + ":" + padName + ":" + index,
                    warnings);
                var padLayer = pad.GetState_Layer();
                var padLayerId = layers.GetOrAdd((int)padLayer, null, warnings);
                var stackId = "altium:padstack:" + padId;
                var shapes = ReadPadShapes(
                    pad,
                    stackId,
                    layers,
                    warnings,
                    diagnostics);

                padStacks.Add(new PadStack { Id = stackId, Shapes = shapes });
                pads.Add(new Pad
                {
                    Id = padId,
                    ComponentId = componentId,
                    NetId = nets.GetOrAdd(pad.GetState_Net(), warnings),
                    LayerId = padLayerId,
                    PadStackId = stackId,
                    Position = Point2D.FromIu(pad.GetState_XLocation(), pad.GetState_YLocation()),
                    RotationDeg = AngleNormalizer.NormalizeDegrees(pad.GetState_Rotation()),
                });

                if (pad.GetState_HoleSize() > 0 && !holeWarning)
                {
                    holeWarning = true;
                    warnings.Add("Pad holes are present but the Core Pad contract has no hole fields; hole data was not exported.");
                }

                index++;
            }
        }
        finally
        {
            component.GroupIterator_Destroy(ref iterator);
        }
    }

    private static IReadOnlyList<PadShape> ReadPadShapes(
        IPCB_Pad pad,
        string stackId,
        LayerCatalog layers,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var shapes = new List<PadShape>();
        var padLayer = (int)pad.GetState_Layer();
        foreach (var pair in layers.Layers.OrderBy(item => item.Key))
        {
            var layerId = pair.Key;
            if (layerId is < 1 or > 32 && layerId is < 39 or > 54)
                continue;

            if (padLayer != 74 && layerId != padLayer)
                continue;

            if (!layers.V7Layers.TryGetValue(layerId, out var layer))
                continue;

            var shape = pad.GetState_ShapeOnLayer(layer);
            var width = pad.GetState_XSizeOnLayer(layer);
            var height = pad.GetState_YSizeOnLayer(layer);
            if (width <= 0)
                width = pad.GetState_XStackSizeOnLayer(layer);
            if (height <= 0)
                height = pad.GetState_YStackSizeOnLayer(layer);
            if (shape == TShape.eNoShape || (width <= 0 && height <= 0))
                continue;

            var shapeCount = diagnostics.CountPadShape(shape);
            var kind = MapPadShape(shape, width, height, out var simplified);
            if (kind is null)
            {
                diagnostics.UnsupportedPadShapes++;
                if (shapeCount == 1)
                {
                    warnings.Add(
                        $"Unsupported Altium pad shape '{shape}' was omitted from PadShape; " +
                        "the current Core PadShape cannot represent it without guessing geometry.");
                }
                continue;
            }

            if (simplified)
            {
                diagnostics.SimplifiedPadShapes++;
                if (shapeCount == 1)
                {
                    warnings.Add(shape switch
                    {
                        TShape.eOctagonal =>
                            "Altium eOctagonal is a chamfered rectangle; Core Octagonal is an approximation.",
                        TShape.eRounded =>
                            "Oval pads are represented as rounded rectangles with radius = min(width,height)/2; Core has no Oval kind.",
                        _ =>
                            "Rounded rectangle corner radius is unavailable from the confirmed API; PadShape was exported without CornerRadius.",
                    });
                }
            }
            else
            {
                diagnostics.ExactPadShapes++;
            }

            var cornerRadius = shape == TShape.eRounded
                ? LengthValue.FromIu(Math.Min(Math.Abs(width), Math.Abs(height)) / 2)
                : LengthValue.FromIu(null);
            shapes.Add(new PadShape
            {
                Id = stackId + ":shape:" + layerId,
                Kind = kind,
                LayerId = pair.Value.Id,
                Width = width > 0 ? LengthValue.FromIu(width) : LengthValue.FromIu(null),
                Height = height > 0 ? LengthValue.FromIu(height) : LengthValue.FromIu(null),
                CornerRadius = cornerRadius,
                Vertices = Array.Empty<Point2D>(),
            });
        }

        if (shapes.Count == 0)
        {
            diagnostics.OmittedPadStacks++;
            warnings.Add($"Pad '{pad.GetState_Name()}' has no representable copper shape; its PadStack is empty.");
        }

        return shapes;
    }

    internal static PadShapeKind? MapPadShape(TShape shape, int width, int height, out bool simplified)
    {
        simplified = false;
        switch (shape)
        {
            case TShape.eRectangular:
                return PadShapeKind.Rectangular;
            case TShape.eCircleShape:
                return PadShapeKind.Circular;
            case TShape.eOctagonal:
                simplified = true;
                return PadShapeKind.Octagonal;
            case TShape.eRoundRectShape:
            case TShape.eRoundedRectangular:
                simplified = true;
                return PadShapeKind.RoundedRectangle;
            case TShape.eRounded:
                if (Math.Abs(width - height) <= 1)
                    return PadShapeKind.Circular;
                simplified = true;
                return PadShapeKind.RoundedRectangle;
            case TShape.eRotatedRectShape:
            case TShape.eArcShape:
            case TShape.eTerminator:
            case TShape.eCustomShape:
            case TShape.eNoShape:
                return null;
            default:
                return null;
        }
    }

    private static LengthValue ReadBoardThickness(IPCB_Board board, LayerCatalog layers, List<string> warnings)
    {
        try
        {
            var stack = board.GetState_MasterStack();
            if (stack is null)
                return LengthValue.FromIu(null);

            if (!layers.Objects.TryGetValue(1, out var top) || !layers.Objects.TryGetValue(32, out var bottom))
            {
                warnings.Add("Board thickness is unavailable because top or bottom layer object is missing.");
                return LengthValue.FromIu(null);
            }

            var zTop = stack.Get_ZTop(top);
            var zBottom = stack.Get_ZBottom(bottom);
            var thickness = Math.Abs(zTop - zBottom);
            return thickness > 0 ? LengthValue.FromIu(thickness) : LengthValue.FromIu(null);
        }
        catch (Exception ex)
        {
            warnings.Add("Board thickness read failed: " + ex.GetType().Name + ".");
            return LengthValue.FromIu(null);
        }
    }

    private static string MakeId(string kind, string? uniqueId, string fallback, List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(uniqueId))
            return "altium:" + kind + ":" + uniqueId;

        warnings.Add($"A {kind} has no source unique id; a deterministic fallback id was used.");
        return "altium:" + kind + ":fallback:" + fallback;
    }

    private static BoardSide? MapSide(int layerId) => layerId switch
    {
        1 or 33 or 35 or 37 => BoardSide.Top,
        32 or 34 or 36 or 38 => BoardSide.Bottom,
        _ => null,
    };

    private sealed class LayerCatalog
    {
        public Dictionary<int, LayerRef> Layers { get; } = new();

        public Dictionary<int, IPCB_LayerObject> Objects { get; } = new();

        public Dictionary<int, V7_LayerBase> V7Layers { get; } = new();

        public void Add(int layerId, IPCB_LayerObject layer)
        {
            Objects[layerId] = layer;
            Layers[layerId] = new LayerRef
            {
                Id = LayerId(layerId),
                Name = layer.GetState_LayerName(),
                Kind = MapLayerKind(layer, layerId),
                Side = MapSide(layerId),
            };

            try
            {
                V7Layers[layerId] = layer.V7_LayerID();
            }
            catch
            {
                // A V7 id is optional; the V6 id remains authoritative.
            }
        }

        public string GetOrAdd(int layerId, IPCB_LayerObject? layer, List<string> warnings)
        {
            if (Layers.TryGetValue(layerId, out var existing))
                return existing.Id;

            if (layer is not null)
            {
                Add(layerId, layer);
                return LayerId(layerId);
            }

            warnings.Add($"Layer {layerId} was referenced but is absent from the board layer iterator.");
            Layers[layerId] = new LayerRef
            {
                Id = LayerId(layerId),
                Name = "Layer " + layerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Kind = MapLayerKind(null, layerId),
                Side = MapSide(layerId),
            };
            return LayerId(layerId);
        }

        private static string LayerId(int layerId) =>
            "altium:layer:" + layerId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static LayerKind MapLayerKind(IPCB_LayerObject? layer, int layerId)
        {
            return layerId switch
            {
                >= 1 and <= 32 => LayerKind.Copper,
                33 or 34 => LayerKind.Silkscreen,
                35 or 36 => LayerKind.PasteMask,
                37 or 38 => LayerKind.SolderMask,
                >= 39 and <= 54 => LayerKind.Copper,
                55 or 73 => LayerKind.Drill,
                >= 57 and <= 72 => LayerKind.Mechanical,
                _ => LayerKind.Other,
            };
        }
    }

    private sealed class NetCatalog
    {
        private readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal);

        public List<Net> Items { get; } = new();

        public string? GetOrAdd(IPCB_Net? net, List<string> warnings)
        {
            if (net is null)
                return null;

            var name = net.GetState_Name();
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var uniqueId = net.GetState_UniqueId();
            var key = string.IsNullOrWhiteSpace(uniqueId) ? name : uniqueId;
            if (_ids.TryGetValue(key, out var existing))
                return existing;

            var id = string.IsNullOrWhiteSpace(uniqueId)
                ? "altium:net:fallback:" + name
                : "altium:net:" + uniqueId;
            if (_ids.ContainsKey(id))
                id += ":" + Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

            _ids[key] = id;
            _ids[id] = id;
            Items.Add(new Net { Id = id, Name = name });
            return id;
        }
    }
}
