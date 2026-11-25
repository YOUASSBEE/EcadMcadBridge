namespace EcadMcadBridge.Core.Placement;

/// <summary>An immutable two-dimensional vector in millimetres.</summary>
public readonly struct Vector2Mm : IEquatable<Vector2Mm>
{
    /// <summary>Creates a vector and rejects non-finite coordinates.</summary>
    public Vector2Mm(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x), "Vector coordinates must be finite.");

        X = x;
        Y = y;
    }

    /// <summary>X coordinate in millimetres.</summary>
    public double X { get; }

    /// <summary>Y coordinate in millimetres.</summary>
    public double Y { get; }

    /// <summary>Returns the zero vector.</summary>
    public static Vector2Mm Zero => new(0, 0);

    /// <summary>Euclidean length in millimetres.</summary>
    public double Length => Math.Sqrt(X * X + Y * Y);

    /// <summary>Adds two vectors.</summary>
    public static Vector2Mm operator +(Vector2Mm left, Vector2Mm right) =>
        new(left.X + right.X, left.Y + right.Y);

    /// <summary>Subtracts two vectors.</summary>
    public static Vector2Mm operator -(Vector2Mm left, Vector2Mm right) =>
        new(left.X - right.X, left.Y - right.Y);

    /// <summary>Negates a vector.</summary>
    public static Vector2Mm operator -(Vector2Mm value) => new(-value.X, -value.Y);

    /// <summary>Scales a vector.</summary>
    public static Vector2Mm operator *(Vector2Mm value, double scale) =>
        new(value.X * scale, value.Y * scale);

    /// <summary>Scales a vector.</summary>
    public static Vector2Mm operator *(double scale, Vector2Mm value) => value * scale;

    /// <summary>Compares coordinates with the supplied tolerance.</summary>
    public bool NearlyEquals(Vector2Mm other, double tolerance = 1e-9) =>
        Math.Abs(X - other.X) <= tolerance && Math.Abs(Y - other.Y) <= tolerance;

    /// <inheritdoc />
    public bool Equals(Vector2Mm other) => X.Equals(other.X) && Y.Equals(other.Y);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Vector2Mm other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <inheritdoc />
    public override string ToString() => $"({X}, {Y}) mm";
}
