namespace EcadMcadBridge.Core.Placement;

/// <summary>
/// Immutable double-precision 4x4 matrix using column vectors, a translation in the last
/// column and left-to-right composition: <c>A * B</c> applies <c>B</c> first, then <c>A</c>.
/// </summary>
public readonly struct Matrix4x4D : IEquatable<Matrix4x4D>
{
    private readonly double _m11;
    private readonly double _m12;
    private readonly double _m13;
    private readonly double _m14;
    private readonly double _m21;
    private readonly double _m22;
    private readonly double _m23;
    private readonly double _m24;
    private readonly double _m31;
    private readonly double _m32;
    private readonly double _m33;
    private readonly double _m34;
    private readonly double _m41;
    private readonly double _m42;
    private readonly double _m43;
    private readonly double _m44;

    /// <summary>Creates a matrix from row-major components and rejects non-finite values.</summary>
    public Matrix4x4D(
        double m11, double m12, double m13, double m14,
        double m21, double m22, double m23, double m24,
        double m31, double m32, double m33, double m34,
        double m41, double m42, double m43, double m44)
    {
        if (!double.IsFinite(m11) || !double.IsFinite(m12) || !double.IsFinite(m13) || !double.IsFinite(m14) ||
            !double.IsFinite(m21) || !double.IsFinite(m22) || !double.IsFinite(m23) || !double.IsFinite(m24) ||
            !double.IsFinite(m31) || !double.IsFinite(m32) || !double.IsFinite(m33) || !double.IsFinite(m34) ||
            !double.IsFinite(m41) || !double.IsFinite(m42) || !double.IsFinite(m43) || !double.IsFinite(m44))
        {
            throw new ArgumentOutOfRangeException(nameof(m11), "Matrix components must be finite.");
        }

        _m11 = m11;
        _m12 = m12;
        _m13 = m13;
        _m14 = m14;
        _m21 = m21;
        _m22 = m22;
        _m23 = m23;
        _m24 = m24;
        _m31 = m31;
        _m32 = m32;
        _m33 = m33;
        _m34 = m34;
        _m41 = m41;
        _m42 = m42;
        _m43 = m43;
        _m44 = m44;
    }

    /// <summary>Returns the identity matrix.</summary>
    public static Matrix4x4D Identity =>
        new(1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);

    /// <summary>Creates a translation matrix in millimetres.</summary>
    public static Matrix4x4D CreateTranslation(double x, double y, double z) =>
        new(1, 0, 0, x,
            0, 1, 0, y,
            0, 0, 1, z,
            0, 0, 0, 1);

    /// <summary>Creates a right-handed rotation about X in degrees.</summary>
    public static Matrix4x4D CreateRotationX(double degrees)
    {
        var (cos, sin) = CosSin(degrees);
        return new Matrix4x4D(
            1, 0, 0, 0,
            0, cos, -sin, 0,
            0, sin, cos, 0,
            0, 0, 0, 1);
    }

    /// <summary>Creates a right-handed rotation about Y in degrees.</summary>
    public static Matrix4x4D CreateRotationY(double degrees)
    {
        var (cos, sin) = CosSin(degrees);
        return new Matrix4x4D(
            cos, 0, sin, 0,
            0, 1, 0, 0,
            -sin, 0, cos, 0,
            0, 0, 0, 1);
    }

    /// <summary>Creates a right-handed rotation about Z in degrees.</summary>
    public static Matrix4x4D CreateRotationZ(double degrees)
    {
        var (cos, sin) = CosSin(degrees);
        return new Matrix4x4D(
            cos, -sin, 0, 0,
            sin, cos, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
    }

    /// <summary>Returns a component by row and column, zero-based.</summary>
    public double this[int row, int column]
    {
        get
        {
            if ((uint)row > 3u)
                throw new ArgumentOutOfRangeException(nameof(row));
            if ((uint)column > 3u)
                throw new ArgumentOutOfRangeException(nameof(column));

            return (row, column) switch
            {
                (0, 0) => _m11,
                (0, 1) => _m12,
                (0, 2) => _m13,
                (0, 3) => _m14,
                (1, 0) => _m21,
                (1, 1) => _m22,
                (1, 2) => _m23,
                (1, 3) => _m24,
                (2, 0) => _m31,
                (2, 1) => _m32,
                (2, 2) => _m33,
                (2, 3) => _m34,
                (3, 0) => _m41,
                (3, 1) => _m42,
                (3, 2) => _m43,
                _ => _m44,
            };
        }
    }

    /// <summary>Multiplies two matrices: <paramref name="left"/> acts after <paramref name="right"/>.</summary>
    public static Matrix4x4D Multiply(Matrix4x4D left, Matrix4x4D right) =>
        new(
            left._m11 * right._m11 + left._m12 * right._m21 + left._m13 * right._m31 + left._m14 * right._m41,
            left._m11 * right._m12 + left._m12 * right._m22 + left._m13 * right._m32 + left._m14 * right._m42,
            left._m11 * right._m13 + left._m12 * right._m23 + left._m13 * right._m33 + left._m14 * right._m43,
            left._m11 * right._m14 + left._m12 * right._m24 + left._m13 * right._m34 + left._m14 * right._m44,
            left._m21 * right._m11 + left._m22 * right._m21 + left._m23 * right._m31 + left._m24 * right._m41,
            left._m21 * right._m12 + left._m22 * right._m22 + left._m23 * right._m32 + left._m24 * right._m42,
            left._m21 * right._m13 + left._m22 * right._m23 + left._m23 * right._m33 + left._m24 * right._m43,
            left._m21 * right._m14 + left._m22 * right._m24 + left._m23 * right._m34 + left._m24 * right._m44,
            left._m31 * right._m11 + left._m32 * right._m21 + left._m33 * right._m31 + left._m34 * right._m41,
            left._m31 * right._m12 + left._m32 * right._m22 + left._m33 * right._m32 + left._m34 * right._m42,
            left._m31 * right._m13 + left._m32 * right._m23 + left._m33 * right._m33 + left._m34 * right._m43,
            left._m31 * right._m14 + left._m32 * right._m24 + left._m33 * right._m34 + left._m34 * right._m44,
            left._m41 * right._m11 + left._m42 * right._m21 + left._m43 * right._m31 + left._m44 * right._m41,
            left._m41 * right._m12 + left._m42 * right._m22 + left._m43 * right._m32 + left._m44 * right._m42,
            left._m41 * right._m13 + left._m42 * right._m23 + left._m43 * right._m33 + left._m44 * right._m43,
            left._m41 * right._m14 + left._m42 * right._m24 + left._m43 * right._m34 + left._m44 * right._m44);

    /// <summary>Multiplies two matrices; <paramref name="left"/> acts after <paramref name="right"/>.</summary>
    public static Matrix4x4D operator *(Matrix4x4D left, Matrix4x4D right) => Multiply(left, right);

    /// <summary>Transforms a point, including translation.</summary>
    public Vector3Mm TransformPoint(Vector3Mm point) =>
        new(
            _m11 * point.X + _m12 * point.Y + _m13 * point.Z + _m14,
            _m21 * point.X + _m22 * point.Y + _m23 * point.Z + _m24,
            _m31 * point.X + _m32 * point.Y + _m33 * point.Z + _m34);

    /// <summary>Transforms a direction; translation is ignored.</summary>
    public Vector3Mm TransformDirection(Vector3Mm direction) =>
        new(
            _m11 * direction.X + _m12 * direction.Y + _m13 * direction.Z,
            _m21 * direction.X + _m22 * direction.Y + _m23 * direction.Z,
            _m31 * direction.X + _m32 * direction.Y + _m33 * direction.Z);

    /// <summary>Returns the determinant of the upper-left 3x3 block.</summary>
    public double Determinant3x3() =>
        _m11 * (_m22 * _m33 - _m23 * _m32) -
        _m12 * (_m21 * _m33 - _m23 * _m31) +
        _m13 * (_m21 * _m32 - _m22 * _m31);

    /// <summary>
    /// Inverts a rigid transform (orthonormal rotation with determinant +1 and zero bottom row).
    /// Returns false for non-rigid, reflected or otherwise degenerate matrices.
    /// </summary>
    public bool TryInvertRigid(out Matrix4x4D inverse)
    {
        inverse = Identity;

        const double tolerance = 1e-9;
        if (Math.Abs(_m41) > tolerance || Math.Abs(_m42) > tolerance ||
            Math.Abs(_m43) > tolerance || Math.Abs(_m44 - 1.0) > tolerance)
        {
            return false;
        }

        // R^T * R must be the identity.
        Span<double> r = stackalloc double[9] { _m11, _m12, _m13, _m21, _m22, _m23, _m31, _m32, _m33 };
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var dot = r[row * 3] * r[column * 3] +
                          r[row * 3 + 1] * r[column * 3 + 1] +
                          r[row * 3 + 2] * r[column * 3 + 2];
                var expected = row == column ? 1.0 : 0.0;
                if (Math.Abs(dot - expected) > tolerance)
                    return false;
            }
        }

        if (Math.Abs(Determinant3x3() - 1.0) > tolerance)
            return false;

        var tx = _m14;
        var ty = _m24;
        var tz = _m34;
        inverse = new Matrix4x4D(
            _m11, _m21, _m31, -(_m11 * tx + _m21 * ty + _m31 * tz),
            _m12, _m22, _m32, -(_m12 * tx + _m22 * ty + _m32 * tz),
            _m13, _m23, _m33, -(_m13 * tx + _m23 * ty + _m33 * tz),
            0, 0, 0, 1);

        return true;
    }

    /// <summary>Compares all components with the supplied tolerance.</summary>
    public bool NearlyEquals(Matrix4x4D other, double tolerance = 1e-9)
    {
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                if (Math.Abs(this[row, column] - other[row, column]) > tolerance)
                    return false;
            }
        }

        return Math.Abs(this[3, 0] - other[3, 0]) <= tolerance &&
               Math.Abs(this[3, 1] - other[3, 1]) <= tolerance &&
               Math.Abs(this[3, 2] - other[3, 2]) <= tolerance &&
               Math.Abs(this[3, 3] - other[3, 3]) <= tolerance;
    }

    /// <inheritdoc />
    public bool Equals(Matrix4x4D other)
    {
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                if (!this[row, column].Equals(other[row, column]))
                    return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Matrix4x4D other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            HashCode.Combine(_m11, _m12, _m13, _m14),
            HashCode.Combine(_m21, _m22, _m23, _m24),
            HashCode.Combine(_m31, _m32, _m33, _m34),
            HashCode.Combine(_m41, _m42, _m43, _m44));

    /// <inheritdoc />
    public override string ToString() =>
        $"[{_m11}, {_m12}, {_m13}, {_m14}; {_m21}, {_m22}, {_m23}, {_m24}; " +
        $"{_m31}, {_m32}, {_m33}, {_m34}; {_m41}, {_m42}, {_m43}, {_m44}]";

    private static (double Cos, double Sin) CosSin(double degrees)
    {
        if (!double.IsFinite(degrees))
            throw new ArgumentOutOfRangeException(nameof(degrees), "Angle must be finite.");

        var radians = degrees * Math.PI / 180.0;
        return (Math.Cos(radians), Math.Sin(radians));
    }
}
