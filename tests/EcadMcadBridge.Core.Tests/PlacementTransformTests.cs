using EcadMcadBridge.Core.Placement;
using Xunit;

namespace EcadMcadBridge.Core.Tests;

public sealed class PlacementTransformTests
{
    private const double TranslationToleranceMm = 0.0001;
    private const double RotationTolerance = 1e-8;
    private const double GeometryToleranceMm = 0.01;

    private static readonly Vector2Mm TopOffset = new(-4.314346, 1.740448);
    private static readonly Vector2Mm BottomLocalOffset = new(-0.943031, -1.740448);
    private static readonly Vector2Mm BottomBoardOffset = new(-0.943031, 1.740448);
    private static readonly Vector2Mm BottomPivotOffset = new(3.371315, 0);

    private static readonly double[] Identity = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
    private static readonly double[] Rx180 = { 1, 0, 0, 0, -1, 0, 0, 0, -1 };
    private static readonly double[] Rz90 = { 0, -1, 0, 1, 0, 0, 0, 0, 1 };
    private static readonly double[] Rz180 = { -1, 0, 0, 0, -1, 0, 0, 0, 1 };
    private static readonly double[] RzMinus90 = { 0, 1, 0, -1, 0, 0, 0, 0, 1 };
    private static readonly double[] Rz90Rx180 = { 0, 1, 0, 1, 0, 0, 0, 0, -1 };
    private static readonly double[] Rz180Rx180 = { -1, 0, 0, 0, 1, 0, 0, 0, -1 };
    private static readonly double[] RzMinus90Rx180 = { 0, -1, 0, -1, 0, 0, 0, 0, -1 };

    private static FootprintOriginCalibration Calibration =>
        new(TopOffset, BottomLocalOffset);

    // ---------------------------------------------------------------------
    // Matrix core
    // ---------------------------------------------------------------------

    [Fact]
    public void Identity_LeavesPointAndDirectionUnchanged()
    {
        var point = new Vector3Mm(1.25, -2.5, 3.75);

        Assert.True(Matrix4x4D.Identity.TransformPoint(point).NearlyEquals(point, 1e-12));
        Assert.True(Matrix4x4D.Identity.TransformDirection(point).NearlyEquals(point, 1e-12));
        Assert.Equal(1.0, Matrix4x4D.Identity.Determinant3x3(), precision: 12);
    }

    [Fact]
    public void Translation_MovesPointButNotDirection()
    {
        var matrix = Matrix4x4D.CreateTranslation(10, -20, 3);

        Assert.True(matrix.TransformPoint(new Vector3Mm(1, 2, 4)).NearlyEquals(new Vector3Mm(11, -18, 7), 1e-12));
        Assert.True(matrix.TransformDirection(new Vector3Mm(1, 2, 4)).NearlyEquals(new Vector3Mm(1, 2, 4), 1e-12));
    }

    [Fact]
    public void Rotations_UseRightHandedColumnVectorConvention()
    {
        Assert.True(Matrix4x4D.CreateRotationX(90).TransformPoint(new Vector3Mm(0, 1, 0))
            .NearlyEquals(new Vector3Mm(0, 0, 1), 1e-12));
        Assert.True(Matrix4x4D.CreateRotationY(90).TransformPoint(new Vector3Mm(1, 0, 0))
            .NearlyEquals(new Vector3Mm(0, 0, -1), 1e-12));
        Assert.True(Matrix4x4D.CreateRotationZ(90).TransformPoint(new Vector3Mm(1, 0, 0))
            .NearlyEquals(new Vector3Mm(0, 1, 0), 1e-12));
    }

