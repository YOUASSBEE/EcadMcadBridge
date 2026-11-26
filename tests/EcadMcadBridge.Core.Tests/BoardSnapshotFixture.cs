using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Core.Tests;

internal static class BoardSnapshotFixture
{
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);

    public static BoardSnapshot CreateValid()
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
        var mechanical = new LayerRef
        {
            Id = "layer-mech-1",
            Name = "Mechanical 1",
            Kind = LayerKind.Mechanical,
        };

        var outline = new BoardOutline
        {
            Id = "outline-1",
            Completeness = OutlineCompleteness.Complete,
            Contours = new[]
            {
                new Contour
                {
                    Id = "outline-outer",
                    Role = ContourRole.Outer,
                    Segments = new ContourSegment[]
                    {
                        Line(0, 0, 10_000_000, 0),
                        new ArcSegment
                        {
                            Start = Point2D.FromIu(10_000_000, 0),
                            End = Point2D.FromIu(10_000_000, 10_000_000),
                            Center = Point2D.FromIu(10_000_000, 5_000_000),
                            Radius = LengthValue.FromIu(5_000_000),
                            SweepDirection = ArcSweepDirection.CounterClockwise,
                        },
                        Line(10_000_000, 10_000_000, 0, 10_000_000),
                        Line(0, 10_000_000, 0, 0),
                    },
                },
                RectangleContour(
                    "outline-hole",
                    2_000_000,
                    2_000_000,
                    4_000_000,
                    4_000_000,
                    ContourRole.Hole),
            },
        };

        var component1 = new ComponentInstance
        {
            Id = "component-1",
            Designator = "R1",
            Comment = "10k",
            Footprint = "0402",
            SourceUniqueId = "altium-uid-1",
            LayerId = top.Id,
            Placement = new Transform
            {
                Position = Point2D.FromIu(1_000_000, 2_000_000),
                RotationDeg = 0,
                Side = BoardSide.Top,
            },
            Height = LengthValue.FromIu(500_000),
            Fitted = true,
        };
        var component2 = new ComponentInstance
        {
            Id = "component-2",
            Designator = "C1",
            Comment = "100n",
            Footprint = "0402",
            LayerId = bottom.Id,
            Placement = new Transform
            {
                Position = Point2D.FromIu(2_000_000, 3_000_000),
                RotationDeg = 90,
                Side = BoardSide.Bottom,
            },
            Fitted = true,
        };

        var rectangularShape = new PadShape
        {
            Id = "padshape-rect",
            Kind = PadShapeKind.Rectangular,
            LayerId = top.Id,
            Width = LengthValue.FromIu(1_000_000),
            Height = LengthValue.FromIu(800_000),
        };
        var customShape = new PadShape
        {
            Id = "padshape-custom",
            Kind = PadShapeKind.Custom,
            LayerId = top.Id,
            Vertices = new[]
            {
                Point2D.FromIu(0, 0),
                Point2D.FromIu(500_000, 0),
                Point2D.FromIu(250_000, 500_000),
            },
        };
        var circularShape = new PadShape
        {
            Id = "padshape-circle",
            Kind = PadShapeKind.Circular,
            LayerId = bottom.Id,
            Width = LengthValue.FromIu(1_200_000),
            Height = LengthValue.FromIu(1_200_000),
        };
        var padStack1 = new PadStack
        {
            Id = "padstack-1",
            Shapes = new[] { rectangularShape, customShape },
        };
        var padStack2 = new PadStack
        {
            Id = "padstack-2",
            Shapes = new[] { circularShape },
        };

        var pad1 = new Pad
        {
            Id = "pad-1",
            ComponentId = component1.Id,
            NetId = "net-1",
            LayerId = top.Id,
            PadStackId = padStack1.Id,
            Position = Point2D.FromIu(1_000_000, 2_000_000),
            RotationDeg = 0,
        };
        var pad2 = new Pad
        {
            Id = "pad-2",
            ComponentId = component2.Id,
            NetId = "net-2",
            LayerId = bottom.Id,
            PadStackId = padStack2.Id,
            Position = Point2D.FromIu(2_000_000, 3_000_000),
            RotationDeg = 90,
        };

        var nets = new[]
        {
            new Net { Id = "net-1", Name = "GND" },
            new Net { Id = "net-2", Name = "VCC" },
        };

        var tracks = new[]
        {
            new Track
            {
                Id = "track-1",
                NetId = "net-1",
                LayerId = top.Id,
                Start = Point2D.FromIu(1_000_000, 2_000_000),
                End = Point2D.FromIu(3_000_000, 2_000_000),
                Width = LengthValue.FromIu(250_000),
            },
        };

        var vias = new[]
        {
            new Via
            {
                Id = "via-1",
                NetId = "net-1",
                Position = Point2D.FromIu(3_000_000, 2_000_000),
                Diameter = LengthValue.FromIu(600_000),
                HoleDiameter = LengthValue.FromIu(300_000),
                StartLayerId = top.Id,
                EndLayerId = bottom.Id,
            },
        };

        var regions = new[]
        {
            new Region
            {
                Id = "region-1",
                NetId = "net-1",
                LayerId = top.Id,
                Kind = RegionKind.Copper,
                Polygon = new PolygonGeometry
                {
                    Contours = new[]
                    {
                        RectangleContour("region-outer", 0, 0, 1_000_000, 1_000_000, ContourRole.Outer),
                        RectangleContour("region-hole", 200_000, 200_000, 400_000, 400_000, ContourRole.Hole),
                    },
                },
            },
        };

        var texts = new[]
        {
            new TextItem
            {
                Id = "text-1",
                ComponentId = component1.Id,
                LayerId = top.Id,
                Position = Point2D.FromIu(1_000_000, 1_000_000),
                Text = "R1",
                Height = LengthValue.FromIu(500_000),
                StrokeWidth = LengthValue.FromIu(100_000),
                RotationDeg = 0,
            },
        };

        return new BoardSnapshot
        {
            Source = new BoardSnapshotSource
            {
                Cad = "altium",
                CadVersion = "26.12.2.9",
                DocumentFileName = "board.PcbDoc",
                ExportedAtUtc = Timestamp,
                ToolVersion = "0.2.0",
            },
            Properties = new BoardProperties
            {
                Id = "board-1",
                Name = "Synthetic board",
                Material = "FR-4",
                Thickness = LengthValue.FromIu(1_600_000),
                OriginX = LengthValue.FromIu(0),
                OriginY = LengthValue.FromIu(0),
            },
            Outline = outline,
            Layers = new[] { top, bottom, mechanical },
            Components = new[] { component1, component2 },
            PadStacks = new[] { padStack1, padStack2 },
            Pads = new[] { pad1, pad2 },
            Nets = nets,
            Tracks = tracks,
            Vias = vias,
            Regions = regions,
            Texts = texts,
        };
    }

    public static BoardSnapshot CreateMinimal()
    {
        var top = new LayerRef
        {
            Id = "layer-top",
            Name = "Top Layer",
            Kind = LayerKind.Copper,
            Side = BoardSide.Top,
        };

        return new BoardSnapshot
        {
            Source = new BoardSnapshotSource
            {
                Cad = "altium",
                CadVersion = "26.12.2.9",
                DocumentFileName = "minimal.PcbDoc",
                ExportedAtUtc = Timestamp,
            },
            Properties = new BoardProperties
            {
                Id = "board-minimal",
            },
            Outline = new BoardOutline
            {
                Id = "outline-minimal",
                Contours = new[]
                {
                    RectangleContour("contour-minimal", 0, 0, 1_000_000, 1_000_000, ContourRole.Outer),
                },
            },
            Layers = new[] { top },
            Components = new[]
            {
                new ComponentInstance
                {
                    Id = "component-minimal",
                    Designator = "R1",
                    LayerId = top.Id,
                    Placement = new Transform
                    {
                        Position = Point2D.FromIu(0, 0),
                        RotationDeg = 0,
                        Side = BoardSide.Top,
                    },
                },
            },
        };
    }

    public static BoardSnapshot CreateEmpty()
    {
        return new BoardSnapshot
        {
            Source = new BoardSnapshotSource
            {
                Cad = "altium",
                DocumentFileName = "empty.PcbDoc",
                ExportedAtUtc = Timestamp,
            },
            Properties = new BoardProperties
            {
                Id = "board-empty",
                Thickness = LengthValue.FromIu(0),
            },
            Outline = new BoardOutline
            {
                Id = "outline-empty",
            },
        };
    }

    public static BoardSnapshot CreateValidReversed()
    {
        var source = CreateValid();
        return With(
            source,
            layers: source.Layers.Reverse().ToArray(),
            components: source.Components.Reverse().ToArray(),
            padStacks: source.PadStacks.Reverse().ToArray(),
            pads: source.Pads.Reverse().ToArray(),
            nets: source.Nets.Reverse().ToArray(),
            tracks: source.Tracks.Reverse().ToArray(),
            vias: source.Vias.Reverse().ToArray(),
            regions: source.Regions.Reverse().ToArray(),
            texts: source.Texts.Reverse().ToArray());
    }

    public static BoardSnapshot With(
        BoardSnapshot source,
        IReadOnlyList<LayerRef>? layers = null,
        IReadOnlyList<ComponentInstance>? components = null,
        IReadOnlyList<PadStack>? padStacks = null,
        IReadOnlyList<Pad>? pads = null,
        IReadOnlyList<Net>? nets = null,
        IReadOnlyList<Track>? tracks = null,
        IReadOnlyList<Via>? vias = null,
        IReadOnlyList<Region>? regions = null,
        IReadOnlyList<TextItem>? texts = null,
        BoardOutline? outline = null,
        BoardSnapshotSource? snapshotSource = null)
    {
        return new BoardSnapshot
        {
            Source = snapshotSource ?? source.Source,
            SourceUnits = source.SourceUnits,
            Units = source.Units,
            Properties = source.Properties,
            Outline = outline ?? source.Outline,
            Layers = layers ?? source.Layers,
            Components = components ?? source.Components,
            PadStacks = padStacks ?? source.PadStacks,
            Pads = pads ?? source.Pads,
            Nets = nets ?? source.Nets,
            Tracks = tracks ?? source.Tracks,
            Vias = vias ?? source.Vias,
            Regions = regions ?? source.Regions,
            Texts = texts ?? source.Texts,
        };
    }

    public static Contour RectangleContour(
        string id,
        int x1,
        int y1,
        int x2,
        int y2,
        ContourRole role)
    {
        return new Contour
        {
            Id = id,
            Role = role,
            Segments = new ContourSegment[]
            {
                Line(x1, y1, x2, y1),
                Line(x2, y1, x2, y2),
                Line(x2, y2, x1, y2),
                Line(x1, y2, x1, y1),
            },
        };
    }

    private static LineSegment Line(int x1, int y1, int x2, int y2)
    {
        return new LineSegment
        {
            Start = Point2D.FromIu(x1, y1),
            End = Point2D.FromIu(x2, y2),
        };
    }
}
