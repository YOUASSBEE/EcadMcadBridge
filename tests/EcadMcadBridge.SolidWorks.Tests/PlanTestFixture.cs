using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.SolidWorks.Tests;

internal static class PlanTestFixture
{
    public static BoardSnapshot Snapshot(params string[] componentIds)
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
                CadVersion = "26",
                DocumentFileName = "solidworks-test.PcbDoc",
                ExportedAtUtc = new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero),
                ToolVersion = "0.4.0",
            },
            Properties = new BoardProperties
            {
                Id = "board-solidworks-test",
                Name = "SOLIDWORKS test",
                Thickness = LengthValue.FromIu(1_600_000),
            },
            Outline = new BoardOutline
            {
                Id = "outline-solidworks-test",
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
                            Line(10_000_000, 0, 10_000_000, 10_000_000),
                            Line(10_000_000, 10_000_000, 0, 10_000_000),
                            Line(0, 10_000_000, 0, 0),
                        },
                    },
                },
            },
            Layers = new[] { top },
            Components = componentIds.Select((id, index) => new ComponentInstance
            {
                Id = id,
                Designator = "U" + (index + 1),
                LayerId = top.Id,
                Placement = new Transform
                {
                    Position = Point2D.FromIu(index * 1_000_000, index * 1_000_000),
                    RotationDeg = 0,
                    Side = BoardSide.Top,
                },
            }).ToArray(),
        };
    }

    private static LineSegment Line(int x1, int y1, int x2, int y2) => new()
    {
        Start = Point2D.FromIu(x1, y1),
        End = Point2D.FromIu(x2, y2),
    };

    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "EcadMcadBridge.SolidWorks.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
