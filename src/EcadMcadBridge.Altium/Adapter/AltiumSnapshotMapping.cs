using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using PCB;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>Pure mappings used by the adapter and unit tests; no COM access.</summary>
internal static class AltiumSnapshotMapping
{
    public static Track? MapTrack(
        string id,
        int x1,
        int y1,
        int x2,
        int y2,
        int width,
        string layerId,
        string? netId)
    {
        if ((x1 == x2 && y1 == y2) || width <= 0)
            return null;

        return new Track
        {
            Id = id,
            NetId = netId,
            LayerId = layerId,
            Start = Point2D.FromIu(x1, y1),
            End = Point2D.FromIu(x2, y2),
            Width = LengthValue.FromIu(width),
        };
    }

    public static Via? MapVia(
        string id,
        int x,
        int y,
        int size,
        int hole,
        string startLayerId,
        string endLayerId,
        string? netId)
    {
        if (size <= 0 || hole < 0 || hole > size)
            return null;

        return new Via
        {
            Id = id,
            NetId = netId,
            Position = Point2D.FromIu(x, y),
            Diameter = LengthValue.FromIu(size),
            HoleDiameter = LengthValue.FromIu(hole),
            StartLayerId = startLayerId,
            EndLayerId = endLayerId,
        };
    }

    public static RegionKind MapRegionKind(
        TRegionKind? kind,
        bool isKeepout,
        out bool unknown)
    {
        unknown = false;
        switch (kind)
        {
            case TRegionKind.eRegionKind_Copper:
                return isKeepout ? RegionKind.KeepOut : RegionKind.Copper;
            case TRegionKind.eRegionKind_NamedRegion:
            case TRegionKind.eRegionKind_Cutout:
            case TRegionKind.eRegionKind_Cavity:
                return RegionKind.Other;
            default:
                unknown = true;
                return RegionKind.Other;
        }
    }

    public static IReadOnlyList<Contour> MapRegionContours(
        int regionIndex,
        IReadOnlyList<AltiumRegionContour> sourceContours,
        out int errors)
    {
        errors = 0;
        var result = new List<Contour>();
        for (var i = 0; i < sourceContours.Count; i++)
        {
            var source = sourceContours[i];
            var points = source.Points
                .Where(point => point.XIu is int && point.YIu is int)
                .ToArray();
            if (points.Length >= 2
                && points[0].XIu == points[^1].XIu
                && points[0].YIu == points[^1].YIu)
            {
                points = points[..^1];
            }

            if (points.Length < 3)
            {
                errors++;
                continue;
            }

            var segments = new List<ContourSegment>(points.Length);
            for (var p = 0; p < points.Length; p++)
            {
                var next = points[(p + 1) % points.Length];
                if (points[p].XIu == next.XIu && points[p].YIu == next.YIu)
                    continue;
                segments.Add(new LineSegment { Start = points[p], End = next });
            }

            if (segments.Count < 2)
            {
                errors++;
                continue;
            }

            result.Add(new Contour
            {
                Id = "altium:region:" + regionIndex + ":contour:" + i,
                Role = source.IsHole ? ContourRole.Hole : ContourRole.Outer,
                Segments = segments,
            });
        }

        return result;
    }

    public static TextItem MapText(
        string id,
        string? componentId,
        string layerId,
        int x,
        int y,
        string text,
        int size,
        int width,
        double rotation,
        out bool rotationKnown)
    {
        rotationKnown = double.IsFinite(rotation);
        return new TextItem
        {
            Id = id,
            ComponentId = componentId,
            LayerId = layerId,
            Position = Point2D.FromIu(x, y),
            Text = text,
            Height = size > 0 ? LengthValue.FromIu(size) : LengthValue.FromIu(null),
            StrokeWidth = width > 0 ? LengthValue.FromIu(width) : LengthValue.FromIu(null),
            RotationDeg = rotationKnown ? AngleNormalizer.NormalizeDegrees(rotation) : null,
        };
    }
}
