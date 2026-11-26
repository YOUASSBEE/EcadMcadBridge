using System.Globalization;
using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using EcadMcadBridge.SolidWorks.Placement;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace EcadMcadBridge.SolidWorks.Importing;

/// <summary>One numerically checked point inside imported board geometry.</summary>
public sealed record SolidWorksBoardProbe(
    string Kind,
    string ContourId,
    double XMm,
    double YMm,
    double ZMm,
    bool ExpectedInsideSolid,
    bool InsideSolid);

/// <summary>Measured board geometry; all values are millimetres and mm^3.</summary>
public sealed record SolidWorksBoardGeometry(
    int SolidBodyCount,
    double MinX,
    double MinY,
    double MinZ,
    double MaxX,
    double MaxY,
    double MaxZ,
    double WidthMm,
    double HeightMm,
    double ThicknessMm,
    double NominalThicknessMm,
    double ThicknessDeltaMm,
    double VolumeMm3,
    double ExpectedVolumeMm3,
    double VolumeResidualMm3,
    double ExpectedWidthMm,
    double ExpectedHeightMm,
    double XyBoundsResidualMm,
    double ZTopResidualMm,
    double FrameOriginXMm,
    double FrameOriginYMm,
    IReadOnlyList<SolidWorksBodyMeasurement> Bodies,
    IReadOnlyList<SolidWorksBoardProbe> Probes);

/// <summary>One imported solid body's measured volume and its outline-derived expectation.</summary>
public sealed record SolidWorksBodyMeasurement(
    int Index,
    double MinX,
    double MinY,
    double MinZ,
    double MaxX,
    double MaxY,
    double MaxZ,
    double ThicknessMm,
    double MeasuredVolumeMm3,
    double OutlineAreaMm2,
    double ExpectedVolumeMm3,
    double VolumeResidualMm3,
    int TriangleCount);

public sealed record SolidWorksBoardGeometryResult(
    SolidWorksBoardGeometry? Geometry,
    IReadOnlyList<SolidWorksDiagnostic> Diagnostics)
{
    public bool IsValid =>
        Geometry is not null
        && Diagnostics.All(item => item.Severity != SolidWorksDiagnosticSeverity.Error);
}

/// <summary>
/// Measures an imported board part and validates it against the snapshot outline. It never
/// substitutes nominal properties for measured geometry.
/// </summary>
internal readonly record struct SolidWorksBodyBox(
    double MinX,
    double MinY,
    double MinZ,
    double MaxX,
    double MaxY,
    double MaxZ,
    double VolumeMm3);

internal readonly record struct SolidWorksBodyVolume(
    double ThicknessMm,
    double OutlineAreaMm2,
    double ExpectedVolumeMm3);

internal static class SolidWorksBoardGeometryInspector
{
    private const double BoundsToleranceMm = 0.05;
    private const double UnitToleranceMm = 1e-9;
    private const double VolumeAbsoluteToleranceMm3 = 5.0;
    private const double VolumeRelativeTolerance = 0.02;
    private const int ArcSamples = 64;

    /// <summary>SOLIDWORKS body geometry APIs return metres; the exchange contract is millimetres.</summary>
    private const double MetresToMm = 1000.0;

    private const double CubicMetresToCubicMm = 1_000_000_000.0;

    public static SolidWorksBoardGeometryResult Inspect(PartDoc part, BoardSnapshot snapshot)
    {
        var diagnostics = new List<SolidWorksDiagnostic>();
        if (!TryReadFrameOrigin(snapshot, diagnostics, out var originX, out var originY))
            return new SolidWorksBoardGeometryResult(null, diagnostics);

        var bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (bodies is null || bodies.Length == 0)
        {
            Error("SW-BRD-001", "board", "Imported board contains no solid bodies.", diagnostics);
            return new SolidWorksBoardGeometryResult(null, diagnostics);
        }

        var inputs = new List<(Body2 Body, SolidWorksBodyBox Box)>();
        for (var i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] is not Body2 body)
            {
                Error("SW-BRD-002", "board.body[" + i + "]", "A body entry is not IBody2.", diagnostics);
                continue;
            }

