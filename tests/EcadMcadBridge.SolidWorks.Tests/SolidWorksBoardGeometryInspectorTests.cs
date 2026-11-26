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
            new[] { OuterPoints() },
            new[] { ("h1", RectanglePoints(FirstHole)) },
            diagnostics);

        SolidWorksBoardGeometryInspector.ValidateProbes(probes, new[] { "h1" }, diagnostics);

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
            new[] { OuterPoints() },
            HolePoints(FirstHole, SecondHole),
            diagnostics);

        SolidWorksBoardGeometryInspector.ValidateProbes(probes, new[] { "h1", "h2" }, diagnostics);

        Assert.Empty(Errors(diagnostics));
        Assert.Equal(new[] { "h1", "h2" }, probes.Where(probe => probe.Kind == "cutout")
            .Select(probe => probe.ContourId).OrderBy(value => value, StringComparer.Ordinal));
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
            new[] { OuterPoints() },
            holes,
            diagnostics);

        SolidWorksBoardGeometryInspector.ValidateProbes(probes, new[] { "h1", "h2" }, diagnostics);

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
            new[] { OuterPoints() },
            HolePoints(FirstHole, SecondHole),
            diagnostics);

        SolidWorksBoardGeometryInspector.ValidateProbes(probes, new[] { "h1", "h2" }, diagnostics);

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
            new SolidWorksBoardProbe("board", "outer", 5, 5, 1, true, true),
            new SolidWorksBoardProbe("cutout", "h1", 25, 25, 1, false, false),
            new SolidWorksBoardProbe("cutout", "h2", 65, 25, 1, false, false),
        };

        SolidWorksBoardGeometryInspector.ValidateProbes(probes, new[] { "h1", "h2" }, diagnostics);

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

        SolidWorksBoardGeometryInspector.ValidateProbes(probes, new[] { "h1" }, diagnostics);

        Assert.Contains(diagnostics, item => item.Code == "SW-BRD-014"
            && item.Severity == SolidWorksDiagnosticSeverity.Error);
    }

    private static IReadOnlyList<SolidWorksDiagnostic> Errors(IEnumerable<SolidWorksDiagnostic> diagnostics) =>
        diagnostics.Where(item => item.Severity == SolidWorksDiagnosticSeverity.Error).ToArray();

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
