namespace EcadMcadBridge.SolidWorks.Importing;

/// <summary>
/// Closed triangle mesh used for numerically reproducible point-in-solid checks.
/// No CAD tool is required for <see cref="Contains"/>; the tessellation is supplied by the caller.
/// </summary>
internal sealed class TriangleMesh
{
    private const double RayEpsilon = 1e-9;
    private const double HitMergeTolerance = 1e-6;

    private readonly List<double[]> _vertices = new();
    private readonly List<(int A, int B, int C)> _triangles = new();

    public int VertexCount => _vertices.Count;

    public int TriangleCount => _triangles.Count;

    public int AddVertex(double x, double y, double z)
    {
        _vertices.Add(new[] { x, y, z });
        return _vertices.Count - 1;
    }

    public void AddTriangle(int a, int b, int c)
    {
        if (a == b || b == c || a == c)
            return;
        _triangles.Add((a, b, c));
    }

    public void Append(TriangleMesh other)
    {
        var offset = _vertices.Count;
        foreach (var vertex in other._vertices)
            _vertices.Add(new[] { vertex[0], vertex[1], vertex[2] });
        foreach (var triangle in other._triangles)
            AddTriangle(triangle.A + offset, triangle.B + offset, triangle.C + offset);
    }

    /// <summary>
    /// Odd/even ray parity against the closed mesh. The direction is deliberately skewed from
    /// all coordinate axes to avoid passing exactly through shared triangle edges.
    /// </summary>
    public bool Contains(double x, double y, double z)
    {
        if (_triangles.Count == 0)
            return false;

        var dirX = 0.0131;
        var dirY = 0.0173;
        var dirZ = 1.0;
        var length = Math.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
        dirX /= length;
        dirY /= length;
        dirZ /= length;

        var hits = new List<double>();
        foreach (var triangle in _triangles)
        {
            var v0 = _vertices[triangle.A];
            var v1 = _vertices[triangle.B];
            var v2 = _vertices[triangle.C];

            var edge1X = v1[0] - v0[0];
            var edge1Y = v1[1] - v0[1];
            var edge1Z = v1[2] - v0[2];
            var edge2X = v2[0] - v0[0];
            var edge2Y = v2[1] - v0[1];
            var edge2Z = v2[2] - v0[2];

            var hX = dirY * edge2Z - dirZ * edge2Y;
            var hY = dirZ * edge2X - dirX * edge2Z;
            var hZ = dirX * edge2Y - dirY * edge2X;
            var a = edge1X * hX + edge1Y * hY + edge1Z * hZ;
            if (Math.Abs(a) < 1e-14)
                continue;

            var f = 1.0 / a;
            var sX = x - v0[0];
            var sY = y - v0[1];
            var sZ = z - v0[2];
            var u = f * (sX * hX + sY * hY + sZ * hZ);
            if (u < 0 || u > 1)
                continue;

            var qX = sY * edge1Z - sZ * edge1Y;
            var qY = sZ * edge1X - sX * edge1Z;
            var qZ = sX * edge1Y - sY * edge1X;
            var v = f * (dirX * qX + dirY * qY + dirZ * qZ);
            if (v < 0 || u + v > 1)
                continue;

            var t = f * (edge2X * qX + edge2Y * qY + edge2Z * qZ);
            if (t > RayEpsilon)
                hits.Add(t);
        }

        if (hits.Count == 0)
            return false;

        hits.Sort();
        var unique = 1;
        for (var i = 1; i < hits.Count; i++)
        {
            if (Math.Abs(hits[i] - hits[i - 1]) > HitMergeTolerance)
                unique++;
        }

        return unique % 2 == 1;
    }
}