            var box = body.GetBodyBox() as double[];
            if (box is not { Length: >= 6 })
            {
                Error("SW-BRD-003", "board.body[" + i + "]", "IBody2.GetBodyBox returned no usable box.", diagnostics);
                continue;
            }

            var mass = body.GetMassProperties(1.0) as double[];
            if (mass is not { Length: > 3 })
            {
                Error("SW-BRD-004", "board.body[" + i + "]", "IBody2.GetMassProperties returned no usable volume.", diagnostics);
                continue;
            }

            inputs.Add((body, new SolidWorksBodyBox(
                box[0] * MetresToMm,
                box[1] * MetresToMm,
                box[2] * MetresToMm,
                box[3] * MetresToMm,
                box[4] * MetresToMm,
                box[5] * MetresToMm,
                mass[3] * CubicMetresToCubicMm)));
        }

        var volume = inputs.Sum(item => item.Box.VolumeMm3);
        if (inputs.Count == 0 || !double.IsFinite(volume) || volume <= 0)
        {
            Error("SW-BRD-004", "board", "Imported board volume is missing or not positive.", diagnostics);
            return new SolidWorksBoardGeometryResult(null, diagnostics);
        }

        if (bodies.Length != 1)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-BRD-005",
                SolidWorksDiagnosticSeverity.Warning,
                "board",
                "Imported board contains " + bodies.Length.ToString(CultureInfo.InvariantCulture) +
                " solid bodies; they are treated as one substrate part."));
        }

        if (!BoardOutlineBounds.TryCompute(snapshot.Outline, out var bounds, out var outlineError))
        {
            Error("SW-BRD-006", "board.outline", "Board outline bounds are unavailable: " + outlineError, diagnostics);
            return new SolidWorksBoardGeometryResult(null, diagnostics);
        }

        var localMinX = inputs.Min(item => item.Box.MinX);
        var localMinY = inputs.Min(item => item.Box.MinY);
        var minZ = inputs.Min(item => item.Box.MinZ);
        var localMaxX = inputs.Max(item => item.Box.MaxX);
        var localMaxY = inputs.Max(item => item.Box.MaxY);
        var maxZ = inputs.Max(item => item.Box.MaxZ);
        var minX = localMinX + originX;
        var minY = localMinY + originY;
        var maxX = localMaxX + originX;
        var maxY = localMaxY + originY;
        var expectedWidth = bounds.MaxX - bounds.MinX;
        var expectedHeight = bounds.MaxY - bounds.MinY;
        var width = maxX - minX;
        var height = maxY - minY;
        var thickness = maxZ - minZ;
        var xyResidual = Math.Max(
            Math.Max(Math.Abs(minX - bounds.MinX), Math.Abs(minY - bounds.MinY)),
            Math.Max(Math.Abs(maxX - bounds.MaxX), Math.Abs(maxY - bounds.MaxY)));

        if (!double.IsFinite(thickness) || thickness <= 0)
        {
            Error("SW-BRD-007", "board", "Measured board thickness is not positive.", diagnostics);
        }

        if (xyResidual > BoundsToleranceMm)
        {
            Error(
                "SW-BRD-008",
                "board",
                "Imported board XY bounds do not match the board-local Outer contour: residual=" +
                xyResidual.ToString("R", CultureInfo.InvariantCulture) + " mm.",
                diagnostics);
        }

        var zTopResidual = Math.Abs(maxZ);
        if (zTopResidual > BoundsToleranceMm)
        {
            Error(
                "SW-BRD-009",
                "board",
                "Imported board top surface is not at board Z=0: residual=" +
                zTopResidual.ToString("R", CultureInfo.InvariantCulture) + " mm.",
                diagnostics);
        }

        var bodyVolumes = EstimateBodyVolumes(
            inputs.Select(item => item.Box).ToArray(),
            snapshot.Outline,
            originX,
            originY,
            diagnostics);
        var expectedVolume = bodyVolumes.Sum(item => item.ExpectedVolumeMm3);
        var volumeResidual = volume - expectedVolume;
        var volumeTolerance = Math.Max(
            VolumeAbsoluteToleranceMm3,
            Math.Abs(expectedVolume) * VolumeRelativeTolerance);
        if (Math.Abs(volumeResidual) > volumeTolerance)
        {
            Error(
                "SW-BRD-010",
                "board",
                "Imported board volume " + volume.ToString("R", CultureInfo.InvariantCulture) +
                " mm^3 does not match per-body outline area x measured thickness " +
                expectedVolume.ToString("R", CultureInfo.InvariantCulture) + " mm^3.",
                diagnostics);
        }

        var mesh = BuildMesh(inputs.Select(item => item.Body).ToArray(), diagnostics, out var triangleCounts);
        var probes = new List<SolidWorksBoardProbe>();
        var nominalThickness = snapshot.Properties.Thickness.Mm ?? 0;
        if (mesh is not null)
        {
            var midZ = (minZ + maxZ) / 2.0;
            var outerContours = (snapshot.Outline?.Contours ?? Array.Empty<Contour>())
                .Where(contour => contour.Role == ContourRole.Outer)
                .ToArray();
            var holeContours = (snapshot.Outline?.Contours ?? Array.Empty<Contour>())
                .Where(contour => contour.Role == ContourRole.Hole)
                .ToArray();
            var outerPoints = outerContours
                .Select(contour => (contour.Id, Points: SampleContour(contour)))
                .ToArray();
            var holePoints = holeContours
                .Select(contour => (contour.Id, Points: SampleContour(contour)))
                .ToArray();

            probes.AddRange(ProbeGeometry(
                mesh,
                originX,
                originY,
                midZ,
                outerPoints,
                holePoints,
                diagnostics));
            ValidateProbes(
                probes,
                outerContours.Select(contour => contour.Id).ToArray(),
                holeContours.Select(contour => contour.Id).ToArray(),
                diagnostics);
        }
        else
        {
            Error("SW-BRD-016", "board", "Board tessellation is unavailable; cutouts cannot be probed.", diagnostics);
        }

        var measurements = inputs.Select((item, index) => new SolidWorksBodyMeasurement(
            index + 1,
            item.Box.MinX + originX,
            item.Box.MinY + originY,
            item.Box.MinZ,
            item.Box.MaxX + originX,
            item.Box.MaxY + originY,
            item.Box.MaxZ,
            bodyVolumes[index].ThicknessMm,
            item.Box.VolumeMm3,
            bodyVolumes[index].OutlineAreaMm2,
            bodyVolumes[index].ExpectedVolumeMm3,
            item.Box.VolumeMm3 - bodyVolumes[index].ExpectedVolumeMm3,
            triangleCounts[index])).ToArray();
        var geometry = new SolidWorksBoardGeometry(
            bodies.Length,
            minX,
            minY,
            minZ,
            maxX,
            maxY,
            maxZ,
            width,
            height,
            thickness,
            nominalThickness,
            thickness - nominalThickness,
            volume,
            expectedVolume,
            volumeResidual,
            expectedWidth,
            expectedHeight,
            xyResidual,
            zTopResidual,
            originX,
            originY,
            measurements,
            probes);
        return new SolidWorksBoardGeometryResult(geometry, diagnostics);
    }

    /// <summary>
    /// The frame origin is mandatory for board-3d-model inspection. A missing value is never
    /// replaced by zero: the caller must stop before applying any board offset.
    /// </summary>
    internal static bool TryReadFrameOrigin(
        BoardSnapshot snapshot,
        List<SolidWorksDiagnostic> diagnostics,
        out double originXMm,
        out double originYMm)
    {
        originXMm = 0;
        originYMm = 0;
        var required = BoardModelValidator.ValidateFrameOrigin(snapshot);
        foreach (var item in required)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                item.Code,
                item.Severity == PackageDiagnosticSeverity.Error
                    ? SolidWorksDiagnosticSeverity.Error
                    : SolidWorksDiagnosticSeverity.Warning,
                item.Path,
                item.Message));
        }

        if (required.Any(item => item.Severity == PackageDiagnosticSeverity.Error))
            return false;

        var properties = snapshot.Properties;
        if (!TryResolveOrigin(properties.OriginX, "board.properties.originX", out var xMm, diagnostics)
            || !TryResolveOrigin(properties.OriginY, "board.properties.originY", out var yMm, diagnostics))
        {
            return false;
        }

        originXMm = xMm;
        originYMm = yMm;
        return true;
    }

    private static bool TryResolveOrigin(
        LengthValue value,
        string path,
        out double mm,
        List<SolidWorksDiagnostic> diagnostics)
    {
        mm = 0;
        if (value.Iu is not { } iu
            || value.Mm is not { } candidate
            || !double.IsFinite(candidate)
            || Math.Abs(candidate - AltiumUnits.IuToMm(iu)) > UnitToleranceMm)
        {
            Error(
                PackageDiagnosticCodes.MissingBoardOrigin,
                path,
                "Board frame origin is missing or inconsistent with its IU value.",
                diagnostics);
            return false;
        }

        mm = candidate;
        return true;
    }

    /// <summary>
    /// Measured body boxes bound slices of the package outline. Their own Z extent
    /// supplies each slice thickness; no nominal thickness is imposed on a body.
    /// </summary>
    internal static IReadOnlyList<SolidWorksBodyVolume> EstimateBodyVolumes(
        IReadOnlyList<SolidWorksBodyBox> bodies,
        BoardOutline? outline,
        double originX,
        double originY,
        List<SolidWorksDiagnostic> diagnostics)
    {
        var outerContours = (outline?.Contours ?? Array.Empty<Contour>())
            .Where(contour => contour.Role == ContourRole.Outer)
            .ToArray();
        var holeContours = (outline?.Contours ?? Array.Empty<Contour>())
            .Where(contour => contour.Role == ContourRole.Hole)
            .ToArray();
        foreach (var contour in outerContours.Concat(holeContours))
        {
            if (IsContourClosed(contour))
                continue;

            Error(
                "SW-BRD-018",
                contour.Id,
                "The board outline contour is not closed; its area and cutout probes cannot be validated.",
                diagnostics);
        }

        var result = new List<SolidWorksBodyVolume>(bodies.Count);
        foreach (var body in bodies)
        {
            var minX = body.MinX + originX;
            var minY = body.MinY + originY;
            var maxX = body.MaxX + originX;
            var maxY = body.MaxY + originY;
            var area = 0.0;
            foreach (var contour in outerContours)
            {
                area += Math.Abs(BoardOutlineTopology.PolygonArea(
                    BoardOutlineTopology.ClipToBox(contour, minX, minY, maxX, maxY)));
            }

            foreach (var contour in holeContours)
            {
                area -= Math.Abs(BoardOutlineTopology.PolygonArea(
                    BoardOutlineTopology.ClipToBox(contour, minX, minY, maxX, maxY)));
            }

            area = Math.Max(0, area);
            var thickness = body.MaxZ - body.MinZ;
            result.Add(new SolidWorksBodyVolume(thickness, area, area * thickness));
        }

        return result;
    }

    internal static IReadOnlyList<SolidWorksBoardProbe> ProbeGeometry(
        TriangleMesh mesh,
        double minX,
        double minY,
        double midZ,
        IReadOnlyList<(string Id, IReadOnlyList<(double X, double Y)> Points)> outerPoints,
        IReadOnlyList<(string Id, IReadOnlyList<(double X, double Y)> Points)> holePoints,
        List<SolidWorksDiagnostic> diagnostics)
    {
        var probes = new List<SolidWorksBoardProbe>();
        foreach (var (id, points) in outerPoints)
        {
            Func<double, double, bool> isSolidAtProbeHeight = (candidateX, candidateY) =>
                mesh.Contains(candidateX - minX, candidateY - minY, midZ);
            if (!TryFindSolidPoint(
                    new[] { points },
                    holePoints.Select(item => item.Points).ToArray(),
                    out var solidX,
                    out var solidY,
                    isSolidAtProbeHeight))
            {
                Error("SW-BRD-011", id, "No interior board probe point could be found at the probe height.", diagnostics);
                continue;
            }

            var localX = solidX - minX;
            var localY = solidY - minY;
            probes.Add(new SolidWorksBoardProbe(
                "board",
                id,
                localX,
                localY,
                midZ,
                true,
                mesh.Contains(localX, localY, midZ)));
        }

        foreach (var (id, points) in holePoints)
        {
            if (BuildCutoutProbe(id, points, mesh, minX, minY, midZ, diagnostics) is { } probe)
                probes.Add(probe);
        }

        return probes;
    }

    internal static SolidWorksBoardProbe? BuildCutoutProbe(
        string contourId,
        IReadOnlyList<(double X, double Y)> points,
        TriangleMesh mesh,
        double minX,
        double minY,
        double midZ,
        List<SolidWorksDiagnostic> diagnostics)
    {
        if (!TryInteriorPoint(points, out var holeX, out var holeY))
        {
            Error(
                "SW-BRD-012",
                contourId,
                "Cutout interior point could not be derived; the cutout was not probed.",
                diagnostics);
            return null;
        }

        var localX = holeX - minX;
        var localY = holeY - minY;
        return new SolidWorksBoardProbe(
            "cutout",
            contourId,
            localX,
            localY,
            midZ,
            false,
            mesh.Contains(localX, localY, midZ));
    }

    internal static void ValidateProbes(
        IReadOnlyList<SolidWorksBoardProbe> probes,
        IReadOnlyList<string> outerContourIds,
        IReadOnlyList<string> holeContourIds,
        List<SolidWorksDiagnostic> diagnostics)
    {
        var materialIds = probes.Where(probe => probe.Kind == "board")
            .Select(probe => probe.ContourId)
            .ToArray();
        if (materialIds.Length != outerContourIds.Count
            || !materialIds.ToHashSet(StringComparer.Ordinal).SetEquals(outerContourIds))
        {
            Error(
                "SW-BRD-019",
                "board",
                "Verified " + materialIds.Length.ToString(CultureInfo.InvariantCulture) +
                " material probes; expected one per " + outerContourIds.Count.ToString(CultureInfo.InvariantCulture) +
                " Outer contours.",
                diagnostics);
        }

        foreach (var probe in probes)
        {
            if (probe.InsideSolid == probe.ExpectedInsideSolid)
                continue;

            Error(
                "SW-BRD-013",
                probe.ContourId,
                probe.Kind + " probe insideSolid=" + probe.InsideSolid.ToString() +
                "; expected " + probe.ExpectedInsideSolid.ToString() + ".",
                diagnostics);
        }

        if (!probes.Any(probe => probe.Kind == "board" && probe.InsideSolid))
        {
            Error("SW-BRD-014", "board", "The solid board probe did not confirm material.", diagnostics);
        }

        var cutoutIds = probes.Where(probe => probe.Kind == "cutout")
            .Select(probe => probe.ContourId)
            .ToArray();
        if (cutoutIds.Length != holeContourIds.Count
            || !cutoutIds.ToHashSet(StringComparer.Ordinal).SetEquals(holeContourIds))
        {
            Error(
                "SW-BRD-015",
                "board",
                "Verified " + cutoutIds.Length.ToString(CultureInfo.InvariantCulture) +
                " cutout probes; expected " + holeContourIds.Count.ToString(CultureInfo.InvariantCulture) +
                " Hole contours.",
                diagnostics);
        }
    }

    private static TriangleMesh? BuildMesh(
        IReadOnlyList<Body2> bodies,
        List<SolidWorksDiagnostic> diagnostics,
        out int[] triangleCounts)
    {
        var mesh = new TriangleMesh();
        triangleCounts = new int[bodies.Count];
        var failed = false;
        for (var i = 0; i < bodies.Count; i++)
        {
            var path = "board.body[" + i + "]";
            ITessellation? tessellation;
            try
            {
                tessellation = bodies[i].GetTessellation(null) as ITessellation;
            }
            catch (Exception ex)
            {
                Error("SW-BRD-017", path, "IBody2.GetTessellation failed: " + ex.GetType().Name + ".", diagnostics);
                failed = true;
                continue;
            }

            if (tessellation is null)
            {
                Error("SW-BRD-022", path, "IBody2.GetTessellation returned null.", diagnostics);
                failed = true;
                continue;
            }

            tessellation.ImprovedQuality = true;
            tessellation.SurfacePlaneTolerance = 0.01;
            tessellation.CurveChordTolerance = 0.01;
            tessellation.NeedFaceFacetMap = false;
            tessellation.NeedEdgeFinMap = false;
            tessellation.NeedVertexParams = false;
            tessellation.NeedVertexNormal = false;
            if (!tessellation.Tessellate())
            {
                Error("SW-BRD-023", path, "ITessellation.Tessellate returned false.", diagnostics);
                failed = true;
                continue;
            }

            var bodyMesh = new TriangleMesh();
            var vertexCount = tessellation.GetVertexCount();
            var finCount = tessellation.GetFinCount();
            var facetCount = tessellation.GetFacetCount();
            if (!TryAddBodyMesh(
                    i,
                    vertexCount,
                    index => tessellation.GetVertexPoint(index) as double[],
                    finCount,
                    facetCount,
                    tessellation.GetFacetFins,
                    tessellation.GetFinVertices,
                    bodyMesh,
                    diagnostics))
            {
                failed = true;
                continue;
            }

            triangleCounts[i] = bodyMesh.TriangleCount;
            mesh.Append(bodyMesh);
        }

        if (failed || mesh.TriangleCount == 0)
        {
            triangleCounts = new int[bodies.Count];
            return null;
        }

        return mesh;
    }

    /// <summary>
    /// Strict mesh ingestion: one bad vertex, fin, facet or index rejects the whole body.
    /// Callers never receive a partial mesh.
    /// </summary>
    internal static bool TryAddBodyMesh(
        int bodyIndex,
        int vertexCount,
        Func<int, double[]?> vertexPoint,
        int finCount,
        int facetCount,
        Func<int, object?> facetFins,
        Func<int, object?> finVertices,
        TriangleMesh mesh,
        List<SolidWorksDiagnostic> diagnostics)
    {
        var path = "board.body[" + bodyIndex + "]";
        if (vertexCount <= 0)
        {
            Error("SW-BRD-024", path, "Tessellation has no vertices.", diagnostics);
            return false;
        }

        var vertexMap = new Dictionary<int, int>();
        for (var i = 0; i < vertexCount; i++)
        {
            var point = vertexPoint(i);
            if (point is not { Length: >= 3 }
                || !double.IsFinite(point[0]) || !double.IsFinite(point[1]) || !double.IsFinite(point[2]))
            {
                Error("SW-BRD-024", path, "Tessellation vertex " + i + " is missing or invalid.", diagnostics);
                return false;
            }

            vertexMap[i] = mesh.AddVertex(
                point[0] * MetresToMm,
                point[1] * MetresToMm,
                point[2] * MetresToMm);
        }

        if (finCount <= 0)
        {
            Error("SW-BRD-025", path, "Tessellation has no fins.", diagnostics);
            return false;
        }

        if (facetCount <= 0)
        {
            Error("SW-BRD-025", path, "Tessellation has no facets.", diagnostics);
            return false;
        }

        for (var facet = 0; facet < facetCount; facet++)
        {
            var fins = AsIntArray(facetFins(facet));
            if (fins is null || fins.Length != 3)
            {
                Error("SW-BRD-025", path, "Tessellation facet " + facet + " does not have exactly three fins.", diagnostics);
                return false;
            }

            var vertices = new List<int>(3);
            foreach (var fin in fins)
            {
                if (fin < 0 || fin >= finCount)
                {
                    Error("SW-BRD-026", path, "Tessellation facet " + facet + " references unknown fin " + fin + ".", diagnostics);
                    return false;
                }

                var points = AsIntArray(finVertices(fin));
                if (points is null || points.Length != 2)
                {
                    Error("SW-BRD-025", path, "Tessellation fin " + fin + " does not have exactly two vertices.", diagnostics);
                    return false;
                }

                foreach (var vertex in points)
                {
                    if (vertex < 0 || vertex >= vertexCount || !vertexMap.TryGetValue(vertex, out var mapped))
                    {
                        Error("SW-BRD-026", path, "Tessellation fin " + fin + " references unknown vertex " + vertex + ".", diagnostics);
                        return false;
                    }

                    if (!vertices.Contains(mapped))
                        vertices.Add(mapped);
                }
            }

            if (vertices.Count != 3)
            {
                Error("SW-BRD-026", path, "Tessellation facet " + facet + " does not resolve to three vertices.", diagnostics);
                return false;
            }

            mesh.AddTriangle(vertices[0], vertices[1], vertices[2]);
        }

        return mesh.TriangleCount > 0;
    }

    private static int[]? AsIntArray(object? value) => value switch
    {
        null => null,
        int single => new[] { single },
        int[] array => array,
        IEnumerable<int> sequence => sequence.ToArray(),
        _ => null,
    };

    private static bool TryFindSolidPoint(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> outerContours,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> holeContours,
        out double x,
        out double y,
        Func<double, double, bool>? accepts = null)
    {
        x = 0;
        y = 0;
        var outer = outerContours.FirstOrDefault();
        if (outer is null || outer.Count < 3)
            return false;

        var minX = outer.Min(point => point.X);
        var maxX = outer.Max(point => point.X);
        var minY = outer.Min(point => point.Y);
        var maxY = outer.Max(point => point.Y);
        for (var ix = 1; ix <= 9; ix++)
        {
            for (var iy = 1; iy <= 9; iy++)
            {
                var candidateX = minX + (maxX - minX) * ix / 10.0;
                var candidateY = minY + (maxY - minY) * iy / 10.0;
                if (!PointInPolygon(outer, candidateX, candidateY))
                    continue;
                if (holeContours.Any(hole => PointInPolygon(hole, candidateX, candidateY)))
                    continue;
                if (accepts is not null && !accepts(candidateX, candidateY))
                    continue;

                x = candidateX;
                y = candidateY;
                return true;
            }
        }

        return false;
    }

    private static bool TryInteriorPoint(
        IReadOnlyList<(double X, double Y)> contour,
        out double x,
        out double y)
    {
        x = 0;
        y = 0;
        if (contour.Count < 3)
            return false;

        var centroid = PolygonCentroid(contour);
        if (PointInPolygon(contour, centroid.X, centroid.Y))
        {
            x = centroid.X;
            y = centroid.Y;
            return true;
        }

        var minX = contour.Min(point => point.X);
        var maxX = contour.Max(point => point.X);
        var minY = contour.Min(point => point.Y);
        var maxY = contour.Max(point => point.Y);
        for (var iy = 1; iy <= 9; iy++)
        {
            var scanY = minY + (maxY - minY) * iy / 10.0;
            var intersections = new List<double>();
            for (var i = 0; i < contour.Count; i++)
            {
                var a = contour[i];
                var b = contour[(i + 1) % contour.Count];
                if ((a.Y > scanY) == (b.Y > scanY))
                    continue;
                intersections.Add(a.X + (scanY - a.Y) * (b.X - a.X) / (b.Y - a.Y));
            }

            intersections.Sort();
            for (var i = 0; i + 1 < intersections.Count; i += 2)
            {
                var candidateX = (intersections[i] + intersections[i + 1]) / 2.0;
                if (!PointInPolygon(contour, candidateX, scanY))
                    continue;
                x = candidateX;
                y = scanY;
                return true;
            }
        }

        return false;
    }

    private static bool IsContourClosed(Contour contour)
    {
        if (contour.Segments.Count == 0)
            return false;
        var first = contour.Segments[0].Start;
        var last = contour.Segments[^1].End;
        if (first.XMm is not { } startX || first.YMm is not { } startY
            || last.XMm is not { } endX || last.YMm is not { } endY)
        {
            return false;
        }

        return Math.Abs(startX - endX) <= 0.01 && Math.Abs(startY - endY) <= 0.01;
    }

    private static IReadOnlyList<(double X, double Y)> SampleContour(Contour contour)
    {
        var points = new List<(double X, double Y)>();
        foreach (var segment in contour.Segments)
        {
            if (segment.Start.XMm is not { } startX || segment.Start.YMm is not { } startY)
                continue;

            if (segment is ArcSegment arc
                && arc.Center.XMm is { } centerX
                && arc.Center.YMm is { } centerY
                && arc.Radius.Mm is { } radius
                && arc.SweepDirection is { } direction)
            {
                var startAngle = Math.Atan2(startY - centerY, startX - centerX);
                var endX = arc.End.XMm ?? startX;
                var endY = arc.End.YMm ?? startY;
                var endAngle = Math.Atan2(endY - centerY, endX - centerX);
                var counterClockwise = direction == ArcSweepDirection.CounterClockwise;
                var sweep = counterClockwise
                    ? Normalize(endAngle - startAngle)
                    : Normalize(startAngle - endAngle);
                for (var i = 0; i < ArcSamples; i++)
                {
                    var angle = counterClockwise
                        ? startAngle + sweep * i / ArcSamples
                        : startAngle - sweep * i / ArcSamples;
                    points.Add((centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle)));
                }

                continue;
            }

            points.Add((startX, startY));
        }

        return points;
    }

    private static double PolygonArea(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 3)
            return 0;
        var sum = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum / 2.0;
    }

    private static (double X, double Y) PolygonCentroid(IReadOnlyList<(double X, double Y)> points)
    {
        var area = PolygonArea(points);
        if (Math.Abs(area) < 1e-9)
            return (points.Average(point => point.X), points.Average(point => point.Y));

        var cx = 0.0;
        var cy = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            var cross = a.X * b.Y - b.X * a.Y;
            cx += (a.X + b.X) * cross;
            cy += (a.Y + b.Y) * cross;
        }

        return (cx / (6.0 * area), cy / (6.0 * area));
    }

    private static bool PointInPolygon(
        IReadOnlyList<(double X, double Y)> polygon,
        double x,
        double y)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            if ((a.Y > y) != (b.Y > y))
            {
                var intersectionX = a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                if (x < intersectionX)
                    inside = !inside;
            }
        }

        return inside;
    }

    private static double Normalize(double angle)
    {
        var value = angle % (2.0 * Math.PI);
        return value < 0 ? value + 2.0 * Math.PI : value;
    }

    private static void Error(
        string code,
        string path,
        string message,
        List<SolidWorksDiagnostic> diagnostics) =>
        diagnostics.Add(new SolidWorksDiagnostic(code, SolidWorksDiagnosticSeverity.Error, path, message));
}
