using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.SolidWorks.Importing;
using Xunit;

namespace EcadMcadBridge.SolidWorks.Tests;

public sealed class SolidWorksBoardGeometryInspectorTests
{
    private static readonly (double X0, double Y0, double X1, double Y1) FirstHole = (20, 20, 30, 30);
    private static readonly (double X0, double Y0, double X1, double Y1) SecondHole = (60, 20, 70, 30);

    [Fact]
    public void OneHoleContour_ProbesMaterialAndCutout()
    {
        var mesh = BuildBoard(FirstHole);
        var diagnostics = new List<SolidWorksDiagnostic>();
        var probes = SolidWorksBoardGeometryInspector.ProbeGeometry(
            mesh,
            0,
            0,
            1,
            new[] { ("o1", OuterPoints()) },
            new[] { ("h1", RectanglePoints(FirstHole)) },
            diagnostics);

        SolidWorksBoardGeometryInspector.ValidateProbes(probes, new[] { "o1" }, new[] { "h1" }, diagnostics);

        Assert.Empty(Errors(diagnostics));
        Assert.Equal(2, probes.Count);
        Assert.True(probes.Single(probe => probe.Kind == "board").InsideSolid);
        Assert.False(probes.Single(probe => probe.Kind == "cutout").InsideSolid);
    }

