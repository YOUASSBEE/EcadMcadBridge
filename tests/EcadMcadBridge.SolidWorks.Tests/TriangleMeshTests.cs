using EcadMcadBridge.SolidWorks.Importing;
using Xunit;

namespace EcadMcadBridge.SolidWorks.Tests;

public sealed class TriangleMeshTests
{
    [Fact]
    public void Cube_ClassifiesInsideAndOutside()
    {
        var mesh = BuildBox(0, 0, 0, 10, 10, 10);

        Assert.True(mesh.Contains(5, 5, 5));
        Assert.False(mesh.Contains(15, 5, 5));
        Assert.False(mesh.Contains(5, 5, 11));
        Assert.False(mesh.Contains(-1, 5, 5));
    }

    [Fact]
    public void FrameWithThroughHole_ConfirmsMaterialAndEmptyCutout()
    {
        var mesh = BuildFrameWithHole();

        Assert.True(mesh.Contains(2, 2, 5));
        Assert.True(mesh.Contains(9, 9, 5));
        Assert.False(mesh.Contains(5, 5, 5));
        Assert.False(mesh.Contains(5, 2, 11));
    }

    private static TriangleMesh BuildBox(
        double x0,
        double y0,
        double z0,
        double x1,
        double y1,
        double z1)
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

    private static TriangleMesh BuildFrameWithHole()
    {
        var mesh = new TriangleMesh();
        const double o0 = 0, o1 = 10, h0 = 4, h1 = 6, z0 = 0, z1 = 10;

        // Top and bottom faces: four quads around the hole.
        AddQuad(mesh, (o0, o0, z1), (o1, o0, z1), (o1, h0, z1), (o0, h0, z1));
        AddQuad(mesh, (o0, h1, z1), (o1, h1, z1), (o1, o1, z1), (o0, o1, z1));
        AddQuad(mesh, (o0, h0, z1), (h0, h0, z1), (h0, h1, z1), (o0, h1, z1));
        AddQuad(mesh, (h1, h0, z1), (o1, h0, z1), (o1, h1, z1), (h1, h1, z1));

        AddQuad(mesh, (o0, h0, z0), (o1, h0, z0), (o1, o0, z0), (o0, o0, z0));
        AddQuad(mesh, (o0, o1, z0), (o1, o1, z0), (o1, h1, z0), (o0, h1, z0));
        AddQuad(mesh, (o0, h1, z0), (h0, h1, z0), (h0, h0, z0), (o0, h0, z0));
        AddQuad(mesh, (h1, h1, z0), (o1, h1, z0), (o1, h0, z0), (h1, h0, z0));

        // Outer and hole side walls.
        AddQuad(mesh, (o0, o0, z0), (o1, o0, z0), (o1, o0, z1), (o0, o0, z1));
        AddQuad(mesh, (o1, o0, z0), (o1, o1, z0), (o1, o1, z1), (o1, o0, z1));
        AddQuad(mesh, (o1, o1, z0), (o0, o1, z0), (o0, o1, z1), (o1, o1, z1));
        AddQuad(mesh, (o0, o1, z0), (o0, o0, z0), (o0, o0, z1), (o0, o1, z1));

        AddQuad(mesh, (h0, h0, z0), (h0, h1, z0), (h0, h1, z1), (h0, h0, z1));
        AddQuad(mesh, (h0, h1, z0), (h1, h1, z0), (h1, h1, z1), (h0, h1, z1));
        AddQuad(mesh, (h1, h1, z0), (h1, h0, z0), (h1, h0, z1), (h1, h1, z1));
        AddQuad(mesh, (h1, h0, z0), (h0, h0, z0), (h0, h0, z1), (h1, h0, z1));

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
