using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Core.Validation;

/// <summary>
/// CAD-independent structural validator. It never modifies geometry and returns both errors
/// (snapshot is not usable) and warnings (data is incomplete but structurally usable).
/// </summary>
public static class BoardSnapshotValidator
{
    private const double UnitToleranceMm = 1e-9;
    private const double GeometryToleranceMm = 1e-6;

    public static BoardValidationResult Validate(BoardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var diagnostics = new List<BoardDiagnostic>();
        ValidateBoard(snapshot, diagnostics);

        var layers = OrEmpty(snapshot.Layers);
        var components = OrEmpty(snapshot.Components);
        var padStacks = OrEmpty(snapshot.PadStacks);
        var pads = OrEmpty(snapshot.Pads);
        var nets = OrEmpty(snapshot.Nets);
        var tracks = OrEmpty(snapshot.Tracks);
        var vias = OrEmpty(snapshot.Vias);
        var regions = OrEmpty(snapshot.Regions);
        var texts = OrEmpty(snapshot.Texts);

        var layerIds = ValidateLayers(layers, diagnostics);
        var componentIds = ValidateComponents(components, layerIds, diagnostics);
        var netIds = ValidateNets(nets, diagnostics);
        var padStackIds = ValidatePadStacks(padStacks, layerIds, diagnostics);

        ValidatePads(pads, componentIds, netIds, layerIds, padStackIds, diagnostics);
        ValidateTracks(tracks, netIds, layerIds, diagnostics);
        ValidateVias(vias, netIds, layerIds, diagnostics);
        ValidateRegions(regions, netIds, layerIds, diagnostics);
        ValidateTexts(texts, componentIds, layerIds, diagnostics);

        if (components.Count == 0)
            Warning(BoardDiagnosticCodes.EmptyCollection, "components", "Snapshot contains no components.", diagnostics);

        if (nets.Count == 0)
            Warning(BoardDiagnosticCodes.EmptyCollection, "nets", "Snapshot contains no nets.", diagnostics);

        return new BoardValidationResult(diagnostics);
    }

    private static void ValidateBoard(BoardSnapshot snapshot, List<BoardDiagnostic> diagnostics)
    {
        if (snapshot.Properties is null)
        {
            Error(
                BoardDiagnosticCodes.MissingRequiredAttribute,
                "board.properties",
                "Board properties are required.",
                diagnostics);
        }
        else
        {
            ValidateId(snapshot.Properties.Id, "board.properties.id", null, diagnostics);
            ValidateOptionalLength(
                snapshot.Properties.Thickness,
                "board.properties.thickness",
                allowNegative: false,
                "Board thickness is unknown.",
                diagnostics);
        }

        if (snapshot.Outline is null)
        {
            Error(
                BoardDiagnosticCodes.MissingRequiredAttribute,
                "board.outline",
                "Board outline is required.",
                diagnostics);
            return;
        }

        ValidateId(snapshot.Outline.Id, "board.outline.id", null, diagnostics);
        ValidateContours(
            "board.outline.contours",
            snapshot.Outline.Contours,
            requireOuter: true,
            emptyIsError: false,
            diagnostics);
    }

    private static HashSet<string> ValidateLayers(
        IReadOnlyList<LayerRef> layers,
        List<BoardDiagnostic> diagnostics)
    {
        var ids = NewIdSet();
        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            if (layer is null)
            {
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, $"layers[{i}]", "Layer is null.", diagnostics);
                continue;
            }

            var path = $"layers[{i}]";
            ValidateId(layer.Id, path + ".id", ids, diagnostics);
            RequireString(layer.Name, path + ".name", "Layer name is required.", diagnostics);

            if (layer.Kind is null)
                Warning(BoardDiagnosticCodes.MissingOptionalData, path + ".kind", "Layer kind is unknown.", diagnostics);