    [Fact]
    public void Composition_AppliesRightMatrixFirst()
    {
        // T * R: rotate first, then translate.
        var translateAfterRotate = Matrix4x4D.Multiply(
            Matrix4x4D.CreateTranslation(10, 0, 0),
            Matrix4x4D.CreateRotationZ(90));

        // R * T: translate first, then rotate.
        var rotateAfterTranslate = Matrix4x4D.Multiply(
            Matrix4x4D.CreateRotationZ(90),
            Matrix4x4D.CreateTranslation(10, 0, 0));

        Assert.True(translateAfterRotate.TransformPoint(new Vector3Mm(1, 0, 0))
            .NearlyEquals(new Vector3Mm(10, 1, 0), 1e-12));
        Assert.True(rotateAfterTranslate.TransformPoint(new Vector3Mm(1, 0, 0))
            .NearlyEquals(new Vector3Mm(0, 11, 0), 1e-12));
    }

    [Fact]
    public void Determinant_ReflectsRotationAndScale()
    {
        Assert.Equal(1.0, Matrix4x4D.CreateRotationZ(41).Determinant3x3(), precision: 12);
        Assert.Equal(1.0, Matrix4x4D.CreateRotationX(180).Determinant3x3(), precision: 12);

        var scaled = new Matrix4x4D(
            2, 0, 0, 0,
            0, 3, 0, 0,
            0, 0, 4, 0,
            0, 0, 0, 1);
        Assert.Equal(24.0, scaled.Determinant3x3(), precision: 12);

        var mirrored = new Matrix4x4D(
            1, 0, 0, 0,
            0, -1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
        Assert.Equal(-1.0, mirrored.Determinant3x3(), precision: 12);
    }

    [Fact]
    public void RigidInverse_RoundTripsAndRejectsNonRigidMatrices()
    {
        var rigid = Matrix4x4D.Multiply(
            Matrix4x4D.CreateTranslation(10, -2, 3),
            Matrix4x4D.CreateRotationZ(37));

        Assert.True(rigid.TryInvertRigid(out var inverse));
        Assert.True(Matrix4x4D.Multiply(rigid, inverse).NearlyEquals(Matrix4x4D.Identity, 1e-9));
        Assert.True(Matrix4x4D.CreateRotationX(180).TryInvertRigid(out _));

        var scaled = new Matrix4x4D(
            2, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
        Assert.False(scaled.TryInvertRigid(out _));

        var mirrored = new Matrix4x4D(
            1, 0, 0, 0,
            0, -1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
        Assert.False(mirrored.TryInvertRigid(out _));
    }

    [Fact]
    public void Matrix_RejectsNonFiniteComponents()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Matrix4x4D(
            double.NaN, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1));

        Assert.Throws<ArgumentOutOfRangeException>(() => Matrix4x4D.CreateRotationZ(double.PositiveInfinity));
    }

    [Fact]
    public void Matrix_PreservesDoublePrecisionTranslation()
    {
        var matrix = Matrix4x4D.CreateTranslation(1e-12, -1e-12, 0);
        var transformed = matrix.TransformPoint(Vector3Mm.Zero);

        Assert.True(Math.Abs(transformed.X - 1e-12) < 1e-24);
        Assert.True(Math.Abs(transformed.Y + 1e-12) < 1e-24);
    }

    // ---------------------------------------------------------------------
    // Model-state normalization
    // ---------------------------------------------------------------------

    [Fact]
    public void Normalize_SourceFootprintIsUnchanged()
    {
        var raw = new ModelStateInput(23, 37, 41, 1);

        Assert.Equal(raw, PlacementTransformBuilder.Normalize(
            raw, ComponentSide.Top, ModelStateSource.SourceFootprint, 90));
    }

    [Fact]
    public void Normalize_PlacedBodyTopSubtractsComponentRotation()
    {
        var raw = new ModelStateInput(23, 37, 131, 1);
        var normalized = PlacementTransformBuilder.Normalize(
            raw, ComponentSide.Top, ModelStateSource.PlacedBodyEffective, 90);

        Assert.True(normalized.NearlyEquals(new ModelStateInput(23, 37, 41, 1)));
    }

    [Fact]
    public void Normalize_PlacedBodyBottomAddsComponentRotation()
    {
        var raw = new ModelStateInput(0, 0, 270, 0);
        var normalized = PlacementTransformBuilder.Normalize(
            raw, ComponentSide.Bottom, ModelStateSource.PlacedBodyEffective, 90);

        Assert.True(normalized.IsZero());
    }

    [Fact]
    public void Normalize_RejectsNonFiniteRotation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlacementTransformBuilder.Normalize(
            ModelStateInput.Zero, ComponentSide.Top, ModelStateSource.SourceFootprint, double.NaN));
    }

    // ---------------------------------------------------------------------
    // Footprint calibration
    // ---------------------------------------------------------------------

    [Fact]
    public void Calibration_ComputesBottomBoardOffsetAndMeasuredPivot()
    {
        var calibration = Calibration;

        Assert.True(calibration.BottomModelOriginOffsetBoardMm!.Value.NearlyEquals(BottomBoardOffset, 1e-9));
        Assert.True(calibration.BottomPivotOffsetMm!.Value.NearlyEquals(BottomPivotOffset, 1e-9));
    }

    // ---------------------------------------------------------------------
    // Top placement
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(0.0, 23.085654, 54.540448, 0.0)]
    [InlineData(90.0, 65.659552, 48.485654, 0.0)]
    [InlineData(180.0, 111.714346, 51.059552, 0.0)]
    [InlineData(-90.0, 149.140448, 57.114346, 0.0)]
    public void Top_U101ToU104_MatchR3ControlData(
        double rotation,
        double expectedX,
        double expectedY,
        double expectedZ)
    {
        var positions = new[]
        {
            new Vector2Mm(27.4, 52.8),
            new Vector2Mm(67.4, 52.8),
            new Vector2Mm(107.4, 52.8),
            new Vector2Mm(147.4, 52.8),
        };
        var expectedRotations = new[] { Identity, Rz90, Rz180, RzMinus90 };
        var index = rotation switch { 0 => 0, 90 => 1, 180 => 2, _ => 3 };

        var request = CreateRequest(positions[index], rotation, calibration: Calibration);
        var result = PlacementTransformBuilder.Build(request);

        Assert.True(result.IsSupported);
        AssertTransform(
            Expected(expectedRotations[index], new Vector3Mm(expectedX, expectedY, expectedZ)),
            result.Transform!.Value);
    }

    [Fact]
    public void Top_ModelOriginOffsetRotatesAroundComponentPosition()
    {
        var request = CreateRequest(new Vector2Mm(67.4, 52.8), 90, calibration: Calibration);
        var result = PlacementTransformBuilder.Build(request);

        Assert.True(result.IsSupported);

        // The offset (-4.314346, +1.740448) rotated by +90 becomes (-1.740448, -4.314346).
        var expected = Expected(Rz90, new Vector3Mm(65.659552, 48.485654, 0));
        AssertTransform(expected, result.Transform!.Value);
        Assert.False(result.Transform.Value[0, 3].Equals(67.4 + TopOffset.X));
    }

    [Theory]
    [InlineData(23.0, 37.0, 41.0, 1.0, 0.0, 23.085654, 54.540448, 1.0)]
    [InlineData(23.0, 37.0, 41.0, 1.0, 90.0, 65.659552, 48.485654, 1.0)]
    [InlineData(-17.0, 29.0, -53.0, -1.0, 0.0, 103.085654, 54.540448, -1.0)]
    [InlineData(-17.0, 29.0, -53.0, -1.0, -90.0, 149.140448, 57.114346, -1.0)]
    public void Top_CompoundStateAndRotation_MatchR4ControlData(
        double rx,
        double ry,
        double rz,
        double dz,
        double rotation,
        double expectedX,
        double expectedY,
        double expectedZ)
    {
        var position = rotation switch
        {
            0 when rz > 0 => new Vector2Mm(27.4, 52.8),
            90 => new Vector2Mm(67.4, 52.8),
            0 => new Vector2Mm(107.4, 52.8),
            _ => new Vector2Mm(147.4, 52.8),
        };

        var result = PlacementTransformBuilder.Build(CreateRequest(
            position,
            rotation,
            state: new ModelStateInput(rx, ry, rz, dz),
            calibration: Calibration));

        Assert.True(result.IsSupported);

        var stateRotation = Matrix4x4D.Multiply(
            Matrix4x4D.Multiply(
                Matrix4x4D.CreateRotationZ(rz),
                Matrix4x4D.CreateRotationY(ry)),
            Matrix4x4D.CreateRotationX(rx));
        var baseTransform = Expected(ToArray(stateRotation), new Vector3Mm(
            position.X + TopOffset.X,
            position.Y + TopOffset.Y,
            dz));
        var pivot = Matrix4x4D.CreateTranslation(position.X, position.Y, 0);
        var expected = Matrix4x4D.Multiply(
            Matrix4x4D.Multiply(
                Matrix4x4D.Multiply(pivot, Matrix4x4D.CreateRotationZ(rotation)),
                Matrix4x4D.CreateTranslation(-position.X, -position.Y, 0)),
            baseTransform);

        AssertTransform(expected, result.Transform!.Value);
        Assert.True(result.Transform.Value.TransformPoint(Vector3Mm.Zero)
            .NearlyEquals(new Vector3Mm(expectedX, expectedY, expectedZ), TranslationToleranceMm));
    }

    [Fact]
    public void Top_ArbitraryRotationIsSupported()
    {
        var position = new Vector2Mm(50, 60);
        var result = PlacementTransformBuilder.Build(CreateRequest(position, 37, calibration: Calibration));

        Assert.True(result.IsSupported);

        var pivot = Matrix4x4D.CreateTranslation(position.X, position.Y, 0);
        var expected = Matrix4x4D.Multiply(
            Matrix4x4D.Multiply(
                Matrix4x4D.Multiply(pivot, Matrix4x4D.CreateRotationZ(37)),
                Matrix4x4D.CreateTranslation(-position.X, -position.Y, 0)),
            Matrix4x4D.CreateTranslation(
                position.X + TopOffset.X,
                position.Y + TopOffset.Y,
                0));

        AssertTransform(expected, result.Transform!.Value);
    }

    [Fact]
    public void Top_AssetOriginIsSubtractedInModelSpace()
    {
        var assetOrigin = new Vector3Mm(1, 2, 3);
        var result = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(27.4, 52.8),
            0,
            calibration: Calibration,
            assetOrigin: assetOrigin));

        Assert.True(result.IsSupported);
        AssertTransform(
            Expected(Identity, new Vector3Mm(23.085654 - 1, 54.540448 - 2, -3)),
            result.Transform!.Value);
    }

    [Fact]
    public void Top_PlacedBodyEffectiveRemovesDoubleRotation()
    {
        var source = new ModelStateInput(23, 37, 41, 1);
        var placed = new ModelStateInput(23, 37, 131, 1);
        var position = new Vector2Mm(67.4, 52.8);

        var sourceResult = PlacementTransformBuilder.Build(CreateRequest(
            position, 90, state: source, calibration: Calibration));
        var placedResult = PlacementTransformBuilder.Build(CreateRequest(
            position, 90, state: placed, source: ModelStateSource.PlacedBodyEffective, calibration: Calibration));

        Assert.True(sourceResult.IsSupported);
        Assert.True(placedResult.IsSupported);
        Assert.True(placedResult.NormalizedModelState.NearlyEquals(source));
        AssertTransform(sourceResult.Transform!.Value, placedResult.Transform!.Value);
    }

    // ---------------------------------------------------------------------
    // Bottom placement
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(0.0, 26.456969, 94.540448, -2.028002, 0)]
    [InlineData(90.0, 69.030867, 88.485654, -2.028002, 1)]
    [InlineData(180.0, 115.085661, 91.059552, -2.028002, 2)]
    [InlineData(-90.0, 152.511763, 97.114346, -2.028002, 3)]
    public void Bottom_Rotation0To270_MatchCandidateC(
        double rotation,
        double expectedX,
        double expectedY,
        double expectedZ,
        int rotationIndex)
    {
        var positions = new[]
        {
            new Vector2Mm(27.4, 92.8),
            new Vector2Mm(67.4, 92.8),
            new Vector2Mm(107.4, 92.8),
            new Vector2Mm(147.4, 92.8),
        };
        var expectedRotations = new[] { Rx180, Rz90Rx180, Rz180Rx180, RzMinus90Rx180 };

        var result = PlacementTransformBuilder.Build(CreateRequest(
            positions[rotationIndex],
            rotation,
            side: ComponentSide.Bottom,
            projection: BodyProjection.Bottom,
            calibration: Calibration));

        Assert.True(result.IsSupported);
        AssertTransform(
            Expected(expectedRotations[rotationIndex], new Vector3Mm(expectedX, expectedY, expectedZ)),
            result.Transform!.Value);
        Assert.Equal(1.0, result.Transform.Value.Determinant3x3(), precision: 12);
    }

    [Fact]
    public void Bottom_PlacedBodyEffectiveNormalizesRawRz()
    {
        var sourceResult = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(67.4, 92.8),
            90,
            side: ComponentSide.Bottom,
            projection: BodyProjection.Bottom,
            calibration: Calibration));

        var placedResult = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(67.4, 92.8),
            90,
            side: ComponentSide.Bottom,
            projection: BodyProjection.Bottom,
            state: new ModelStateInput(0, 0, 270, 0),
            source: ModelStateSource.PlacedBodyEffective,
            calibration: Calibration));

        Assert.True(sourceResult.IsSupported);
        Assert.True(placedResult.IsSupported);
        Assert.True(placedResult.NormalizedModelState.IsZero());
        AssertTransform(sourceResult.Transform!.Value, placedResult.Transform!.Value);
    }

    [Fact]
    public void Bottom_MissingCalibrationIsExplicit()
    {
        var noCalibration = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(27.4, 92.8),
            0,
            side: ComponentSide.Bottom,
            projection: BodyProjection.Bottom));

        AssertUnsupported(noCalibration, PlacementDiagnosticCode.MissingFootprintCalibration);

        var onlyTop = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(27.4, 92.8),
            0,
            side: ComponentSide.Bottom,
            projection: BodyProjection.Bottom,
            calibration: new FootprintOriginCalibration(TopOffset, null)));

        AssertUnsupported(onlyTop, PlacementDiagnosticCode.MissingFootprintCalibration);

        var onlyBottom = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(27.4, 92.8),
            0,
            side: ComponentSide.Bottom,
            projection: BodyProjection.Bottom,
            calibration: new FootprintOriginCalibration(null, BottomLocalOffset)));

        AssertUnsupported(onlyBottom, PlacementDiagnosticCode.MissingFootprintCalibration);
    }

    [Fact]
    public void ProjectionMismatch_BottomComponentNeedsOnlyBottomOrigin()
    {
        var result = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(147.4, 82.8),
            0,
            side: ComponentSide.Bottom,
            projection: BodyProjection.Top,
            calibration: new FootprintOriginCalibration(null, BottomLocalOffset)));

        Assert.True(result.IsSupported);
        AssertTransform(
            Expected(Identity, new Vector3Mm(146.456969, 84.540448, 0)),
            result.Transform!.Value);
    }

    [Fact]
    public void Bottom_CompoundAndUnsupportedAnglesAreRejected()
    {
        var compound = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(27.4, 92.8),
            0,
            side: ComponentSide.Bottom,
            projection: BodyProjection.Bottom,
            state: new ModelStateInput(5, 0, 0, 0),
            calibration: Calibration));
        AssertUnsupported(compound, PlacementDiagnosticCode.UnsupportedCombination);

        var angle = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(27.4, 92.8),
            45,
            side: ComponentSide.Bottom,
            projection: BodyProjection.Bottom,
            calibration: Calibration));
        AssertUnsupported(angle, PlacementDiagnosticCode.UnsupportedCombination);
    }

    // ---------------------------------------------------------------------
    // BodyProjection and mismatch
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(ComponentSide.Top, BodyProjection.Top, 27.4, 82.8, 23.085654, 84.540448, 0.0, 0)]
    [InlineData(ComponentSide.Top, BodyProjection.Bottom, 67.4, 82.8, 63.085654, 84.540448, -2.028002, 1)]
    [InlineData(ComponentSide.Bottom, BodyProjection.Bottom, 107.4, 82.8, 106.456969, 84.540448, -2.028002, 2)]
    [InlineData(ComponentSide.Bottom, BodyProjection.Top, 147.4, 82.8, 146.456969, 84.540448, 0.0, 3)]
    public void Projection_AllFourCombinationsUseCorrectZAndOrientation(
        ComponentSide side,
        BodyProjection projection,
        double x,
        double y,
        double expectedX,
        double expectedY,
        double expectedZ,
        int rotationIndex)
    {
        var expectedRotations = new[] { Identity, Rx180, Rx180, Identity };

        var result = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(x, y),
            0,
            side: side,
            projection: projection,
            calibration: Calibration));

        Assert.True(result.IsSupported);
        AssertTransform(
            Expected(expectedRotations[rotationIndex], new Vector3Mm(expectedX, expectedY, expectedZ)),
            result.Transform!.Value);
    }

    [Fact]
    public void Projection_StandoffMovesBodyFromTheSelectedSurface()
    {
        var top = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(27.4, 82.8),
            0,
            side: ComponentSide.Top,
            projection: BodyProjection.Top,
            calibration: Calibration,
            standoff: 0.5));
        Assert.True(top.IsSupported);
        Assert.Equal(0.5, top.Transform!.Value[2, 3], precision: 9);

        var bottom = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(67.4, 82.8),
            0,
            side: ComponentSide.Top,
            projection: BodyProjection.Bottom,
            calibration: Calibration,
            standoff: 0.5));
        Assert.True(bottom.IsSupported);
        Assert.Equal(-2.528002, bottom.Transform!.Value[2, 3], precision: 6);
    }

    [Fact]
    public void ProjectionMismatch_RejectsNonZeroStateOrRotation()
    {
        var rotated = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(67.4, 82.8),
            90,
            side: ComponentSide.Top,
            projection: BodyProjection.Bottom,
            calibration: Calibration));
        AssertUnsupported(rotated, PlacementDiagnosticCode.UnsupportedCombination);

        var withState = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(147.4, 82.8),
            0,
            side: ComponentSide.Bottom,
            projection: BodyProjection.Top,
            state: new ModelStateInput(0, 0, 5, 0),
            calibration: Calibration));
        AssertUnsupported(withState, PlacementDiagnosticCode.UnsupportedCombination);
    }

    // ---------------------------------------------------------------------
    // Capability guards and validation
    // ---------------------------------------------------------------------

    [Fact]
    public void NativeAndExternalModelsAreRejected()
    {
        var native = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(27.4, 52.8), 0, calibration: Calibration,
            representation: ModelRepresentation.Native));
        AssertUnsupported(native, PlacementDiagnosticCode.UnsupportedModelRepresentation);

        var external = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(27.4, 52.8), 0, calibration: Calibration,
            origin: ModelSourceOrigin.External));
        AssertUnsupported(external, PlacementDiagnosticCode.UnsupportedModelSource);
    }

    [Fact]
    public void Top_MissingCalibrationIsExplicit()
    {
        var result = PlacementTransformBuilder.Build(CreateRequest(new Vector2Mm(27.4, 52.8), 0));
        AssertUnsupported(result, PlacementDiagnosticCode.MissingFootprintCalibration);
    }

    [Fact]
    public void InvalidBoardSurfaceFrameIsExplicit()
    {
        var result = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(27.4, 52.8),
            0,
            calibration: Calibration,
            topZ: -1,
            bottomZ: 0));
        AssertUnsupported(result, PlacementDiagnosticCode.InvalidRequest);
    }

    [Fact]
    public void NonFiniteRotationAndStandoffAreExplicit()
    {
        var rotation = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(27.4, 52.8),
            double.NaN,
            calibration: Calibration));
        AssertUnsupported(rotation, PlacementDiagnosticCode.InvalidRequest);

        var standoff = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(27.4, 52.8),
            0,
            calibration: Calibration,
            standoff: double.PositiveInfinity));
        AssertUnsupported(standoff, PlacementDiagnosticCode.InvalidRequest);
    }

    [Fact]
    public void GeometricLandmarks_MatchExpectedTransformWithinTolerance()
    {
        var result = PlacementTransformBuilder.Build(CreateRequest(
            new Vector2Mm(67.4, 52.8),
            90,
            calibration: Calibration));
        Assert.True(result.IsSupported);

        var expected = Expected(Rz90, new Vector3Mm(65.659552, 48.485654, 0));
        AssertGeometry(expected, result.Transform!.Value);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static PlacementTransformRequest CreateRequest(
        Vector2Mm position,
        double rotation = 0,
        ComponentSide side = ComponentSide.Top,
        BodyProjection projection = BodyProjection.Top,
        ModelStateInput? state = null,
        ModelStateSource source = ModelStateSource.SourceFootprint,
        FootprintOriginCalibration? calibration = null,
        double topZ = 0,
        double bottomZ = -2.028002,
        double standoff = 0,
        Vector3Mm? assetOrigin = null,
        ModelRepresentation representation = ModelRepresentation.GenericEmbedded,
        ModelSourceOrigin origin = ModelSourceOrigin.Embedded) =>
        new()
        {
            ComponentPositionBoardMm = position,
            ComponentRotationDeg = rotation,
            ComponentSide = side,
            BodyProjection = projection,
            FootprintCalibration = calibration,
            AssetOriginLocalMm = assetOrigin ?? Vector3Mm.Zero,
            ModelState = state ?? ModelStateInput.Zero,
            ModelStateSource = source,
            BoardSurfaceFrame = new BoardSurfaceFrame(topZ, bottomZ),
            StandoffHeightMm = standoff,
            ModelRepresentation = representation,
            ModelSourceOrigin = origin,
        };

    private static Matrix4x4D Expected(double[] rotation, Vector3Mm translation) =>
        new(
            rotation[0], rotation[1], rotation[2], translation.X,
            rotation[3], rotation[4], rotation[5], translation.Y,
            rotation[6], rotation[7], rotation[8], translation.Z,
            0, 0, 0, 1);

    private static double[] ToArray(Matrix4x4D matrix) =>
        new[]
        {
            matrix[0, 0], matrix[0, 1], matrix[0, 2],
            matrix[1, 0], matrix[1, 1], matrix[1, 2],
            matrix[2, 0], matrix[2, 1], matrix[2, 2],
        };

    private static void AssertTransform(Matrix4x4D expected, Matrix4x4D actual)
    {
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                Assert.InRange(
                    Math.Abs(expected[row, column] - actual[row, column]),
                    0,
                    RotationTolerance);
            }

            Assert.InRange(
                Math.Abs(expected[row, 3] - actual[row, 3]),
                0,
                TranslationToleranceMm);
        }

        Assert.Equal(0.0, actual[3, 0], precision: 12);
        Assert.Equal(0.0, actual[3, 1], precision: 12);
        Assert.Equal(0.0, actual[3, 2], precision: 12);
        Assert.Equal(1.0, actual[3, 3], precision: 12);
    }

    private static void AssertGeometry(Matrix4x4D expected, Matrix4x4D actual)
    {
        var landmarks = new[]
        {
            new Vector3Mm(0, 0, 0),
            new Vector3Mm(12, 0, 0),
            new Vector3Mm(0, 7, 0),
            new Vector3Mm(0, 0, 3),
            new Vector3Mm(12, 7, 3),
            new Vector3Mm(12, 0, 3),
            new Vector3Mm(0, 7, 3),
            new Vector3Mm(12, 7, 0),
        };

        foreach (var landmark in landmarks)
        {
            var expectedPoint = expected.TransformPoint(landmark);
            var actualPoint = actual.TransformPoint(landmark);

            Assert.InRange(
                (expectedPoint - actualPoint).Length,
                0,
                GeometryToleranceMm);
        }
    }

    private static void AssertUnsupported(
        PlacementTransformResult result,
        PlacementDiagnosticCode expectedCode)
    {
        Assert.False(result.IsSupported);
        Assert.Null(result.Transform);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
    }
}
