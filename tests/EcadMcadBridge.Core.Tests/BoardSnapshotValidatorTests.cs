using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Validation;
using Xunit;

namespace EcadMcadBridge.Core.Tests;

public sealed class BoardSnapshotValidatorTests
{
    [Fact]
    public void ValidSnapshot_HasNoErrors()
    {
        var result = BoardSnapshotValidator.Validate(BoardSnapshotFixture.CreateValid());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void DuplicateComponentId_IsAnError()
    {
        var valid = BoardSnapshotFixture.CreateValid();
        var duplicate = valid.Components[0];
        var snapshot = BoardSnapshotFixture.With(valid, components: new[] { duplicate, duplicate });

        var result = BoardSnapshotValidator.Validate(snapshot);

        Assert.Contains(
            result.Errors,
            diagnostic => diagnostic.Code == BoardDiagnosticCodes.DuplicateId
                && diagnostic.Path == "components[1].id");
    }

    [Fact]
    public void MissingLayerReference_IsAnError()
    {
        var valid = BoardSnapshotFixture.CreateValid();
        var component = new ComponentInstance
        {
            Id = "component-bad-reference",
            Designator = "X1",
            LayerId = "missing-layer",
            Placement = new Transform
            {
                Position = Point2D.FromIu(0, 0),
                RotationDeg = 0,
                Side = BoardSide.Top,
            },
        };
        var snapshot = BoardSnapshotFixture.With(valid, components: new[] { component });

        var result = BoardSnapshotValidator.Validate(snapshot);

        Assert.Contains(
            result.Errors,
            diagnostic => diagnostic.Code == BoardDiagnosticCodes.InvalidReference
                && diagnostic.Path == "components[0].layerId");
    }

    [Fact]
    public void NegativePadDimension_IsAnError()
    {
        var valid = BoardSnapshotFixture.CreateValid();
        var badShape = new PadShape
        {
            Id = "padshape-negative",
            Kind = PadShapeKind.Rectangular,
            LayerId = "layer-top",
            Width = LengthValue.FromIu(-1_000_000),
            Height = LengthValue.FromIu(1_000_000),
        };
        var badStack = new PadStack
        {
            Id = "padstack-negative",
            Shapes = new[] { badShape },
        };
        var snapshot = BoardSnapshotFixture.With(valid, padStacks: new[] { badStack });

        var result = BoardSnapshotValidator.Validate(snapshot);

        Assert.Contains(
            result.Errors,
            diagnostic => diagnostic.Code == BoardDiagnosticCodes.NegativeDimension
                && diagnostic.Path == "padStacks[0].shapes[0].width");
    }

    [Fact]
    public void NonFiniteRotation_IsAnError()
    {
        var valid = BoardSnapshotFixture.CreateValid();
        var component = new ComponentInstance
        {
            Id = "component-non-finite",
            Designator = "X1",
            LayerId = "layer-top",
            Placement = new Transform
            {
                Position = Point2D.FromIu(0, 0),
                RotationDeg = double.PositiveInfinity,
                Side = BoardSide.Top,
            },
        };
        var snapshot = BoardSnapshotFixture.With(valid, components: new[] { component });

        var result = BoardSnapshotValidator.Validate(snapshot);

        Assert.Contains(
            result.Errors,
            diagnostic => diagnostic.Code == BoardDiagnosticCodes.NonFiniteNumber
                && diagnostic.Path == "components[0].placement.rotationDeg");
    }

    [Fact]
    public void TextWithoutRotation_IsWarningNotError()
    {
        var valid = BoardSnapshotFixture.CreateValid();
        var text = new TextItem
        {
            Id = "text-without-rotation",
            LayerId = "layer-top",
            Position = Point2D.FromIu(1_000_000, 1_000_000),
            Text = "R1",
            Height = LengthValue.FromIu(500_000),
            StrokeWidth = LengthValue.FromIu(100_000),
            RotationDeg = null,
        };
        var snapshot = BoardSnapshotFixture.With(valid, texts: new[] { text });

        var result = BoardSnapshotValidator.Validate(snapshot);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Errors, diagnostic => diagnostic.Path == "texts[0].rotationDeg");
        Assert.Contains(
            result.Warnings,
            diagnostic => diagnostic.Code == BoardDiagnosticCodes.MissingOptionalData
                && diagnostic.Path == "texts[0].rotationDeg");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteTextRotation_IsAnError(double rotation)
    {
        var valid = BoardSnapshotFixture.CreateValid();
        var text = new TextItem
        {
            Id = "text-non-finite-rotation",
            LayerId = "layer-top",
            Position = Point2D.FromIu(1_000_000, 1_000_000),
            Text = "R1",
            Height = LengthValue.FromIu(500_000),
            StrokeWidth = LengthValue.FromIu(100_000),
            RotationDeg = rotation,
        };
        var snapshot = BoardSnapshotFixture.With(valid, texts: new[] { text });

        var result = BoardSnapshotValidator.Validate(snapshot);

        Assert.Contains(
            result.Errors,
            diagnostic => diagnostic.Code == BoardDiagnosticCodes.NonFiniteNumber
                && diagnostic.Path == "texts[0].rotationDeg");
    }

    [Fact]
    public void MissingRequiredTrackWidth_IsAnError()
    {
        var valid = BoardSnapshotFixture.CreateValid();
        var track = new Track
        {
            Id = "track-missing-width",
            NetId = "net-1",
            LayerId = "layer-top",
            Start = Point2D.FromIu(0, 0),
            End = Point2D.FromIu(1_000_000, 0),
        };
        var snapshot = BoardSnapshotFixture.With(valid, tracks: new[] { track });

        var result = BoardSnapshotValidator.Validate(snapshot);

        Assert.Contains(
            result.Errors,
            diagnostic => diagnostic.Code == BoardDiagnosticCodes.MissingRequiredAttribute
                && diagnostic.Path == "tracks[0].width");
    }

    [Fact]
    public void OpenContour_IsAnError()
    {
        var valid = BoardSnapshotFixture.CreateValid();
        var outline = new BoardOutline
        {
            Id = "outline-open",
            Contours = new[]
            {
                new Contour
                {
                    Id = "contour-open",
                    Role = ContourRole.Outer,
                    Segments = new ContourSegment[]
                    {
                        Line(0, 0, 1_000_000, 0),
                        Line(1_000_000, 0, 1_000_000, 1_000_000),
                    },
                },
            },
        };
        var snapshot = BoardSnapshotFixture.With(valid, outline: outline);

        var result = BoardSnapshotValidator.Validate(snapshot);

        Assert.Contains(
            result.Errors,
            diagnostic => diagnostic.Code == BoardDiagnosticCodes.InvalidContour);
    }

    [Fact]
    public void InvalidArcRadius_IsAnError()
    {
        var valid = BoardSnapshotFixture.CreateValid();
        var outline = new BoardOutline
        {
            Id = "outline-invalid-arc",
            Contours = new[]
            {
                new Contour
                {
                    Id = "contour-invalid-arc",
                    Role = ContourRole.Outer,
                    Segments = new ContourSegment[]
                    {
                        Line(0, 0, 10_000_000, 0),
                        new ArcSegment
                        {
                            Start = Point2D.FromIu(10_000_000, 0),
                            End = Point2D.FromIu(10_000_000, 10_000_000),
                            Center = Point2D.FromIu(10_000_000, 5_000_000),
                            Radius = LengthValue.FromIu(1_000_000),
                            SweepDirection = ArcSweepDirection.Clockwise,
                        },
                        Line(10_000_000, 10_000_000, 0, 10_000_000),
                        Line(0, 10_000_000, 0, 0),
                    },
                },
            },
        };
        var snapshot = BoardSnapshotFixture.With(valid, outline: outline);

        var result = BoardSnapshotValidator.Validate(snapshot);

        Assert.Contains(
            result.Errors,
            diagnostic => diagnostic.Code == BoardDiagnosticCodes.InvalidArc);
    }

    [Fact]
    public void MinimalSnapshot_HasWarningsButNoErrors()
    {
        var result = BoardSnapshotValidator.Validate(BoardSnapshotFixture.CreateMinimal());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Contains(
            result.Warnings,
            diagnostic => diagnostic.Code == BoardDiagnosticCodes.MissingOptionalData);
    }

    [Fact]
    public void EmptySnapshot_HasEmptyCollectionWarnings()
    {
        var result = BoardSnapshotValidator.Validate(BoardSnapshotFixture.CreateEmpty());

        Assert.True(result.IsValid);
        Assert.Contains(
            result.Warnings,
            diagnostic => diagnostic.Code == BoardDiagnosticCodes.EmptyCollection
                && diagnostic.Path == "components");
        Assert.Contains(
            result.Warnings,
            diagnostic => diagnostic.Code == BoardDiagnosticCodes.EmptyCollection
                && diagnostic.Path == "nets");
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
