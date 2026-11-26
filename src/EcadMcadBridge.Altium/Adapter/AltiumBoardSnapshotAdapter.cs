using System.IO;
using DXP;
using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using PCB;
using Region = EcadMcadBridge.Core.Model.Region;

namespace EcadMcadBridge.Altium.Adapter;

internal sealed record AltiumBoardSnapshotResult(
    BoardSnapshot Snapshot,
    IPCB_Board Board,
    IReadOnlyList<AltiumComponentIdentity> ComponentIdentities,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<AltiumRegionInfo> Regions,
    AltiumAdapterDiagnostics Diagnostics);

internal sealed record AltiumRegionContour(
    bool IsHole,
    IReadOnlyList<Point2D> Points,
    string Source = "unknown",
    int SourceIndex = -1,
    int ContourIndex = -1);

internal readonly record struct RegionContourProbe(bool IsHole, IReadOnlyList<Point2D> Points);

internal readonly record struct SkippedRegionContour(
    int Index,
    bool? IsHole,
    IReadOnlyList<Point2D> Points);

internal enum AltiumReadIssueKind
{
    GeometricPolygonUnavailable,
    GeometricContourReadError,
    AnalyticSegmentReadError,
    RegionContourReadError,
    RegionContourSkipped,
    RegionContourRecovered,
    RegionContourRecoveryDuplicate,
    RegionContourUnproven,
    BoardCutoutRegionEmpty,
    ClipResultUnavailable,
    ClipShapesUnavailable,
    ClipSegmentReadError,
}

internal sealed record AltiumReadIssue(
    AltiumReadIssueKind Kind,
    string Source,
    int Index,
    string Reason);