            if (layer.Side is null)
                Warning(BoardDiagnosticCodes.MissingOptionalData, path + ".side", "Layer side is unknown.", diagnostics);
        }

        return ids;
    }

    private static HashSet<string> ValidateComponents(
        IReadOnlyList<ComponentInstance> components,
        HashSet<string> layerIds,
        List<BoardDiagnostic> diagnostics)
    {
        var ids = NewIdSet();
        for (var i = 0; i < components.Count; i++)
        {
            var component = components[i];
            if (component is null)
            {
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, $"components[{i}]", "Component is null.", diagnostics);
                continue;
            }

            var path = $"components[{i}]";
            ValidateId(component.Id, path + ".id", ids, diagnostics);
            RequireString(component.Designator, path + ".designator", "Component designator is required.", diagnostics);
            RequireReference(component.LayerId, layerIds, path + ".layerId", "Layer", diagnostics);

            if (component.Placement is null)
            {
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, path + ".placement", "Component placement is required.", diagnostics);
            }
            else
            {
                ValidateTransform(component.Placement, path + ".placement", diagnostics);
                if (component.Placement.Side is null)
                    Warning(BoardDiagnosticCodes.MissingOptionalData, path + ".placement.side", "Component side is unknown.", diagnostics);
            }

            ValidateOptionalLength(
                component.Height,
                path + ".height",
                allowNegative: false,
                "Component height is unknown.",
                diagnostics);

            if (string.IsNullOrWhiteSpace(component.Footprint))
                Warning(BoardDiagnosticCodes.MissingOptionalData, path + ".footprint", "Component footprint is unknown.", diagnostics);
        }

        return ids;
    }

    private static HashSet<string> ValidateNets(
        IReadOnlyList<Net> nets,
        List<BoardDiagnostic> diagnostics)
    {
        var ids = NewIdSet();
        for (var i = 0; i < nets.Count; i++)
        {
            var net = nets[i];
            if (net is null)
            {
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, $"nets[{i}]", "Net is null.", diagnostics);
                continue;
            }

            var path = $"nets[{i}]";
            ValidateId(net.Id, path + ".id", ids, diagnostics);
            RequireString(net.Name, path + ".name", "Net name is required.", diagnostics);
        }

        return ids;
    }

    private static HashSet<string> ValidatePadStacks(
        IReadOnlyList<PadStack> padStacks,
        HashSet<string> layerIds,
        List<BoardDiagnostic> diagnostics)
    {
        var ids = NewIdSet();
        for (var i = 0; i < padStacks.Count; i++)
        {
            var stack = padStacks[i];
            if (stack is null)
            {
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, $"padStacks[{i}]", "Pad stack is null.", diagnostics);
                continue;
            }

            var path = $"padStacks[{i}]";
            ValidateId(stack.Id, path + ".id", ids, diagnostics);
            var shapes = OrEmpty(stack.Shapes);
            var shapeIds = NewIdSet();
            for (var j = 0; j < shapes.Count; j++)
            {
                var shape = shapes[j];
                if (shape is null)
                {
                    Error(BoardDiagnosticCodes.MissingRequiredAttribute, $"{path}.shapes[{j}]", "Pad shape is null.", diagnostics);
                    continue;
                }

                var shapePath = $"{path}.shapes[{j}]";
                ValidateId(shape.Id, shapePath + ".id", shapeIds, diagnostics);

                if (shape.Kind is null)
                    Error(BoardDiagnosticCodes.MissingRequiredAttribute, shapePath + ".kind", "Pad shape kind is required.", diagnostics);

                if (!string.IsNullOrWhiteSpace(shape.LayerId))
                    RequireReference(shape.LayerId, layerIds, shapePath + ".layerId", "Layer", diagnostics);

                ValidateOptionalLength(shape.Width, shapePath + ".width", allowNegative: false, "Pad width is unknown.", diagnostics);
                ValidateOptionalLength(shape.Height, shapePath + ".height", allowNegative: false, "Pad height is unknown.", diagnostics);
                ValidateLengthIfPresent(shape.CornerRadius, shapePath + ".cornerRadius", allowNegative: false, diagnostics);

                var vertices = OrEmpty(shape.Vertices);
                for (var k = 0; k < vertices.Count; k++)
                    ValidatePoint(vertices[k], $"{shapePath}.vertices[{k}]", diagnostics);

                if (shape.Kind == PadShapeKind.Custom && vertices.Count < 3)
                    Error(BoardDiagnosticCodes.InvalidGeometry, shapePath + ".vertices", "Custom pad shape needs at least three vertices.", diagnostics);
            }
        }

        return ids;
    }

    private static void ValidatePads(
        IReadOnlyList<Pad> pads,
        HashSet<string> componentIds,
        HashSet<string> netIds,
        HashSet<string> layerIds,
        HashSet<string> padStackIds,
        List<BoardDiagnostic> diagnostics)
    {
        var ids = NewIdSet();
        for (var i = 0; i < pads.Count; i++)
        {
            var pad = pads[i];
            if (pad is null)
            {
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, $"pads[{i}]", "Pad is null.", diagnostics);
                continue;
            }

            var path = $"pads[{i}]";
            ValidateId(pad.Id, path + ".id", ids, diagnostics);
            RequireReference(pad.ComponentId, componentIds, path + ".componentId", "Component", diagnostics);
            RequireReference(pad.LayerId, layerIds, path + ".layerId", "Layer", diagnostics);

            if (string.IsNullOrWhiteSpace(pad.PadStackId))
                Warning(BoardDiagnosticCodes.MissingOptionalData, path + ".padStackId", "Pad stack is unknown.", diagnostics);
            else
                RequireReference(pad.PadStackId, padStackIds, path + ".padStackId", "Pad stack", diagnostics);

            OptionalReference(pad.NetId, netIds, path + ".netId", "Net", diagnostics);
            if (string.IsNullOrWhiteSpace(pad.NetId))
                Warning(BoardDiagnosticCodes.MissingOptionalData, path + ".netId", "Pad is not connected to a net.", diagnostics);

            ValidatePoint(pad.Position, path + ".position", diagnostics);
            ValidateRequiredDouble(pad.RotationDeg, path + ".rotationDeg", diagnostics);
        }
    }

    private static void ValidateTracks(
        IReadOnlyList<Track> tracks,
        HashSet<string> netIds,
        HashSet<string> layerIds,
        List<BoardDiagnostic> diagnostics)
    {
        var ids = NewIdSet();
        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            if (track is null)
            {
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, $"tracks[{i}]", "Track is null.", diagnostics);
                continue;
            }

            var path = $"tracks[{i}]";
            ValidateId(track.Id, path + ".id", ids, diagnostics);
            RequireReference(track.LayerId, layerIds, path + ".layerId", "Layer", diagnostics);
            OptionalReference(track.NetId, netIds, path + ".netId", "Net", diagnostics);
            ValidatePoint(track.Start, path + ".start", diagnostics);
            ValidatePoint(track.End, path + ".end", diagnostics);
            ValidateRequiredLength(track.Width, path + ".width", allowNegative: false, diagnostics);

            if (track.Start.HasValue && track.End.HasValue && PointsEqual(track.Start, track.End))
                Error(BoardDiagnosticCodes.InvalidGeometry, path, "Track has zero length.", diagnostics);
        }
    }

    private static void ValidateVias(
        IReadOnlyList<Via> vias,
        HashSet<string> netIds,
        HashSet<string> layerIds,
        List<BoardDiagnostic> diagnostics)
    {
        var ids = NewIdSet();
        for (var i = 0; i < vias.Count; i++)
        {
            var via = vias[i];
            if (via is null)
            {
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, $"vias[{i}]", "Via is null.", diagnostics);
                continue;
            }

            var path = $"vias[{i}]";
            ValidateId(via.Id, path + ".id", ids, diagnostics);
            OptionalReference(via.NetId, netIds, path + ".netId", "Net", diagnostics);
            ValidatePoint(via.Position, path + ".position", diagnostics);
            ValidateRequiredLength(via.Diameter, path + ".diameter", allowNegative: false, diagnostics);
            ValidateRequiredLength(via.HoleDiameter, path + ".holeDiameter", allowNegative: false, diagnostics);
            RequireReference(via.StartLayerId, layerIds, path + ".startLayerId", "Layer", diagnostics);
            RequireReference(via.EndLayerId, layerIds, path + ".endLayerId", "Layer", diagnostics);

            if (via.Diameter.Iu is int diameter && via.HoleDiameter.Iu is int hole && hole > diameter)
                Error(BoardDiagnosticCodes.InvalidGeometry, path + ".holeDiameter", "Via hole is larger than its diameter.", diagnostics);
        }
    }

    private static void ValidateRegions(
        IReadOnlyList<Region> regions,
        HashSet<string> netIds,
        HashSet<string> layerIds,
        List<BoardDiagnostic> diagnostics)
    {
        var ids = NewIdSet();
        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            if (region is null)
            {
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, $"regions[{i}]", "Region is null.", diagnostics);
                continue;
            }

            var path = $"regions[{i}]";
            ValidateId(region.Id, path + ".id", ids, diagnostics);
            RequireReference(region.LayerId, layerIds, path + ".layerId", "Layer", diagnostics);
            OptionalReference(region.NetId, netIds, path + ".netId", "Net", diagnostics);

            if (region.Kind is null)
                Warning(BoardDiagnosticCodes.MissingOptionalData, path + ".kind", "Region kind is unknown.", diagnostics);

            if (region.Polygon is null)
            {
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, path + ".polygon", "Region polygon is required.", diagnostics);
                continue;
            }

            ValidateContours(
                path + ".polygon.contours",
                region.Polygon.Contours,
                requireOuter: true,
                emptyIsError: true,
                diagnostics);
        }
    }

    private static void ValidateTexts(
        IReadOnlyList<TextItem> texts,
        HashSet<string> componentIds,
        HashSet<string> layerIds,
        List<BoardDiagnostic> diagnostics)
    {
        var ids = NewIdSet();
        for (var i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            if (text is null)
            {
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, $"texts[{i}]", "Text item is null.", diagnostics);
                continue;
            }

            var path = $"texts[{i}]";
            ValidateId(text.Id, path + ".id", ids, diagnostics);
            RequireReference(text.LayerId, layerIds, path + ".layerId", "Layer", diagnostics);
            OptionalReference(text.ComponentId, componentIds, path + ".componentId", "Component", diagnostics);
            ValidatePoint(text.Position, path + ".position", diagnostics);
            RequireString(text.Text, path + ".text", "Text content is required.", diagnostics);
            ValidateOptionalLength(text.Height, path + ".height", allowNegative: false, "Text height is unknown.", diagnostics);
            ValidateOptionalLength(text.StrokeWidth, path + ".strokeWidth", allowNegative: false, "Text stroke width is unknown.", diagnostics);
            ValidateRequiredDouble(text.RotationDeg, path + ".rotationDeg", diagnostics);
        }
    }

    private static void ValidateTransform(Transform transform, string path, List<BoardDiagnostic> diagnostics)
    {
        ValidatePoint(transform.Position, path + ".position", diagnostics);
        ValidateRequiredDouble(transform.RotationDeg, path + ".rotationDeg", diagnostics);
    }

    private static void ValidateContours(
        string path,
        IReadOnlyList<Contour>? contours,
        bool requireOuter,
        bool emptyIsError,
        List<BoardDiagnostic> diagnostics)
    {
        var items = OrEmpty(contours);
        if (items.Count == 0)
        {
            if (emptyIsError)
                Error(BoardDiagnosticCodes.InvalidPolygon, path, "Polygon has no contours.", diagnostics);
            else
                Warning(BoardDiagnosticCodes.MissingOptionalData, path, "Board outline has no contours.", diagnostics);
            return;
        }

        var ids = NewIdSet();
        var hasOuter = false;
        for (var i = 0; i < items.Count; i++)
        {
            var contour = items[i];
            if (contour is null)
            {
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, $"{path}[{i}]", "Contour is null.", diagnostics);
                continue;
            }

            var contourPath = $"{path}[{i}]";
            ValidateId(contour.Id, contourPath + ".id", ids, diagnostics);

            if (contour.Role is null)
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, contourPath + ".role", "Contour role is required.", diagnostics);
            else if (contour.Role == ContourRole.Outer)
                hasOuter = true;

            ValidateContour(contour, contourPath, diagnostics);
        }

        if (requireOuter && !hasOuter)
            Error(BoardDiagnosticCodes.InvalidPolygon, path, "Polygon needs at least one outer contour.", diagnostics);
    }

    private static void ValidateContour(Contour contour, string path, List<BoardDiagnostic> diagnostics)
    {
        var segments = OrEmpty(contour.Segments);
        if (segments.Count < 2)
        {
            Error(BoardDiagnosticCodes.InvalidContour, path + ".segments", "Contour needs at least two segments.", diagnostics);
            return;
        }

        ContourSegment? previous = null;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment is null)
            {
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, $"{path}.segments[{i}]", "Contour segment is null.", diagnostics);
                continue;
            }

            var segmentPath = $"{path}.segments[{i}]";
            ValidatePoint(segment.Start, segmentPath + ".start", diagnostics);
            ValidatePoint(segment.End, segmentPath + ".end", diagnostics);

            switch (segment)
            {
                case LineSegment:
                    if (segment.Start.HasValue && segment.End.HasValue && PointsEqual(segment.Start, segment.End))
                        Error(BoardDiagnosticCodes.InvalidContour, segmentPath, "Line segment has zero length.", diagnostics);
                    break;
                case ArcSegment arc:
                    ValidateArc(arc, segmentPath, diagnostics);
                    break;
                default:
                    Error(BoardDiagnosticCodes.InvalidContour, segmentPath, "Unknown segment type.", diagnostics);
                    break;
            }

            if (previous is not null
                && previous.End.HasValue
                && segment.Start.HasValue
                && !PointsEqual(previous.End, segment.Start))
            {
                Error(BoardDiagnosticCodes.InvalidContour, segmentPath + ".start", "Contour is not continuous.", diagnostics);
            }

            previous = segment;
        }

        if (previous is not null
            && previous.End.HasValue
            && segments[0] is not null
            && segments[0].Start.HasValue
            && !PointsEqual(previous.End, segments[0].Start))
        {
            Error(BoardDiagnosticCodes.InvalidContour, path, "Contour is not closed.", diagnostics);
        }
    }

    private static void ValidateArc(ArcSegment arc, string path, List<BoardDiagnostic> diagnostics)
    {
        ValidatePoint(arc.Center, path + ".center", diagnostics);
        ValidateRequiredLength(arc.Radius, path + ".radius", allowNegative: false, diagnostics);

        if (arc.SweepDirection is null)
            Error(BoardDiagnosticCodes.MissingRequiredAttribute, path + ".sweepDirection", "Arc sweep direction is required.", diagnostics);

        if (arc.Start.HasValue && arc.End.HasValue && PointsEqual(arc.Start, arc.End))
            Error(BoardDiagnosticCodes.InvalidArc, path, "Arc has equal start and end points.", diagnostics);

        if (arc.Radius.Mm is not double radius)
            return;

        if (radius <= 0)
        {
            Error(BoardDiagnosticCodes.InvalidArc, path + ".radius", "Arc radius must be positive.", diagnostics);
            return;
        }

        if (arc.Center.HasValue && arc.Start.HasValue && !RadiusMatches(arc.Start, arc.Center, radius))
            Error(BoardDiagnosticCodes.InvalidArc, path + ".start", "Arc start point does not lie on the radius.", diagnostics);

        if (arc.Center.HasValue && arc.End.HasValue && !RadiusMatches(arc.End, arc.Center, radius))
            Error(BoardDiagnosticCodes.InvalidArc, path + ".end", "Arc end point does not lie on the radius.", diagnostics);
    }

    private static void ValidatePoint(Point2D point, string path, List<BoardDiagnostic> diagnostics)
    {
        ValidateRequiredLength(point.X, path + ".x", allowNegative: true, diagnostics);
        ValidateRequiredLength(point.Y, path + ".y", allowNegative: true, diagnostics);
    }

    private static void ValidateRequiredLength(
        LengthValue value,
        string path,
        bool allowNegative,
        List<BoardDiagnostic> diagnostics)
    {
        ValidateLength(value, path, required: true, allowNegative, diagnostics);
    }

    private static void ValidateOptionalLength(
        LengthValue value,
        string path,
        bool allowNegative,
        string missingMessage,
        List<BoardDiagnostic> diagnostics)
    {
        if (!value.HasValue)
        {
            CheckUnitPair(value, path, diagnostics);
            if (!value.HasProvidedMm)
                Warning(BoardDiagnosticCodes.MissingOptionalData, path, missingMessage, diagnostics);
            return;
        }

        ValidateLength(value, path, required: false, allowNegative, diagnostics);
    }

    private static void ValidateLengthIfPresent(
        LengthValue value,
        string path,
        bool allowNegative,
        List<BoardDiagnostic> diagnostics)
    {
        if (!value.HasValue && !value.HasProvidedMm)
            return;

        ValidateLength(value, path, required: false, allowNegative, diagnostics);
    }

    private static void ValidateLength(
        LengthValue value,
        string path,
        bool required,
        bool allowNegative,
        List<BoardDiagnostic> diagnostics)
    {
        CheckUnitPair(value, path, diagnostics);

        if (!value.HasValue)
        {
            if (required && !value.HasProvidedMm)
                Error(BoardDiagnosticCodes.MissingRequiredAttribute, path, "A value in Altium IU is required.", diagnostics);
            return;
        }

        if (!allowNegative && value.Iu is < 0)
            Error(BoardDiagnosticCodes.NegativeDimension, path, "Dimension must not be negative.", diagnostics);
    }

    private static void CheckUnitPair(LengthValue value, string path, List<BoardDiagnostic> diagnostics)
    {
        if (!value.HasConsistentMm(UnitToleranceMm))
            Error(BoardDiagnosticCodes.InconsistentUnitPair, path, "IU and mm values are inconsistent.", diagnostics);

        if (value.HasProvidedMm && value.ProvidedMm is double provided && !double.IsFinite(provided))
            Error(BoardDiagnosticCodes.NonFiniteNumber, path, "Millimetre value must be finite.", diagnostics);
    }

    private static void ValidateRequiredDouble(double? value, string path, List<BoardDiagnostic> diagnostics)
    {
        if (value is null)
        {
            Error(BoardDiagnosticCodes.MissingRequiredAttribute, path, "A finite number is required.", diagnostics);
            return;
        }

        if (!double.IsFinite(value.Value))
        {
            Error(BoardDiagnosticCodes.NonFiniteNumber, path, "Value must be finite.", diagnostics);
            return;
        }

        if (value.Value is < -180 or >= 180)
            Error(BoardDiagnosticCodes.InvalidGeometry, path, "Rotation must be in [-180, 180).", diagnostics);
    }

    private static void ValidateId(
        string? id,
        string path,
        HashSet<string>? ids,
        List<BoardDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            Error(BoardDiagnosticCodes.MissingId, path, "A non-empty opaque ID is required.", diagnostics);
            return;
        }

        if (ids is not null && !ids.Add(id))
            Error(BoardDiagnosticCodes.DuplicateId, path, $"Duplicate ID '{id}'.", diagnostics);
    }

    private static void RequireString(
        string? value,
        string path,
        string message,
        List<BoardDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
            Error(BoardDiagnosticCodes.MissingRequiredAttribute, path, message, diagnostics);
    }

    private static void RequireReference(
        string? id,
        HashSet<string> validIds,
        string path,
        string kind,
        List<BoardDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            Error(BoardDiagnosticCodes.MissingRequiredAttribute, path, $"{kind} reference is required.", diagnostics);
            return;
        }

        if (!validIds.Contains(id))
            Error(BoardDiagnosticCodes.InvalidReference, path, $"{kind} '{id}' does not exist.", diagnostics);
    }

    private static void OptionalReference(
        string? id,
        HashSet<string> validIds,
        string path,
        string kind,
        List<BoardDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        if (!validIds.Contains(id))
            Error(BoardDiagnosticCodes.InvalidReference, path, $"{kind} '{id}' does not exist.", diagnostics);
    }

    private static bool RadiusMatches(Point2D point, Point2D center, double radius)
    {
        if (point.XMm is not double x || point.YMm is not double y
            || center.XMm is not double cx || center.YMm is not double cy)
        {
            return false;
        }

        var dx = x - cx;
        var dy = y - cy;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        return Math.Abs(distance - radius) <= GeometryToleranceMm;
    }

    private static bool PointsEqual(Point2D left, Point2D right)
    {
        return left.XMm is double lx
            && left.YMm is double ly
            && right.XMm is double rx
            && right.YMm is double ry
            && Math.Abs(lx - rx) <= GeometryToleranceMm
            && Math.Abs(ly - ry) <= GeometryToleranceMm;
    }

    private static HashSet<string> NewIdSet() => new(StringComparer.Ordinal);

    private static IReadOnlyList<T> OrEmpty<T>(IReadOnlyList<T>? items) =>
        items ?? Array.Empty<T>();

    private static void Error(string code, string path, string message, List<BoardDiagnostic> diagnostics) =>
        diagnostics.Add(new BoardDiagnostic(code, BoardDiagnosticSeverity.Error, path, message));

    private static void Warning(string code, string path, string message, List<BoardDiagnostic> diagnostics) =>
        diagnostics.Add(new BoardDiagnostic(code, BoardDiagnosticSeverity.Warning, path, message));
}
