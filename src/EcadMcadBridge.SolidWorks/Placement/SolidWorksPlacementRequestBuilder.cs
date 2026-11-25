using System.Globalization;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using EcadMcadBridge.Core.Placement;
using EcadMcadBridge.SolidWorks.Importing;

namespace EcadMcadBridge.SolidWorks.Placement;

/// <summary>
/// Converts a validated Exchange Package into deterministic Top/Top placement requests.
/// Unsupported metadata never becomes a zero transform; it becomes an explicit diagnostic.
/// </summary>
public static class SolidWorksPlacementRequestBuilder
{
    private const double ModelRotationToleranceDeg = 1e-6;
    private const int DefaultMaxComponents = 20;

    public static SolidWorksPlacementPlan Build(
        PackageReadResult package,
        SolidWorksImportPlan importPlan,
        IReadOnlyCollection<string>? designators = null,
        int maxComponents = DefaultMaxComponents)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(importPlan);

        var diagnostics = new List<SolidWorksDiagnostic>();
        var requests = new List<SolidWorksPlacementRequest>();
        var unsupported = 0;

        if (!package.IsSuccess || package.Snapshot is null || package.Models is null)
        {
            diagnostics.Add(Error(
                "SW-PLC-001",
                "package",
                "Exchange Package with a snapshot and model catalog is required."));
            return Empty(diagnostics, designators, unsupported, Vector2Mm.Zero);
        }