internal sealed record AltiumRegionInfo(
    int Index,
    string RuntimeType,
    TRegionKind? Kind,
    string? Name,
    string? UniqueId,
    long Area,
    string LayerId,
    string? NetId,
    bool IsKeepout,
    IReadOnlyList<AltiumRegionContour> Contours,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY)
{
    public int HoleCount => Contours.Count(contour => contour.IsHole);

    public IReadOnlyList<Point2D> MainPoints =>
        Contours.FirstOrDefault(contour => !contour.IsHole)?.Points ?? Array.Empty<Point2D>();

    public bool IsClosed =>
        MainPoints.Count >= 2
        && MainPoints[0].XIu == MainPoints[^1].XIu
        && MainPoints[0].YIu == MainPoints[^1].YIu;
}

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
    public List<AltiumReadIssue> ReadIssues { get; } = new();
    public int TotalRegions { get; set; }
    public int BoardCutoutRegions { get; set; }
    public int BoardCutoutContours { get; set; }
    public int BoardCutoutHoles { get; set; }
    public int BoardCutoutEmptyRegions { get; set; }
    public int BoardCutoutContourReadErrors { get; set; }
    public int RegionContourRecoveredCount { get; set; }
    public int RegionContourRecoveryDuplicateCount { get; set; }
    public int BoardCutoutUnprovenSkippedContours { get; set; }
    public int BoardCutoutShapeContours { get; set; }
    public int BoardCutoutShapeFragments { get; set; }
    public int BoardCutoutNestedIslandContours { get; set; }
    public int BoardCutoutUnrelatedContours { get; set; }
    public int BoardCutoutUnsupportedContours { get; set; }
    public HashSet<CutoutSourceKey> MatchedCutoutSources { get; } = new();
    public HashSet<CutoutSourceKey> DuplicateCutoutSources { get; } = new();
    public HashSet<CutoutSourceKey> UnresolvedCutoutSources { get; } = new();
    public int ClipCutoutsCount { get; set; }
    public int ClipNewPrimsCount { get; set; }
    public int ClipInoperablePrimsCount { get; set; }
    public int ClipResultUnavailableCount { get; set; }
    public int ClipShapesUnavailableCount { get; set; }
    public int ClipCutoutContours { get; set; }
    public int ClipCutoutSegments { get; set; }
    public int ClipCutoutErrors { get; set; }
    public int ClipMatchedCutoutRegions { get; set; }
    public int ClipUnmatchedCutoutRegions { get; set; }
    public int ClipAmbiguousCutoutRegions { get; set; }
    public int ClipReusedSources { get; set; }
    public int UnresolvedClipLoops { get; set; }
    public int DuplicateClipLoops { get; set; }
    public int PartialClipMatches { get; set; }
    public int DuplicateCutoutContours { get; set; }
    public int ExportedCutoutContours { get; set; }
    public Dictionary<TRegionKind, int> RegionsByKind { get; } = new();
    public HashSet<string> FallbackIdWarnings { get; } = new(StringComparer.Ordinal);
    public int TracksDetected { get; set; }
    public int TracksExported { get; set; }
    public int TracksSkipped { get; set; }
    public int TrackReadErrors { get; set; }
    public int ViasDetected { get; set; }
    public int ViasExported { get; set; }
    public int ViasSkipped { get; set; }
    public int ViaReadErrors { get; set; }
    public int CoreRegionsDetected { get; set; }
    public int CoreRegionsExported { get; set; }
    public int CoreRegionsSkipped { get; set; }
    public int RegionReadErrors { get; set; }
    public int RegionUnknownKinds { get; set; }
    public int CoreRegionOtherKinds { get; set; }
    public int RegionGeometryFallbacks { get; set; }
    public int TextsDetected { get; set; }
    public int TextsExported { get; set; }
    public int TextsSkipped { get; set; }
    public int TextReadErrors { get; set; }
    public int TextComponentLinkMisses { get; set; }
    public int TextUnknownRotations { get; set; }
    public int ExactPadShapes { get; set; }
    public int SimplifiedPadShapes { get; set; }
    public int UnsupportedPadShapes { get; set; }
    public int OmittedPadStacks { get; set; }

    public Dictionary<TShape, int> PadShapeCounts { get; } = new();

    public bool ShouldWarnFallbackId(string kind) => FallbackIdWarnings.Add(kind);

    public int CountPadShape(TShape shape)
    {
        PadShapeCounts.TryGetValue(shape, out var count);
        count++;
        PadShapeCounts[shape] = count;
        return count;
    }

    public void AddIssue(AltiumReadIssueKind kind, string source, int index, string reason) =>
        ReadIssues.Add(new AltiumReadIssue(kind, source, index, reason));

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
            $"geometricHoles={GeometricHoleCount} readIssues={ReadIssues.Count} " +
            $"totalRegions={TotalRegions} " +
            $"boardCutoutRegions={BoardCutoutRegions} boardCutoutContours={BoardCutoutContours} " +
            $"boardCutoutHoles={BoardCutoutHoles} boardCutoutEmpty={BoardCutoutEmptyRegions} " +
            $"boardCutoutErrors={BoardCutoutContourReadErrors} " +
            $"regionContourRecovered={RegionContourRecoveredCount} " +
            $"regionContourRecoveryDuplicates={RegionContourRecoveryDuplicateCount} " +
            $"boardCutoutUnprovenSkipped={BoardCutoutUnprovenSkippedContours} " +
            $"boardCutoutShapes={BoardCutoutShapeContours} " +
            $"boardCutoutShapeFragments={BoardCutoutShapeFragments} " +
            $"boardCutoutNestedIslands={BoardCutoutNestedIslandContours} " +
            $"boardCutoutUnrelated={BoardCutoutUnrelatedContours} " +
            $"boardCutoutUnsupported={BoardCutoutUnsupportedContours} " +
            $"unresolvedSources={UnresolvedCutoutSources.Count} " +
            $"clipCutouts={ClipCutoutsCount} " +
            $"clipNewPrims={ClipNewPrimsCount} clipInoperable={ClipInoperablePrimsCount} " +
            $"clipResultUnavailable={ClipResultUnavailableCount} " +
            $"clipShapesUnavailable={ClipShapesUnavailableCount} " +
            $"clipCutoutContours={ClipCutoutContours} clipCutoutSegments={ClipCutoutSegments} " +
            $"clipCutoutErrors={ClipCutoutErrors} clipMatchedRegions={ClipMatchedCutoutRegions} " +
            $"clipUnmatchedRegions={ClipUnmatchedCutoutRegions} " +
            $"clipAmbiguousRegions={ClipAmbiguousCutoutRegions} " +
            $"clipReusedSources={ClipReusedSources} unresolvedLoops={UnresolvedClipLoops} " +
            $"duplicateClipLoops={DuplicateClipLoops} partialClipMatches={PartialClipMatches} " +
            $"duplicateCutoutContours={DuplicateCutoutContours} exportedCutoutContours={ExportedCutoutContours} " +
            $"regionsByKind=[{FormatRegionKinds()}] " +
            $"tracksDetected={TracksDetected} tracksExported={TracksExported} tracksSkipped={TracksSkipped} " +
            $"trackErrors={TrackReadErrors} viasDetected={ViasDetected} viasExported={ViasExported} " +
            $"viasSkipped={ViasSkipped} viaErrors={ViaReadErrors} coreRegionsDetected={CoreRegionsDetected} " +
            $"coreRegionsExported={CoreRegionsExported} coreRegionsSkipped={CoreRegionsSkipped} " +
            $"regionErrors={RegionReadErrors} regionUnknownKinds={RegionUnknownKinds} " +
            $"regionOtherKinds={CoreRegionOtherKinds} " +
            $"regionGeometryFallbacks={RegionGeometryFallbacks} textsDetected={TextsDetected} " +
            $"textsExported={TextsExported} textsSkipped={TextsSkipped} textErrors={TextReadErrors} " +
            $"textComponentLinkMisses={TextComponentLinkMisses} " +
            $"textUnknownRotations={TextUnknownRotations} " +
            $"padShapesExact={ExactPadShapes} padShapesSimplified={SimplifiedPadShapes} " +
            $"padShapesUnsupported={UnsupportedPadShapes} padStacksEmpty={OmittedPadStacks} " +
            $"shapeCounts=[{shapes}]";
    }

    private string FormatRegionKinds() =>
        string.Join(
            ",",
            RegionsByKind.OrderBy(item => item.Key.ToString(), StringComparer.Ordinal)
                .Select(item => item.Key + ":" + item.Value));
}

/// <summary>
/// Read-only adapter from the active Altium PCB to the neutral Core BoardSnapshot.
/// It contains no JSON serialization and never mutates board objects.
/// </summary>
internal static class AltiumBoardSnapshotAdapter
{
    private const string ToolVersion = "0.4.0";

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
        var regions = ReadRegions(board, layers, nets, warnings, diagnostics);

        var components = new List<ComponentInstance>();
        var componentIdentities = new List<AltiumComponentIdentity>();
        var padStacks = new List<PadStack>();
        var pads = new List<Pad>();
        var componentIds = new Dictionary<string, string>(StringComparer.Ordinal);
        ReadComponents(
            board,
            layers,
            nets,
            components,
            componentIdentities,
            padStacks,
            pads,
            componentIds,
            warnings,
            diagnostics);

