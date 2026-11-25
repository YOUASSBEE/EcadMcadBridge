namespace EcadMcadBridge.Core.Placement;

/// <summary>An immutable three-dimensional vector in millimetres.</summary>
public readonly struct Vector3Mm : IEquatable<Vector3Mm>
{
    /// <summary>Creates a vector and rejects non-finite coordinates.</summary>
    public Vector3Mm(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            throw new ArgumentOutOfRangeException(nameof(x), "Vector coordinates must be finite.");

        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>X coordinate in millimetres.</summary>
    public double X { get; }

    /// <summary>Y coordinate in millimetres.</summary>
    public double Y { get; }

    /// <summary>Z coordinate in millimetres.</summary>
    public double Z { get; }

    /// <summary>Returns the zero vector.</summary>
    public static Vector3Mm Zero => new(0, 0, 0);

    /// <summary>Euclidean length in millimetres.</summary>
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    /// <summary>Adds two vectors.</summary>
    public static Vector3Mm operator +(Vector3Mm left, Vector3Mm right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    /// <summary>Subtracts two vectors.</summary>
    public static Vector3Mm operator -(Vector3Mm left, Vector3Mm right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    /// <summary>Negates a vector.</summary>
    public static Vector3Mm operator -(Vector3Mm value) => new(-value.X, -value.Y, -value.Z);

    /// <summary>Scales a vector.</summary>
    public static Vector3Mm operator *(Vector3Mm value, double scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    /// <summary>Scales a vector.</summary>
    public static Vector3Mm operator *(double scale, Vector3Mm value) => value * scale;

    /// <summary>Compares coordinates with the supplied tolerance.</summary>
    public bool NearlyEquals(Vector3Mm other, double tolerance = 1e-9) =>
        Math.Abs(X - other.X) <= tolerance &&
        Math.Abs(Y - other.Y) <= tolerance &&
        Math.Abs(Z - other.Z) <= tolerance;

    /// <inheritdoc />
    public bool Equals(Vector3Mm other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Vector3Mm other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <inheritdoc />
    public override string ToString() => $"({X}, {Y}, {Z}) mm";
}
