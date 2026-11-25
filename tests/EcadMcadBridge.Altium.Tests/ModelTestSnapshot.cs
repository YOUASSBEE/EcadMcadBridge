using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Altium.Tests;

internal static class ModelTestSnapshot
{
    public static BoardSnapshot Create(params ComponentSeed[] seeds)
    {
        var top = new LayerRef
        {
            Id = "layer-top",
            Name = "Top Layer",
            Kind = LayerKind.Copper,
            Side = BoardSide.Top,
        };
        var bottom = new LayerRef
        {
            Id = "layer-bottom",
            Name = "Bottom Layer",
            Kind = LayerKind.Copper,
            Side = BoardSide.Bottom,
        };

        var components = seeds
            .Select(seed => new ComponentInstance
            {
                Id = seed.Id,
                Designator = seed.Designator,
                LayerId = seed.Side == BoardSide.Top ? top.Id : bottom.Id,
                SourceUniqueId = seed.SourceUniqueId,
                Placement = new Transform
                {
                    Position = Point2D.FromIu(seed.XIu, seed.YIu),
                    RotationDeg = seed.RotationDeg,
                    Side = seed.Side,
                },
                Height = LengthValue.FromIu(500_000),
            })
            .ToArray();

        return new BoardSnapshot
        {
            Source = new BoardSnapshotSource
            {
                Cad = "altium",
                CadVersion = "26",
                DocumentFileName = "models-test.PcbDoc",
                ExportedAtUtc = new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero),
                ToolVersion = "0.4.0",
            },
            Properties = new BoardProperties
            {
                Id = "board-models-test",
                Name = "Models test",
                Thickness = LengthValue.FromIu(1_600_000),
            },
            Outline = new BoardOutline
            {
                Id = "outline-models-test",
                Contours = new[]
                {
                    new Contour
                    {
                        Id = "outline-outer",
                        Role = ContourRole.Outer,
                        Segments = new ContourSegment[]
                        {
                            Line(0, 0, 10_000_000, 0),
                            Line(10_000_000, 0, 10_000_000, 10_000_000),
                            Line(10_000_000, 10_000_000, 0, 10_000_000),
                            Line(0, 10_000_000, 0, 0),
                        },
                    },
                },
            },
            Layers = new[] { top, bottom },
            Components = components,
        };
    }

    public static ComponentSeed Seed(
        string id,
        string designator,
        int xIu = 1_000_000,
        int yIu = 2_000_000,
        double rotationDeg = 0,
        BoardSide side = BoardSide.Top,
        string? sourceUniqueId = null) =>
        new(id, designator, xIu, yIu, rotationDeg, side, sourceUniqueId);

    private static LineSegment Line(int x1, int y1, int x2, int y2) => new()
    {
        Start = Point2D.FromIu(x1, y1),
        End = Point2D.FromIu(x2, y2),
    };

    internal sealed record ComponentSeed(
        string Id,
        string Designator,
        int XIu,
        int YIu,
        double RotationDeg,
        BoardSide Side,
        string? SourceUniqueId);
}