        var outline = AltiumOutlineReader.Read(board, warnings, diagnostics, regions);
        var tracks = ReadTracks(board, layers, nets, warnings, diagnostics);
        var vias = ReadVias(board, layers, nets, warnings, diagnostics);
        var coreRegions = BuildCoreRegions(regions, warnings, diagnostics);
        var texts = ReadTexts(board, layers, componentIds, warnings, diagnostics);
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
                OriginX = ReadBoardOrigin(board, xOrigin: true, warnings),
                OriginY = ReadBoardOrigin(board, xOrigin: false, warnings),
            },
            Outline = outline,
            Layers = layers.Layers.Values.OrderBy(layer => layer.Id, StringComparer.Ordinal).ToArray(),
            Components = components.OrderBy(component => component.Id, StringComparer.Ordinal).ToArray(),
            PadStacks = padStacks.OrderBy(stack => stack.Id, StringComparer.Ordinal).ToArray(),
            Pads = pads.OrderBy(pad => pad.Id, StringComparer.Ordinal).ToArray(),
            Nets = nets.Items.OrderBy(net => net.Id, StringComparer.Ordinal).ToArray(),
            Tracks = tracks.OrderBy(track => track.Id, StringComparer.Ordinal).ToArray(),
            Vias = vias.OrderBy(via => via.Id, StringComparer.Ordinal).ToArray(),
            Regions = coreRegions.OrderBy(region => region.Id, StringComparer.Ordinal).ToArray(),
            Texts = texts.OrderBy(text => text.Id, StringComparer.Ordinal).ToArray(),
        };

        return new AltiumBoardSnapshotResult(
            snapshot,
            board,
            componentIdentities,
            warnings,
            regions,
            diagnostics);
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

    private static IReadOnlyList<AltiumRegionInfo> ReadRegions(
        IPCB_Board board,
        LayerCatalog layers,
        NetCatalog nets,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var result = new List<AltiumRegionInfo>();
        var iterator = board.BoardIterator_Create();
        try
        {
            var objectSet = new TObjectSet { TObjectId.eRegionObject };
            iterator.AddFilter_ObjectSet(objectSet);
            iterator.AddFilter_Method(TIterationMethod.eProcessAll);

            var index = 0;
            for (var primitive = iterator.FirstPCBObject(); primitive is not null; primitive = iterator.NextPCBObject())
            {
                if (primitive is not IPCB_Region region)
                    continue;

                var kind = region.GetState_Kind();
                diagnostics.TotalRegions++;
                diagnostics.RegionsByKind.TryGetValue(kind, out var kindCount);
                diagnostics.RegionsByKind[kind] = kindCount + 1;

                var regionIndex = index++;
                var contours = ReadRegionContours(region, regionIndex, warnings, diagnostics);
                var bounds = GetBounds(contours.SelectMany(contour => contour.Points));
                var layerId = layers.GetOrAdd((int)region.GetState_Layer(), null, warnings);
                var netId = nets.GetOrAdd(region.GetState_Net(), warnings);
                var info = new AltiumRegionInfo(
                    regionIndex,
                    primitive.GetType().FullName ?? primitive.GetType().Name,
                    kind,
                    region.GetState_Name(),
                    region.GetState_UniqueId(),
                    region.GetState_Area(),
                    layerId,
                    netId,
                    region.GetState_IsKeepout(),
                    contours,
                    bounds.MinX,
                    bounds.MinY,
                    bounds.MaxX,
                    bounds.MaxY);

                result.Add(info);

                if (kind == TRegionKind.eRegionKind_BoardCutout)
                {
                    diagnostics.BoardCutoutRegions++;
                    diagnostics.BoardCutoutContours += contours.Count;
                    if (contours.Count == 0)
                    {
                        diagnostics.BoardCutoutEmptyRegions++;
                        diagnostics.AddIssue(
                            AltiumReadIssueKind.BoardCutoutRegionEmpty,
                            "BoardCutoutRegion",
                            regionIndex,
                            "No usable contour was read from the Board Cutout region.");
                    }
                }
            }
        }
        finally
        {
            board.BoardIterator_Destroy(ref iterator);
        }

        if (result.Count == 0)
        {
            warnings.Add("The Region iterator returned no IPCB_Region objects.");
        }
        else
        {
            var boardCutouts = result
                .Where(region => region.Kind == TRegionKind.eRegionKind_BoardCutout)
                .ToArray();
            warnings.Add(
                $"Region kinds: [{string.Join(",", diagnostics.RegionsByKind.OrderBy(item => item.Key.ToString(), StringComparer.Ordinal).Select(item => item.Key + ":" + item.Value))}]; " +
                (boardCutouts.Length == 0
                    ? "no eRegionKind_BoardCutout regions."
                    : "Board Cutout regions: " + FormatRegionInventory(boardCutouts)));
        }

        return result;
    }

    private static List<Track> ReadTracks(
        IPCB_Board board,
        LayerCatalog layers,
        NetCatalog nets,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var result = new List<Track>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var iterator = board.BoardIterator_Create();
        try
        {
            var objectSet = new TObjectSet { TObjectId.eTrackObject };
            iterator.AddFilter_ObjectSet(objectSet);
            iterator.AddFilter_Method(TIterationMethod.eProcessAll);
            var index = 0;
            for (var primitive = iterator.FirstPCBObject(); primitive is not null; primitive = iterator.NextPCBObject())
            {
                if (primitive is not IPCB_Track track)
                    continue;

                diagnostics.TracksDetected++;
                index++;
                try
                {
                    var x1 = track.GetState_X1();
                    var y1 = track.GetState_Y1();
                    var x2 = track.GetState_X2();
                    var y2 = track.GetState_Y2();
                    var width = track.GetState_Width();
                    if ((x1 == x2 && y1 == y2) || width <= 0)
                    {
                        diagnostics.TracksSkipped++;
                        if (diagnostics.TracksSkipped <= 5)
                            warnings.Add("A track with zero length or non-positive width was omitted.");
                        continue;
                    }

                    var layerId = layers.GetOrAdd((int)track.GetState_Layer(), null, warnings);
                    var netId = nets.GetOrAdd(track.GetState_Net(), warnings);
                    var id = EnsureUniqueId(
                        MakeId("track", track.GetState_UniqueId(), "index:" + index, warnings, diagnostics),
                        ids,
                        index);
                    var mapped = AltiumSnapshotMapping.MapTrack(id, x1, y1, x2, y2, width, layerId, netId);
                    if (mapped is null)
                    {
                        diagnostics.TracksSkipped++;
                        continue;
                    }

                    result.Add(mapped);
                    diagnostics.TracksExported++;
                }
                catch (Exception ex)
                {
                    diagnostics.TrackReadErrors++;
                    if (diagnostics.TrackReadErrors <= 5)
                        warnings.Add("Track read failed: " + ex.GetType().Name + ".");
                }
            }
        }
        finally
        {
            board.BoardIterator_Destroy(ref iterator);
        }

        return result;
    }

    private static List<Via> ReadVias(
        IPCB_Board board,
        LayerCatalog layers,
        NetCatalog nets,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var result = new List<Via>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var iterator = board.BoardIterator_Create();
        try
        {
            var objectSet = new TObjectSet { TObjectId.eViaObject };
            iterator.AddFilter_ObjectSet(objectSet);
            iterator.AddFilter_Method(TIterationMethod.eProcessAll);
            var index = 0;
            for (var primitive = iterator.FirstPCBObject(); primitive is not null; primitive = iterator.NextPCBObject())
            {
                if (primitive is not IPCB_Via via)
                    continue;

                diagnostics.ViasDetected++;
                index++;
                try
                {
                    var size = via.GetState_Size();
                    var hole = via.GetState_HoleSize();
                    var start = via.GetState_StartLayer();
                    var stop = via.GetState_StopLayer();
                    if (size <= 0 || hole < 0 || hole > size || start is null || stop is null)
                    {
                        diagnostics.ViasSkipped++;
                        if (diagnostics.ViasSkipped <= 5)
                            warnings.Add("A via with invalid size/hole/layer data was omitted.");
                        continue;
                    }

                    var startLayerId = layers.GetOrAdd((int)start.V6_LayerID(), start, warnings);
                    var stopLayerId = layers.GetOrAdd((int)stop.V6_LayerID(), stop, warnings);
                    var netId = nets.GetOrAdd(via.GetState_Net(), warnings);
                    var id = EnsureUniqueId(
                        MakeId("via", via.GetState_UniqueId(), "index:" + index, warnings, diagnostics),
                        ids,
                        index);
                    var mapped = AltiumSnapshotMapping.MapVia(
                        id,
                        via.GetState_XLocation(),
                        via.GetState_YLocation(),
                        size,
                        hole,
                        startLayerId,
                        stopLayerId,
                        netId);
                    if (mapped is null)
                    {
                        diagnostics.ViasSkipped++;
                        continue;
                    }

                    result.Add(mapped);
                    diagnostics.ViasExported++;
                }
                catch (Exception ex)
                {
                    diagnostics.ViaReadErrors++;
                    if (diagnostics.ViaReadErrors <= 5)
                        warnings.Add("Via read failed: " + ex.GetType().Name + ".");
                }
            }
        }
        finally
        {
            board.BoardIterator_Destroy(ref iterator);
        }

        return result;
    }

    private static List<TextItem> ReadTexts(
        IPCB_Board board,
        LayerCatalog layers,
        Dictionary<string, string> componentIds,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var result = new List<TextItem>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var iterator = board.BoardIterator_Create();
        try
        {
            var objectSet = new TObjectSet { TObjectId.eTextObject };
            iterator.AddFilter_ObjectSet(objectSet);
            iterator.AddFilter_Method(TIterationMethod.eProcessAll);
            var index = 0;
            for (var primitive = iterator.FirstPCBObject(); primitive is not null; primitive = iterator.NextPCBObject())
            {
                if (primitive is not IPCB_Text text)
                    continue;

                diagnostics.TextsDetected++;
                index++;
                try
                {
                    if (text.IsDesignator() || text.IsComment())
                    {
                        diagnostics.TextsSkipped++;
                        continue;
                    }

                    if (text.GetState_TextKind() == TTextKind.eText_BarCode)
                    {
                        diagnostics.TextsSkipped++;
                        if (diagnostics.TextsSkipped <= 5)
                            warnings.Add("A barcode text object was omitted; Core TextItem represents text only.");
                        continue;
                    }

                    var content = text.GetState_Text();
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        diagnostics.TextsSkipped++;
                        if (diagnostics.TextsSkipped <= 5)
                            warnings.Add("A text object with empty content was omitted.");
                        continue;
                    }

                    var component = text.GetState_Component();
                    string? componentId = null;
                    if (component is not null)
                    {
                        var uniqueId = component.GetState_UniqueId();
                        var sourceId = component.GetState_SourceUniqueId();
                        if ((!string.IsNullOrWhiteSpace(uniqueId) && componentIds.TryGetValue(uniqueId, out componentId))
                            || (!string.IsNullOrWhiteSpace(sourceId) && componentIds.TryGetValue(sourceId, out componentId)))
                        {
                            // Found a proven component relation.
                        }
                        else
                        {
                            diagnostics.TextComponentLinkMisses++;
                            if (diagnostics.TextComponentLinkMisses <= 5)
                            {
                                warnings.Add(
                                    "A text object references a component that is absent from the snapshot; " +
                                    "ComponentId was left empty.");
                            }
                        }
                    }

                    var layerId = layers.GetOrAdd((int)text.GetState_Layer(), null, warnings);
                    var size = text.GetState_Size();
                    var strokeWidth = text.GetState_Width();
                    var rotation = text.GetState_Rotation();
                    var id = EnsureUniqueId(
                        MakeId("text", text.GetState_UniqueId(), "index:" + index, warnings, diagnostics),
                        ids,
                        index);
                    result.Add(AltiumSnapshotMapping.MapText(
                        id,
                        componentId,
                        layerId,
                        text.GetState_XLocation(),
                        text.GetState_YLocation(),
                        content,
                        size,
                        strokeWidth,
                        rotation,
                        out var rotationKnown));
                    diagnostics.TextsExported++;
                    if (!rotationKnown)
                    {
                        diagnostics.TextUnknownRotations++;
                        if (diagnostics.TextUnknownRotations <= 5)
                        {
                            warnings.Add(
                                "A text object has no finite rotation; RotationDeg was left empty.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.TextReadErrors++;
                    if (diagnostics.TextReadErrors <= 5)
                        warnings.Add("Text read failed: " + ex.GetType().Name + ".");
                }
            }
        }
        finally
        {
            board.BoardIterator_Destroy(ref iterator);
        }

        return result;
    }

    private static List<Region> BuildCoreRegions(
        IReadOnlyList<AltiumRegionInfo> regionInfos,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var result = new List<Region>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var otherKinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var info in regionInfos)
        {
            if (info.Kind == TRegionKind.eRegionKind_BoardCutout)
                continue;

            diagnostics.CoreRegionsDetected++;
            var kind = AltiumSnapshotMapping.MapRegionKind(info.Kind, info.IsKeepout, out var unknown);
            if (unknown)
            {
                diagnostics.RegionUnknownKinds++;
            }
            else if (info.Kind is TRegionKind.eRegionKind_NamedRegion
                or TRegionKind.eRegionKind_Cutout
                or TRegionKind.eRegionKind_Cavity)
            {
                diagnostics.CoreRegionOtherKinds++;
                otherKinds.Add(info.Kind!.Value.ToString());
            }

            var contours = AltiumSnapshotMapping.MapRegionContours(
                info.Index,
                info.Contours,
                out var contourErrors);
            diagnostics.RegionReadErrors += contourErrors;
            if (contours.Count == 0)
            {
                diagnostics.CoreRegionsSkipped++;
                continue;
            }

            var id = EnsureUniqueId(
                MakeId("region", info.UniqueId, "index:" + info.Index, warnings, diagnostics),
                ids,
                info.Index);
            result.Add(new Region
            {
                Id = id,
                NetId = info.NetId,
                LayerId = info.LayerId,
                Kind = kind,
                Polygon = new PolygonGeometry { Contours = contours },
            });
            diagnostics.CoreRegionsExported++;
        }

        if (result.Count > 0)
        {
            diagnostics.RegionGeometryFallbacks += result.Count;
            warnings.Add(
                "Region geometry uses IPCB_GeometricPolygon contour points as line segments; " +
                "analytic region arc segments were not confirmed and may be approximated.");
        }

        if (otherKinds.Count > 0)
        {
            warnings.Add(
                "Region kinds without a Core 1.0.0 representation were exported as RegionKind.Other: " +
                string.Join(", ", otherKinds.OrderBy(kind => kind, StringComparer.Ordinal)) + ".");
        }

        return result;
    }

    private static IReadOnlyList<AltiumRegionContour> ReadRegionContours(
        IPCB_Region region,
        int regionIndex,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var isBoardCutout = false;
        try
        {
            isBoardCutout = region.GetState_Kind() == TRegionKind.eRegionKind_BoardCutout;
        }
        catch (Exception ex)
        {
            warnings.Add("Region kind read failed: " + ex.GetType().Name + ".");
        }

        void RecordError(string source, int index, string reason)
        {
            diagnostics.AddIssue(AltiumReadIssueKind.RegionContourReadError, source, index, reason);
            if (isBoardCutout)
            {
                diagnostics.BoardCutoutContourReadErrors++;
                diagnostics.UnresolvedCutoutSources.Add(SkippedContourKey(regionIndex, index));
            }
        }

        void RecordContourError(int index, string reason)
        {
            // A per-contour failure is recoverable until reconciliation proves otherwise.
            // BoardCutoutContourReadErrors is reserved for an unreadable whole source.
            diagnostics.AddIssue(
                AltiumReadIssueKind.RegionContourReadError,
                "RegionGeometricPolygon",
                index,
                reason);
            if (isBoardCutout)
                diagnostics.UnresolvedCutoutSources.Add(SkippedContourKey(regionIndex, index));
        }

        var result = new List<AltiumRegionContour>();
        var skipped = new List<SkippedRegionContour>();
        var polygonUnavailable = false;
        try
        {
            var polygon = region.GetGeometricPolygon();
            var count = polygon?.GetState_Count() ?? 0;
            if (polygon is null || count == 0)
            {
                polygonUnavailable = true;
                diagnostics.AddIssue(
                    AltiumReadIssueKind.GeometricPolygonUnavailable,
                    "RegionGeometricPolygon",
                    regionIndex,
                    "Region geometric polygon is null or empty.");
                if (isBoardCutout)
                    diagnostics.UnresolvedCutoutSources.Add(SkippedContourKey(regionIndex, -1));
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    bool? isHole = null;
                    try
                    {
                        isHole = polygon.GetState_IsHole(i);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(
                            $"Region geometric contour {i} role read failed: {ex.GetType().Name}.");
                    }

                    if (isHole is null)
                    {
                        RecordContourError(i, "Contour role is unavailable.");
                        skipped.Add(new SkippedRegionContour(i, null, Array.Empty<Point2D>()));
                        continue;
                    }

                    var contour = polygon.GetState_Contour(i);
                    if (contour is null)
                    {
                        RecordContourError(i, "Contour is null.");
                        skipped.Add(new SkippedRegionContour(i, isHole, Array.Empty<Point2D>()));
                        continue;
                    }

                    IReadOnlyList<Point2D> points;
                    try
                    {
                        points = ReadContourPoints(contour);
                    }
                    catch (Exception ex)
                    {
                        RecordContourError(
                            i,
                            "Point read failed: " + ex.GetType().Name + ".");
                        skipped.Add(new SkippedRegionContour(i, isHole, Array.Empty<Point2D>()));
                        continue;
                    }

                    if (points.Count < 2)
                    {
                        diagnostics.AddIssue(
                            AltiumReadIssueKind.RegionContourSkipped,
                            "RegionGeometricPolygon",
                            i,
                            $"Contour has {points.Count} point(s); at least two are required.");
                        skipped.Add(new SkippedRegionContour(i, isHole, points));
                        if (isBoardCutout)
                            diagnostics.UnresolvedCutoutSources.Add(SkippedContourKey(regionIndex, i));
                        continue;
                    }

                    result.Add(new AltiumRegionContour(
                        isHole.Value,
                        points,
                        "RegionGeometricPolygon",
                        i,
                        i));
                }
            }
        }
        catch (Exception ex)
        {
            polygonUnavailable = true;
            RecordError("RegionGeometricPolygon", -1, "Read failed: " + ex.GetType().Name + ".");
            warnings.Add(
                $"Region geometry read failed for kind={region.GetState_Kind()} " +
                $"name='{region.GetState_Name()}': {ex.GetType().Name}.");
        }

        var fallback = new List<AltiumRegionContour>();
        if (polygonUnavailable || result.Count == 0 || skipped.Count > 0)
        {
            try
            {
                var main = ReadContourPoints(region.GetMainContour());
                if (main.Count >= 2)
                    fallback.Add(new AltiumRegionContour(false, main, "MainContour", 0));

                var holeCount = region.GetHoleCount();
                for (var i = 0; i < holeCount; i++)
                {
                    var hole = ReadContourPoints(region.GetHole(i));
                    if (hole.Count >= 2)
                        fallback.Add(new AltiumRegionContour(true, hole, "HoleContour", i));
                }
            }
            catch (Exception ex)
            {
                RecordError("RegionMainAndHoleContours", -1, "Read failed: " + ex.GetType().Name + ".");
                warnings.Add(
                    $"Region contour read failed for kind={region.GetState_Kind()} " +
                    $"name='{region.GetState_Name()}': {ex.GetType().Name}.");
            }
        }

        return RecoverRegionContours(
            regionIndex,
            isBoardCutout,
            result,
            skipped,
            fallback,
            index => TryReReadRegionContour(region, index),
            warnings,
            diagnostics);
    }

    /// <summary>
    /// Re-reads the same geometric-polygon contour index. A skipped contour can only be
    /// recovered when this independent request still yields the original geometry; a
    /// missing or short second read leaves the source unresolved.
    /// </summary>
    private static RegionContourProbe? TryReReadRegionContour(IPCB_Region region, int index)
    {
        try
        {
            var polygon = region.GetGeometricPolygon();
            if (polygon is null || index < 0 || index >= polygon.GetState_Count())
                return null;

            var contour = polygon.GetState_Contour(index);
            if (contour is null)
                return null;

            var points = ReadContourPoints(contour);
            if (points.Count < 2)
                return null;

            return new RegionContourProbe(polygon.GetState_IsHole(index), points);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Recovers only production-reachable skipped contours: the original contour index must
    /// still be readable on a second request, its role must match, and the main/hole fallback
    /// must contain one unique exact geometric match with the same topological role.
    /// Nothing is removed from source accounting unless exact geometry and role are proven.
    /// </summary>
    internal static IReadOnlyList<AltiumRegionContour> RecoverRegionContours(
        int regionIndex,
        bool isBoardCutout,
        IReadOnlyList<AltiumRegionContour> geometricContours,
        IReadOnlyList<SkippedRegionContour> skipped,
        IReadOnlyList<AltiumRegionContour> fallback,
        Func<int, RegionContourProbe?> reReadContour,
        List<string> warnings,
        AltiumAdapterDiagnostics diagnostics)
    {
        var result = new List<AltiumRegionContour>(geometricContours);

        void MarkUnproven(SkippedRegionContour item, string reason)
        {
            if (!isBoardCutout)
                return;

            diagnostics.AddIssue(
                AltiumReadIssueKind.RegionContourUnproven,
                "RegionGeometricPolygon",
                item.Index,
                reason);
            diagnostics.BoardCutoutUnprovenSkippedContours++;
            diagnostics.UnresolvedCutoutSources.Add(SkippedContourKey(regionIndex, item.Index));
        }

        foreach (var item in skipped)
        {
            var skippedKey = SkippedContourKey(regionIndex, item.Index);
            if (item.IsHole is not bool role)
            {
                MarkUnproven(item, "Skipped contour topological role is unavailable.");
                continue;
            }

            RegionContourProbe? probe;
            try
            {
                probe = reReadContour(item.Index);
            }
            catch
            {
                probe = null;
            }

            if (probe is null || probe.Value.Points.Count < 2)
            {
                MarkUnproven(
                    item,
                    "Skipped contour could not be re-read with at least two points.");
                continue;
            }

            if (probe.Value.IsHole != role)
            {
                MarkUnproven(
                    item,
                    "Re-read contour has a different topological role from the skipped contour.");
                continue;
            }

            var fallbackIndex = FindUniqueExactContourIndex(
                probe.Value.Points,
                role,
                fallback);
            if (fallbackIndex < 0)
            {
                MarkUnproven(
                    item,
                    "No unique exact main/hole contour with the same topological role was found.");
                continue;
            }

            var recovered = fallback[fallbackIndex] with { ContourIndex = item.Index };
            var duplicateOf = FindExactContourIndex(recovered.Points, recovered.IsHole, result);
            diagnostics.UnresolvedCutoutSources.Remove(skippedKey);

            if (duplicateOf >= 0)
            {
                diagnostics.DuplicateCutoutSources.Add(skippedKey);
                diagnostics.RegionContourRecoveryDuplicateCount++;
                diagnostics.AddIssue(
                    AltiumReadIssueKind.RegionContourRecoveryDuplicate,
                    recovered.Source,
                    recovered.SourceIndex,
                    $"Region {regionIndex} contour {item.Index} exactly duplicates contour {duplicateOf}.");
                warnings.Add(
                    $"Region {regionIndex} contour {item.Index} was proven as an exact duplicate of " +
                    $"contour {duplicateOf} via {recovered.Source}[{recovered.SourceIndex}]; " +
                    "no second contour was exported.");
                continue;
            }

            result.Add(recovered);
            diagnostics.RegionContourRecoveredCount++;
            diagnostics.AddIssue(
                AltiumReadIssueKind.RegionContourRecovered,
                recovered.Source,
                recovered.SourceIndex,
                $"Region {regionIndex} contour {item.Index} was recovered exactly.");
            warnings.Add(
                $"Region {regionIndex} contour {item.Index} was recovered exactly via " +
                $"{recovered.Source}[{recovered.SourceIndex}] and forwarded for geometry processing.");
        }

        return result;
    }

    private static int FindUniqueExactContourIndex(
        IReadOnlyList<Point2D> points,
        bool isHole,
        IReadOnlyList<AltiumRegionContour> candidates)
    {
        var found = -1;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate.IsHole != isHole || !ContourPointsEqual(points, candidate.Points))
                continue;

            if (found >= 0)
                return -1;

            found = i;
        }

        return found;
    }

    private static int FindExactContourIndex(
        IReadOnlyList<Point2D> points,
        bool isHole,
        IReadOnlyList<AltiumRegionContour> candidates)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate.IsHole == isHole && ContourPointsEqual(points, candidate.Points))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Keys for skipped/error contours use a negative sentinel space that cannot collide with
    /// a real non-negative contour index. Index -1 (whole read failure) maps to int.MinValue.
    /// </summary>
    internal static CutoutSourceKey SkippedContourKey(int regionIndex, int contourIndex) =>
        new(regionIndex, contourIndex < 0
            ? int.MinValue
            : -1 - Math.Min(contourIndex, int.MaxValue - 1));

    internal static bool ContourPointsEqual(
        IReadOnlyList<Point2D> left,
        IReadOnlyList<Point2D> right)
    {
        // A closing point is a representation detail: rotate over unique vertices only.
        var first = CanonicalRing(left);
        var second = CanonicalRing(right);
        if (first.Count == 0 || first.Count != second.Count)
            return false;

        for (var offset = 0; offset < first.Count; offset++)
        {
            var forward = true;
            var reverse = true;
            for (var i = 0; i < first.Count; i++)
            {
                var candidate = first[(offset + i) % first.Count];
                if (!candidate.Equals(second[i]))
                    forward = false;
                if (!candidate.Equals(second[(second.Count - 1 - i + second.Count) % second.Count]))
                    reverse = false;
                if (!forward && !reverse)
                    break;
            }

            if (forward || reverse)
                return true;
        }

        return false;
    }

    private static IReadOnlyList<Point2D> CanonicalRing(IReadOnlyList<Point2D> points)
    {
        if (points.Count == 0)
            return Array.Empty<Point2D>();

        var count = points.Count >= 2 && points[0].Equals(points[^1])
            ? points.Count - 1
            : points.Count;
        if (count <= 0)
            return Array.Empty<Point2D>();

        var result = new Point2D[count];
        for (var i = 0; i < count; i++)
            result[i] = points[i];
        return result;
    }

    private static IReadOnlyList<Point2D> ReadContourPoints(IPCB_Contour? contour)
    {
        if (contour is null)
            return Array.Empty<Point2D>();

        var points = new List<Point2D>(contour.GetState_Count());
        for (var i = 0; i < contour.GetState_Count(); i++)
            points.Add(Point2D.FromIu(contour.GetState_PointX(i), contour.GetState_PointY(i)));

        return points;
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) GetBounds(IEnumerable<Point2D> points)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var point in points)
        {
            if (point.XIu is not int x || point.YIu is not int y)
                continue;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        return minX == int.MaxValue ? (0, 0, 0, 0) : (minX, minY, maxX, maxY);
    }

    private static string FormatRegionInventory(IReadOnlyList<AltiumRegionInfo> regions) =>
        string.Join(
            "; ",
            regions.Select(region =>
                $"{region.Index}:{region.Kind}:{region.Name}:contours={region.Contours.Count}:" +
                $"points={region.MainPoints.Count}:holes={region.HoleCount}:area={region.Area}:" +
                $"bounds=({region.MinX},{region.MinY})-({region.MaxX},{region.MaxY})"));

    private static void ReadComponents(
        IPCB_Board board,
        LayerCatalog layers,
        NetCatalog nets,
        List<ComponentInstance> components,
        List<AltiumComponentIdentity> componentIdentities,
        List<PadStack> padStacks,
        List<Pad> pads,
        Dictionary<string, string> componentIds,
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
            var componentIdsSeen = new HashSet<string>(StringComparer.Ordinal);
            for (var primitive = iterator.FirstPCBObject(); primitive is not null; primitive = iterator.NextPCBObject())
            {
                if (primitive is not IPCB_Component component)
                    continue;

                var rawDesignator = component.GetState_Name()?.GetState_Text() ?? string.Empty;
                var designator = rawDesignator;
                if (string.IsNullOrWhiteSpace(designator))
                {
                    designator = "UNNAMED_" + index;
                    warnings.Add("A component has no designator; a synthetic designator was used.");
                }

                var sourceUniqueId = AltiumComponentIdBuilder.NormalizeKey(component.GetState_SourceUniqueId());
                var uniqueId = AltiumComponentIdBuilder.NormalizeKey(component.GetState_UniqueId());
                var componentId = AltiumComponentIdBuilder.Build(
                    uniqueId,
                    sourceUniqueId,
                    designator,
                    index,
                    componentIdsSeen,
                    warnings,
                    diagnostics);
                if (!string.IsNullOrWhiteSpace(uniqueId))
                    componentIds.TryAdd(uniqueId, componentId);
                if (!string.IsNullOrWhiteSpace(sourceUniqueId))
                    componentIds.TryAdd(sourceUniqueId, componentId);
                var rawX = component.GetState_XLocation();
                var rawY = component.GetState_YLocation();
                var rotation = AngleNormalizer.NormalizeDegrees(component.GetState_Rotation());
                var side = ResolveComponentSide(component);
                componentIdentities.Add(new AltiumComponentIdentity(
                    componentId,
                    new AltiumComponentKey(
                        uniqueId,
                        sourceUniqueId,
                        rawDesignator,
                        rawX,
                        rawY,
                        rotation,
                        side)));
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
                        Position = Point2D.FromIu(rawX, rawY),
                        RotationDeg = rotation,
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
                    warnings,
                    diagnostics);
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

    private static LengthValue ReadBoardOrigin(
        IPCB_Board board,
        bool xOrigin,
        List<string> warnings)
    {
        try
        {
            return LengthValue.FromIu(xOrigin ? board.GetState_XOrigin() : board.GetState_YOrigin());
        }
        catch (Exception ex)
        {
            warnings.Add(
                "Board origin " + (xOrigin ? "X" : "Y") +
                " is unavailable: " + ex.GetType().Name + ".");
            return LengthValue.FromIu(null);
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

    internal static string MakeId(
        string kind,
        string? uniqueId,
        string fallback,
        List<string> warnings,
        AltiumAdapterDiagnostics? diagnostics = null)
    {
        if (!string.IsNullOrWhiteSpace(uniqueId))
            return "altium:" + kind + ":" + uniqueId;

        if (diagnostics is null || diagnostics.ShouldWarnFallbackId(kind))
            warnings.Add($"A {kind} has no source unique id; a deterministic fallback id was used.");
        return "altium:" + kind + ":fallback:" + fallback;
    }

    internal static string EnsureUniqueId(string id, HashSet<string> ids, int index)
    {
        if (ids.Add(id))
            return id;

        var candidate = id + ":dup:" + index;
        while (!ids.Add(candidate))
            candidate += "_";
        return candidate;
    }

    /// <summary>Side used by both the snapshot and the component identity resolver.</summary>
    internal static BoardSide ResolveComponentSide(IPCB_Component component)
    {
        var rawLayer = (int)component.GetState_Layer();
        if (rawLayer == 32)
            return BoardSide.Bottom;
        if (rawLayer == 1)
            return BoardSide.Top;
        return component.GetState_FlippedOnLayer() ? BoardSide.Bottom : BoardSide.Top;
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