        var snapshot = package.Snapshot;
        var boardOrigin = GetBoardOrigin(snapshot);
        var snapshotComponents = snapshot.Components
            .GroupBy(component => component.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var catalogComponents = package.Models.Components
            .GroupBy(component => component.ComponentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var assets = importPlan.Assets
            .GroupBy(asset => asset.AssetPath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var importReferences = new HashSet<string>(
            importPlan.BodyReferences.Select(ReferenceKey),
            StringComparer.Ordinal);

        var thickness = snapshot.Properties.Thickness.Mm;
        if (!thickness.HasValue || !double.IsFinite(thickness.Value) || thickness.Value <= 0)
        {
            diagnostics.Add(Error(
                "SW-PLC-002",
                "board.thickness",
                "A finite positive BoardSnapshot.Properties.Thickness is required for placement."));
            return Empty(diagnostics, designators, unsupported, boardOrigin);
        }

        var topSurface = thickness.Value / 2.0;
        var bottomSurface = -topSurface;

        foreach (var component in catalogComponents.Values
                     .OrderBy(item => item.ComponentId, StringComparer.Ordinal))
        {
            if (!snapshotComponents.TryGetValue(component.ComponentId, out var instance))
            {
                diagnostics.Add(Error(
                    "SW-PLC-003",
                    component.ComponentId,
                    "Model catalog component is absent from the board snapshot."));
                continue;
            }

            foreach (var body in component.Bodies.OrderBy(item => item.BodyOrdinal))
            {
                var reference = new SolidWorksBodyReference(
                    component.ComponentId,
                    instance.Designator,
                    body.BodyOrdinal,
                    body.AssetPath ?? string.Empty,
                    body.Representation,
                    body.SourceOrigin);

                if (!TryBuildRequest(
                        snapshot,
                        instance,
                        body,
                        reference,
                        importReferences,
                        assets,
                        boardOrigin,
                        topSurface,
                        bottomSurface,
                        diagnostics,
                        out var request))
                {
                    unsupported++;
                    continue;
                }

                requests.Add(request!);
            }
        }

        var selected = Select(requests, snapshotComponents, designators, maxComponents, diagnostics);
        var missing = designators is null
            ? Array.Empty<string>()
            : designators
                .Where(designator => !snapshotComponents.Values.Any(component =>
                    string.Equals(component.Designator, designator, StringComparison.Ordinal)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(designator => designator, StringComparer.Ordinal)
                .ToArray();

        foreach (var designator in missing)
        {
            diagnostics.Add(Error(
                "SW-PLC-017",
                designator,
                "Requested designator is not present in the board snapshot."));
        }

        return new SolidWorksPlacementPlan(
            selected,
            diagnostics,
            requests.Count,
            unsupported,
            designators?.OrderBy(item => item, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>(),
            missing,
            boardOrigin);
    }

    private static SolidWorksPlacementRequest[] Select(
        IReadOnlyList<SolidWorksPlacementRequest> requests,
        IReadOnlyDictionary<string, ComponentInstance> components,
        IReadOnlyCollection<string>? designators,
        int maxComponents,
        List<SolidWorksDiagnostic> diagnostics)
    {
        if (designators is { Count: > 0 })
        {
            var requested = new HashSet<string>(designators, StringComparer.Ordinal);
            var selected = requests
                .Where(request => requested.Contains(request.Designator))
                .OrderBy(request => request.Designator, StringComparer.Ordinal)
                .ThenBy(request => request.ComponentId, StringComparer.Ordinal)
                .ThenBy(request => request.BodyOrdinal)
                .ToArray();

            foreach (var designator in requested)
            {
                if (!components.Values.Any(component =>
                        string.Equals(component.Designator, designator, StringComparison.Ordinal)))
                    continue;

                if (!selected.Any(request => string.Equals(request.Designator, designator, StringComparison.Ordinal)))
                {
                    diagnostics.Add(Error(
                        "SW-PLC-018",
                        designator,
                        "Requested designator has no supported Top/Top Generic body reference."));
                }
            }

            return selected;
        }

        var groupLimit = maxComponents > 0 ? maxComponents : DefaultMaxComponents;
        var groups = requests
            .GroupBy(request => request.ComponentId, StringComparer.Ordinal)
            .OrderBy(group => group.First().Designator, StringComparer.Ordinal)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(groupLimit);

        return groups
            .SelectMany(group => group.OrderBy(request => request.BodyOrdinal))
            .ToArray();
    }

    private static bool TryBuildRequest(
        BoardSnapshot snapshot,
        ComponentInstance instance,
        ComponentBodyEntry body,
        SolidWorksBodyReference reference,
        IReadOnlySet<string> importReferences,
        IReadOnlyDictionary<string, SolidWorksStepAsset> assets,
        Vector2Mm boardOrigin,
        double topSurface,
        double bottomSurface,
        List<SolidWorksDiagnostic> diagnostics,
        out SolidWorksPlacementRequest? request)
    {
        request = null;
        var path = instance.Designator + "/" + body.BodyOrdinal.ToString(CultureInfo.InvariantCulture);

        if (!importReferences.Contains(ReferenceKey(reference)))
        {
            AddUnsupported(diagnostics, "SW-PLC-004", path, "Body reference is absent from the SOLIDWORKS import plan.");
            return false;
        }

        if (body.AssetPath is null || !assets.TryGetValue(body.AssetPath, out var asset))
        {
            AddUnsupported(diagnostics, "SW-PLC-005", path, "Body has no STEP asset in the import plan.");
            return false;
        }

        if (instance.Placement.Side != BoardSide.Top)
        {
            AddUnsupported(diagnostics, "SW-PLC-006", path, "Only Top component side is supported.");
            return false;
        }

        if (body.BodyProjection != ComponentBodyProjection.Top)
        {
            AddUnsupported(diagnostics, "SW-PLC-007", path, "Only Top BodyProjection is supported.");
            return false;
        }

        if (body.Representation != ComponentModelRepresentation.Generic)
        {
            AddUnsupported(diagnostics, "SW-PLC-008", path, "Only Generic model representation is supported.");
            return false;
        }

        if (body.SourceOrigin != ComponentModelSourceOrigin.Embedded)
        {
            AddUnsupported(diagnostics, "SW-PLC-009", path, "Only Embedded model assets are supported.");
            return false;
        }

        if (body.AssetFormat != ComponentModelAssetFormat.StepAp214)
        {
            AddUnsupported(diagnostics, "SW-PLC-010", path, "Only step-ap214 assets are supported.");
            return false;
        }

        if (body.AssetAcquisition != ComponentModelAssetAcquisition.SdkExport)
        {
            AddUnsupported(diagnostics, "SW-PLC-011", path, "Only sdk-export assets are supported.");
            return false;
        }

        if (body.ModelStateSource != ComponentModelStateSource.PlacedBodyEffective)
        {
            AddUnsupported(diagnostics, "SW-PLC-012", path, "PlacedBodyEffective model state is required.");
            return false;
        }

        if (instance.Placement.Position.XMm is not { } componentX
            || instance.Placement.Position.YMm is not { } componentY
            || instance.Placement.RotationDeg is not { } componentRotation)
        {
            AddUnsupported(diagnostics, "SW-PLC-013", path, "Component position and rotation are required.");
            return false;
        }

        if (body.Origin.XMm is not { } originX || body.Origin.YMm is not { } originY)
        {
            AddUnsupported(diagnostics, "SW-PLC-014", path, "Model origin X/Y is required.");
            return false;
        }

        if (body.State.RotationXDegRaw is not { } rx
            || body.State.RotationYDegRaw is not { } ry
            || body.State.RotationZDegRaw is not { } rz
            || body.State.DzMm is not { } dz)
        {
            AddUnsupported(diagnostics, "SW-PLC-015", path, "Complete model state is required.");
            return false;
        }

        if (body.ModelRotationDegRaw is not { } modelRotation
            || !double.IsFinite(modelRotation)
            || Math.Abs(modelRotation) > ModelRotationToleranceDeg)
        {
            AddUnsupported(
                diagnostics,
                "SW-PLC-016",
                path,
                "ModelRotationDegRaw is missing or non-zero; it cannot be ignored.");
            return false;
        }

        if (body.StandoffHeight.Mm is not { } standoff || !double.IsFinite(standoff))
        {
            AddUnsupported(diagnostics, "SW-PLC-019", path, "StandoffHeight is required.");
            return false;
        }

        var deltaBoardX = originX - componentX;
        var deltaBoardY = originY - componentY;
        var radians = -componentRotation * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var localOffset = new Vector2Mm(
            cos * deltaBoardX - sin * deltaBoardY,
            sin * deltaBoardX + cos * deltaBoardY);
        var boardX = componentX - boardOrigin.X;
        var boardY = componentY - boardOrigin.Y;

        var requestModel = new PlacementTransformRequest
        {
            ComponentPositionBoardMm = new Vector2Mm(boardX, boardY),
            ComponentRotationDeg = componentRotation,
            ComponentSide = ComponentSide.Top,
            BodyProjection = BodyProjection.Top,
            FootprintCalibration = new FootprintOriginCalibration(localOffset, null),
            AssetOriginLocalMm = Vector3Mm.Zero,
            ModelState = new ModelStateInput(rx, ry, rz, dz),
            ModelStateSource = ModelStateSource.PlacedBodyEffective,
            BoardSurfaceFrame = new BoardSurfaceFrame(topSurface, bottomSurface),
            StandoffHeightMm = standoff,
            ModelRepresentation = ModelRepresentation.GenericEmbedded,
            ModelSourceOrigin = ModelSourceOrigin.Embedded,
        };

        var result = PlacementTransformBuilder.Build(requestModel);
        if (!result.IsSupported || result.Transform is null)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-PLC-020",
                    SolidWorksDiagnosticSeverity.Warning,
                    path,
                    diagnostic.Code + ": " + diagnostic.Message));
            }

            return false;
        }

        request = new SolidWorksPlacementRequest(
            instance.Id,
            instance.Designator,
            body.BodyOrdinal,
            body.AssetPath,
            asset.NativePartPath,
            result.Transform.Value,
            result.NormalizedModelState,
            body.ModelStateSource.ToString(),
            body.BodyProjection,
            BoardSide.Top,
            standoff,
            body.OverallHeight.Mm,
            modelRotation,
            new Vector2Mm(originX - boardOrigin.X, originY - boardOrigin.Y),
            localOffset,
            boardOrigin);
        return true;
    }

    private static Vector2Mm GetBoardOrigin(BoardSnapshot snapshot)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var contour in snapshot.Outline?.Contours ?? Array.Empty<Contour>())
        {
            foreach (var segment in contour.Segments ?? Array.Empty<ContourSegment>())
            {
                Add(segment.Start);
                Add(segment.End);
                if (segment is ArcSegment arc)
                    Add(arc.Center);
            }
        }

