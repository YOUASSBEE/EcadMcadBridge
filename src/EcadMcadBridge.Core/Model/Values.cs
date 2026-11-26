using System.Globalization;
using EcadMcadBridge.Core.Diagnostics;

namespace EcadMcadBridge.Core.Model;

/// <summary>
/// A linear value that keeps the original Altium IU and computes millimetres from it.
/// The public contract never exposes an independently mutable millimetre value.
/// </summary>
public readonly struct LengthValue : IEquatable<LengthValue>
{
    private readonly double? _providedMm;
    private readonly bool _hasProvidedMm;

    private LengthValue(int? iu, double? providedMm, bool hasProvidedMm)
    {
        Iu = iu;
        _providedMm = providedMm;
        _hasProvidedMm = hasProvidedMm;
    }

    /// <summary>Original Altium internal units; null means the value is unknown.</summary>
    public int? Iu { get; }

    /// <summary>Millimetres computed from <see cref="Iu"/> with the D-015 coefficient.</summary>
    public double? Mm => Iu.HasValue ? AltiumUnits.IuToMm(Iu.Value) : null;

    public bool HasValue => Iu.HasValue;

    public static LengthValue FromIu(int? iu) => new(iu, null, hasProvidedMm: false);

    public static LengthValue FromIu(int iu) => new(iu, null, hasProvidedMm: false);

    /// <summary>
    /// Used by the JSON reader so a supplied millimetre value can be checked by the validator.
    /// It is internal: model users cannot create an independent millimetre value.
    /// </summary>
    internal static LengthValue FromJson(int? iu, double? providedMm) =>
        new(iu, providedMm, hasProvidedMm: true);

    internal double? ProvidedMm => _providedMm;

    internal bool HasProvidedMm => _hasProvidedMm;

    internal bool HasConsistentMm(double toleranceMm)
    {
        if (!_hasProvidedMm)
            return true;

        if (Iu is null)
            return _providedMm is null;

        return _providedMm is not null && Math.Abs(_providedMm.Value - Mm!.Value) <= toleranceMm;
    }

    public bool Equals(LengthValue other) => Iu == other.Iu;

    public override bool Equals(object? obj) => obj is LengthValue other && Equals(other);

    public override int GetHashCode() => Iu.GetHashCode();

    public override string ToString() => Iu?.ToString(CultureInfo.InvariantCulture) ?? "null";
}

/// <summary>A point with both IU and computed millimetre coordinates.</summary>
public readonly struct Point2D : IEquatable<Point2D>
{
    public Point2D(LengthValue x, LengthValue y)
    {
        X = x;
        Y = y;
    }

    public LengthValue X { get; }

    public LengthValue Y { get; }

    public int? XIu => X.Iu;

    public int? YIu => Y.Iu;

    public double? XMm => X.Mm;

    public double? YMm => Y.Mm;

    public bool HasValue => X.HasValue && Y.HasValue;

    public static Point2D FromIu(int x, int y) => new(LengthValue.FromIu(x), LengthValue.FromIu(y));

    public static Point2D Origin => FromIu(0, 0);

    public bool Equals(Point2D other) => X.Equals(other.X) && Y.Equals(other.Y);

    public override bool Equals(object? obj) => obj is Point2D other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);
}

public enum BoardSide
{
    Top,
    Bottom,
}

public enum LayerKind
{
    Copper,
    Mechanical,
    Silkscreen,
    SolderMask,
    PasteMask,
    Drill,
    Other,
}

public enum ContourRole
{
    Outer,
    Hole,
}

public enum ArcSweepDirection
{
    Clockwise,
    CounterClockwise,
}

/// <summary>
/// Evidence level for board-outline extraction. Older packages without the field are
/// <see cref="Unknown"/>, never assumed complete.
/// </summary>
public enum OutlineCompleteness
{
    Complete,
    Incomplete,
    Unknown,
}

public enum BoardCutoutRole
{
    /// <summary>The whole source contour coincides with one whole Outer contour.</summary>
    BoardShape,

    /// <summary>The whole source contour lies on part of an Outer contour, so it is board-shape evidence but not a hole.</summary>
    BoardShapeFragment,

    /// <summary>The whole source contour lies strictly inside one Outer contour.</summary>
    Hole,

    /// <summary>The contour is neither on nor inside the board boundary.</summary>
    Unrelated,

    /// <summary>The contour's nesting/flag semantics cannot be proven; fail closed.</summary>
    Unsupported,
}

public enum PadShapeKind
{
    Circular,
    Rectangular,
    RoundedRectangle,
    Octagonal,
    Custom,
}

public enum RegionKind
{
    Copper,
    KeepOut,
    Other,
}