    [Fact]
    public void TwoHoleContours_BothProbesPass()
    {
        var mesh = BuildBoard(FirstHole, SecondHole);
        var diagnostics = new List<SolidWorksDiagnostic>();
        var probes = SolidWorksBoardGeometryInspector.ProbeGeometry(
            mesh,
            0,
            0,
            1,
            new[] { ("o1", OuterPoints()) },
            HolePoints(FirstHole, SecondHole),
            diagnostics);

        SolidWorksBoardGeometryInspector.ValidateProbes(probes, new[] { "o1" }, new[] { "h1", "h2" }, diagnostics);

        Assert.Empty(Errors(diagnostics));
        Assert.Equal(new[] { "h1", "h2" }, probes.Where(probe => probe.Kind == "cutout")
            .Select(probe => probe.ContourId).OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public void MaterialProbe_SearchesAtProbeHeight()
    {
        var mesh = BuildBoard(FirstHole);
        var diagnostics = new List<SolidWorksDiagnostic>();
        var probes = SolidWorksBoardGeometryInspector.ProbeGeometry(
            mesh,
            0,
            0,
            3,
            new[] { ("o1", OuterPoints()) },
            new[] { ("h1", RectanglePoints(FirstHole)) },
            diagnostics);

        Assert.DoesNotContain(probes, probe => probe.Kind == "board");
        Assert.Contains(diagnostics, item => item.Code == "SW-BRD-011"
            && item.Severity == SolidWorksDiagnosticSeverity.Error);
    }

    [Fact]
    public void TwoHoles_OneInteriorPointUnavailable_IsInvalid()
    {
        var mesh = BuildBoard(FirstHole, SecondHole);
        var diagnostics = new List<SolidWorksDiagnostic>();
        var holes = HolePoints(FirstHole, SecondHole);
        holes[1] = ("h2", Array.Empty<(double X, double Y)>());
        var probes = SolidWorksBoardGeometryInspector.ProbeGeometry(
            mesh,
            0,
            0,
            1,
            new[] { ("o1", OuterPoints()) },
            holes,
            diagnostics);

        SolidWorksBoardGeometryInspector.ValidateProbes(probes, new[] { "o1" }, new[] { "h1", "h2" }, diagnostics);

        Assert.Contains(diagnostics, item => item.Code == "SW-BRD-012"
            && item.Severity == SolidWorksDiagnosticSeverity.Error
            && item.Path == "h2");
        Assert.Contains(diagnostics, item => item.Code == "SW-BRD-015"
            && item.Severity == SolidWorksDiagnosticSeverity.Error);
    }

    [Fact]
    public void TwoHoles_OneProbeFindsMaterial_IsInvalid()
    {
        var mesh = BuildBoard(FirstHole);
        var diagnostics = new List<SolidWorksDiagnostic>();
        var probes = SolidWorksBoardGeometryInspector.ProbeGeometry(
            mesh,
            0,
            0,
            1,
            new[] { ("o1", OuterPoints()) },
            HolePoints(FirstHole, SecondHole),
            diagnostics);

        SolidWorksBoardGeometryInspector.ValidateProbes(probes, new[] { "o1" }, new[] { "h1", "h2" }, diagnostics);

        Assert.Contains(diagnostics, item => item.Code == "SW-BRD-013"
            && item.Severity == SolidWorksDiagnosticSeverity.Error
            && item.Path == "h2");
        Assert.DoesNotContain(diagnostics, item => item.Code == "SW-BRD-015");
    }

    [Fact]
    public void BothCutoutsEmptyAndMaterialPositive_IsValid()
    {
        var diagnostics = new List<SolidWorksDiagnostic>();
        var probes = new[]
        {
            new SolidWorksBoardProbe("board", "o1", 5, 5, 1, true, true),
            new SolidWorksBoardProbe("cutout", "h1", 25, 25, 1, false, false),
            new SolidWorksBoardProbe("cutout", "h2", 65, 25, 1, false, false),
        };

        SolidWorksBoardGeometryInspector.ValidateProbes(probes, new[] { "o1" }, new[] { "h1", "h2" }, diagnostics);

        Assert.Empty(Errors(diagnostics));
    }

    [Fact]
    public void MissingMaterialProbe_IsInvalid()
    {
        var diagnostics = new List<SolidWorksDiagnostic>();
        var probes = new[]
        {
            new SolidWorksBoardProbe("cutout", "h1", 25, 25, 1, false, false),
        };

        SolidWorksBoardGeometryInspector.ValidateProbes(probes, new[] { "o1" }, new[] { "h1" }, diagnostics);

        Assert.Contains(diagnostics, item => item.Code == "SW-BRD-014"
            && item.Severity == SolidWorksDiagnosticSeverity.Error);
    }

    [Fact]
    public void VariableThicknessBodies_UseTheirOwnMeasuredZExtent()
    {
        var outline = new BoardOutline
        {
            Id = "outline",
            Completeness = OutlineCompleteness.Complete,
            Contours = new[]
            {
                RectangleContour("outer", 0, 0, 100, 50, ContourRole.Outer),
                RectangleContour("hole", 10, 0, 40, 10, ContourRole.Hole),
            },
        };
        var diagnostics = new List<SolidWorksDiagnostic>();
        var bodies = new[]
        {
            new SolidWorksBodyBox(0, 0, 0, 100, 10, 0.1, 7),
            new SolidWorksBodyBox(0, 10, -0.1, 100, 50, 0.9, 4000),
        };

        var volumes = SolidWorksBoardGeometryInspector.EstimateBodyVolumes(
            bodies,
            outline,
            0,
            0,
            diagnostics);

        Assert.Empty(Errors(diagnostics));
        Assert.Equal(0.1, volumes[0].ThicknessMm, 10);
        Assert.Equal(700, volumes[0].OutlineAreaMm2, 3);
        Assert.Equal(70, volumes[0].ExpectedVolumeMm3, 3);
        Assert.Equal(1.0, volumes[1].ThicknessMm, 3);
        Assert.Equal(4000, volumes[1].ExpectedVolumeMm3, 3);
    }

    [Fact]
    public void MultipleOuterIslands_RequireOneMaterialProbePerContour()
    {
        var mesh = new TriangleMesh();
        mesh.Append(BoxMesh(0, 0, 0, 20, 10, 2));
        mesh.Append(BoxMesh(40, 0, 0, 60, 10, 2));
        var diagnostics = new List<SolidWorksDiagnostic>();
        var outers = new[]
        {
            ("o1", RectanglePoints((0, 0, 20, 10))),
            ("o2", RectanglePoints((40, 0, 60, 10))),
        };

        var probes = SolidWorksBoardGeometryInspector.ProbeGeometry(
            mesh,
            0,
            0,
            1,
            outers,
            Array.Empty<(string Id, IReadOnlyList<(double X, double Y)> Points)>(),
            diagnostics);
        SolidWorksBoardGeometryInspector.ValidateProbes(
            probes,
            new[] { "o1", "o2" },
            Array.Empty<string>(),
            diagnostics);

        Assert.Empty(Errors(diagnostics));
        Assert.Equal(new[] { "o1", "o2" }, probes.Select(probe => probe.ContourId));
        Assert.All(probes, probe => Assert.True(probe.InsideSolid));
    }

    [Fact]
    public void MissingOuterProbeId_IsAnError()
    {
        var diagnostics = new List<SolidWorksDiagnostic>();
        var probes = new[]
        {
            new SolidWorksBoardProbe("board", "o1", 5, 5, 1, true, true),
        };

        SolidWorksBoardGeometryInspector.ValidateProbes(
            probes,
            new[] { "o1", "o2" },
            Array.Empty<string>(),
            diagnostics);

        Assert.Contains(diagnostics, item => item.Code == "SW-BRD-019"
            && item.Severity == SolidWorksDiagnosticSeverity.Error);
    }

    [Fact]
    public void Tessellation_ValidTriangleIsAccepted()
    {
        var mesh = new TriangleMesh();
        var diagnostics = new List<SolidWorksDiagnostic>();
        var accepted = SolidWorksBoardGeometryInspector.TryAddBodyMesh(
            0,
            3,
            index => new[] { index * 0.01, 0.0, 0.0 },
            3,
            1,
            _ => new[] { 0, 1, 2 },
            fin => fin switch
            {
                0 => new[] { 0, 1 },
                1 => new[] { 1, 2 },
                _ => new[] { 2, 0 },
            },
            mesh,
            diagnostics);

        Assert.True(accepted);
        Assert.Equal(1, mesh.TriangleCount);
        Assert.Empty(Errors(diagnostics));
    }

    [Fact]
    public void Tessellation_FinIndicesAreValidatedAgainstFinCount()
    {
        var mesh = new TriangleMesh();
        var diagnostics = new List<SolidWorksDiagnostic>();
        var accepted = SolidWorksBoardGeometryInspector.TryAddBodyMesh(
            0,
            3,
            index => new[] { index * 0.01, 0.0, 0.0 },
            4,
            1,
            _ => new[] { 0, 3, 2 },
            fin => fin switch
            {
                0 => new[] { 0, 1 },
                3 => new[] { 1, 2 },
                _ => new[] { 2, 0 },
            },
            mesh,
            diagnostics);

        Assert.True(accepted);
        Assert.Equal(1, mesh.TriangleCount);
        Assert.Empty(Errors(diagnostics));
    }

    [Theory]
    [InlineData("vertex")]
    [InlineData("fin")]
    [InlineData("facet")]
    public void Tessellation_PartialMeshIsRejected(string defect)
    {
        var mesh = new TriangleMesh();
        var diagnostics = new List<SolidWorksDiagnostic>();
        var accepted = SolidWorksBoardGeometryInspector.TryAddBodyMesh(
            0,
            3,
            index => defect == "vertex" && index == 1 ? null : new[] { index * 0.01, 0.0, 0.0 },
            3,
            defect == "facet" ? 0 : 1,
            _ => defect == "fin" ? new[] { 0, 99, 2 } : new[] { 0, 1, 2 },
            fin => fin switch
            {
                0 => new[] { 0, 1 },
                1 => new[] { 1, 2 },
                _ => new[] { 2, 0 },
            },
            mesh,
            diagnostics);

        Assert.False(accepted);
        Assert.Equal(0, mesh.TriangleCount);
        Assert.Contains(diagnostics, item => item.Severity == SolidWorksDiagnosticSeverity.Error);
    }

    private static IReadOnlyList<SolidWorksDiagnostic> Errors(IEnumerable<SolidWorksDiagnostic> diagnostics) =>
        diagnostics.Where(item => item.Severity == SolidWorksDiagnosticSeverity.Error).ToArray();

    private static Contour RectangleContour(
        string id,
        double x0,
        double y0,
        double x1,
        double y1,
        ContourRole role)
    {
        var p0 = Point2D.FromIu(AltiumUnits.MmToIu(x0), AltiumUnits.MmToIu(y0));
        var p1 = Point2D.FromIu(AltiumUnits.MmToIu(x1), AltiumUnits.MmToIu(y0));
        var p2 = Point2D.FromIu(AltiumUnits.MmToIu(x1), AltiumUnits.MmToIu(y1));
        var p3 = Point2D.FromIu(AltiumUnits.MmToIu(x0), AltiumUnits.MmToIu(y1));
        return new Contour
        {
            Id = id,
            Role = role,
            Segments = new ContourSegment[]
            {
                new LineSegment { Start = p0, End = p1 },
                new LineSegment { Start = p1, End = p2 },
                new LineSegment { Start = p2, End = p3 },
                new LineSegment { Start = p3, End = p0 },
            },
        };
    }

    private static TriangleMesh BoxMesh(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var mesh = new TriangleMesh();
        AddQuad(mesh, (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1));
        AddQuad(mesh, (x0, y1, z0), (x1, y1, z0), (x1, y0, z0), (x0, y0, z0));
        AddQuad(mesh, (x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1));
        AddQuad(mesh, (x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1));
        AddQuad(mesh, (x1, y1, z0), (x0, y1, z0), (x0, y1, z1), (x1, y1, z1));
        AddQuad(mesh, (x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1));
        return mesh;
    }

    private static (string Id, IReadOnlyList<(double X, double Y)> Points)[] HolePoints(
        params (double X0, double Y0, double X1, double Y1)[] holes)
    {
        var ids = new[] { "h1", "h2" };
        return holes.Select((hole, index) =>
            (ids[index], RectanglePoints(hole))).ToArray();
    }

    private static IReadOnlyList<(double X, double Y)> OuterPoints() => RectanglePoints((0, 0, 100, 60));

    private static IReadOnlyList<(double X, double Y)> RectanglePoints(
        (double X0, double Y0, double X1, double Y1) rect) => new[]
    {
        (rect.X0, rect.Y0),
        (rect.X1, rect.Y0),
        (rect.X1, rect.Y1),
        (rect.X0, rect.Y1),
    };

    private static TriangleMesh BuildBoard(
        params (double X0, double Y0, double X1, double Y1)[] holes)
    {
        const double z0 = 0;
        const double z1 = 2;
        var mesh = new TriangleMesh();
        var xs = new SortedSet<double> { 0, 100 };
        var ys = new SortedSet<double> { 0, 60 };
        foreach (var hole in holes)
        {
            xs.Add(hole.X0);
            xs.Add(hole.X1);
            ys.Add(hole.Y0);
            ys.Add(hole.Y1);
        }

        var xList = xs.ToArray();
        var yList = ys.ToArray();
        for (var ix = 0; ix + 1 < xList.Length; ix++)
        {
            for (var iy = 0; iy + 1 < yList.Length; iy++)
            {
                var x0 = xList[ix];
                var x1 = xList[ix + 1];
                var y0 = yList[iy];
                var y1 = yList[iy + 1];
                var cx = (x0 + x1) / 2.0;
                var cy = (y0 + y1) / 2.0;
                if (holes.Any(hole => cx > hole.X0 && cx < hole.X1 && cy > hole.Y0 && cy < hole.Y1))
                    continue;

                AddQuad(mesh, (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1));
                AddQuad(mesh, (x0, y1, z0), (x1, y1, z0), (x1, y0, z0), (x0, y0, z0));
            }
        }

        AddQuad(mesh, (0, 0, z0), (100, 0, z0), (100, 0, z1), (0, 0, z1));
        AddQuad(mesh, (100, 0, z0), (100, 60, z0), (100, 60, z1), (100, 0, z1));
        AddQuad(mesh, (100, 60, z0), (0, 60, z0), (0, 60, z1), (100, 60, z1));
        AddQuad(mesh, (0, 60, z0), (0, 0, z0), (0, 0, z1), (0, 60, z1));

        foreach (var hole in holes)
        {
            AddQuad(mesh, (hole.X0, hole.Y0, z0), (hole.X1, hole.Y0, z0),
                (hole.X1, hole.Y0, z1), (hole.X0, hole.Y0, z1));
            AddQuad(mesh, (hole.X1, hole.Y0, z0), (hole.X1, hole.Y1, z0),
                (hole.X1, hole.Y1, z1), (hole.X1, hole.Y0, z1));
            AddQuad(mesh, (hole.X1, hole.Y1, z0), (hole.X0, hole.Y1, z0),
                (hole.X0, hole.Y1, z1), (hole.X1, hole.Y1, z1));
            AddQuad(mesh, (hole.X0, hole.Y1, z0), (hole.X0, hole.Y0, z0),
                (hole.X0, hole.Y0, z1), (hole.X0, hole.Y1, z1));
        }

        return mesh;
    }

    private static void AddQuad(
        TriangleMesh mesh,
        (double X, double Y, double Z) a,
        (double X, double Y, double Z) b,
        (double X, double Y, double Z) c,
        (double X, double Y, double Z) d)
    {
        var ia = mesh.AddVertex(a.X, a.Y, a.Z);
        var ib = mesh.AddVertex(b.X, b.Y, b.Z);
        var ic = mesh.AddVertex(c.X, c.Y, c.Z);
        var id = mesh.AddVertex(d.X, d.Y, d.Z);
        mesh.AddTriangle(ia, ib, ic);
        mesh.AddTriangle(ia, ic, id);
    }
}