        return xs.Count == 0
            ? Vector2Mm.Zero
            : new Vector2Mm(xs.Min(), ys.Min());

        void Add(Point2D point)
        {
            if (point.XMm is { } x && point.YMm is { } y && double.IsFinite(x) && double.IsFinite(y))
            {
                xs.Add(x);
                ys.Add(y);
            }
        }
    }

    private static string ReferenceKey(SolidWorksBodyReference reference) =>
        reference.ComponentId + "\u001f" + reference.BodyOrdinal.ToString(CultureInfo.InvariantCulture);

    private static SolidWorksPlacementPlan Empty(
        List<SolidWorksDiagnostic> diagnostics,
        IReadOnlyCollection<string>? designators,
        int unsupported,
        Vector2Mm boardOrigin) =>
        new(
            Array.Empty<SolidWorksPlacementRequest>(),
            diagnostics,
            0,
            unsupported,
            designators?.OrderBy(item => item, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>(),
            Array.Empty<string>(),
            boardOrigin);

    private static void AddUnsupported(
        List<SolidWorksDiagnostic> diagnostics,
        string code,
        string path,
        string message) =>
        diagnostics.Add(new SolidWorksDiagnostic(
            code,
            SolidWorksDiagnosticSeverity.Warning,
            path,
            message));

    private static SolidWorksDiagnostic Error(string code, string path, string message) =>
        new(code, SolidWorksDiagnosticSeverity.Error, path, message);
}
