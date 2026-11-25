using EcadMcadBridge.Core.Diagnostics;

namespace EcadMcadBridge.Core.Placement;

/// <summary>
/// Pure deterministic builder that converts an Altium 3D body into board coordinates.
/// It never reads files, SDK objects or registry data and never guesses missing calibration.
/// </summary>
public static class PlacementTransformBuilder
{
    private const double Tolerance = 1e-9;

    /// <summary>Builds a placement transform or returns an explicit unsupported result.</summary>
    public static PlacementTransformResult Build(PlacementTransformRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!double.IsFinite(request.ComponentRotationDeg) || !double.IsFinite(request.StandoffHeightMm))
        {
            return Unsupported(
                request.ModelState,
                PlacementDiagnosticCode.InvalidRequest,
                "Component rotation and standoff must be finite.");
        }

        if (request.BoardSurfaceFrame is null ||
            !double.IsFinite(request.BoardSurfaceFrame.TopSurfaceZMm) ||
            !double.IsFinite(request.BoardSurfaceFrame.BottomSurfaceZMm) ||
            request.BoardSurfaceFrame.TopSurfaceZMm <= request.BoardSurfaceFrame.BottomSurfaceZMm)
        {
            return Unsupported(
                request.ModelState,
                PlacementDiagnosticCode.InvalidRequest,
                "BoardSurfaceFrame requires finite surfaces with TopSurfaceZMm > BottomSurfaceZMm.");
        }

        if (request.ModelRepresentation != ModelRepresentation.GenericEmbedded)
        {
            return Unsupported(
                request.ModelState,
                PlacementDiagnosticCode.UnsupportedModelRepresentation,
                "Only generic embedded 3D bodies are supported by the first placement version.");
        }

        if (request.ModelSourceOrigin != ModelSourceOrigin.Embedded)
        {
            return Unsupported(
                request.ModelState,
                PlacementDiagnosticCode.UnsupportedModelSource,
                "Only embedded model assets are supported by the first placement version.");
        }

        if (!Enum.IsDefined(request.ComponentSide) ||
            !Enum.IsDefined(request.BodyProjection) ||
            !Enum.IsDefined(request.ModelStateSource))
        {
            return Unsupported(
                request.ModelState,
                PlacementDiagnosticCode.InvalidRequest,
                "Component side, body projection and model-state source must be defined values.");
        }

        var normalized = Normalize(request.ModelState, request.ComponentSide, request.ModelStateSource, request.ComponentRotationDeg);

        try
        {
            if (request.ComponentSide == ComponentSide.Top && request.BodyProjection == BodyProjection.Top)
                return BuildTop(request, normalized);

            if (request.ComponentSide == ComponentSide.Bottom && request.BodyProjection == BodyProjection.Bottom)
                return BuildBottom(request, normalized);

            return BuildProjectionMismatch(request, normalized);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Unsupported(
                normalized,
                PlacementDiagnosticCode.InvalidRequest,
                "Placement values must be finite: " + ex.Message);
        }
    }

    /// <summary>
    /// Removes or adds component rotation so <see cref="ModelStateInput.Rz"/> represents the
    /// footprint state exactly once.
    /// </summary>
    public static ModelStateInput Normalize(
        ModelStateInput raw,
        ComponentSide side,
        ModelStateSource source,
        double componentRotationDeg)
    {
        if (!double.IsFinite(componentRotationDeg))
            throw new ArgumentOutOfRangeException(nameof(componentRotationDeg), "Rotation must be finite.");

        if (source == ModelStateSource.SourceFootprint)
            return raw;

        var rz = side == ComponentSide.Top
            ? raw.Rz - componentRotationDeg
            : raw.Rz + componentRotationDeg;

        return new ModelStateInput(raw.Rx, raw.Ry, AngleNormalizer.NormalizeDegrees(rz), raw.DzMm);
    }

    private static PlacementTransformResult BuildTop(
        PlacementTransformRequest request,
        ModelStateInput state)
    {
        var offset = request.FootprintCalibration?.TopModelOriginOffsetBoardMm;
        if (offset is null)
            return MissingCalibration(state, "Top model-origin calibration is required.");

        var position = request.ComponentPositionBoardMm;
        var frame = request.BoardSurfaceFrame;
        var asset = request.AssetOriginLocalMm;

        var baseTransform = Matrix4x4D.Multiply(
            Matrix4x4D.Multiply(
                Matrix4x4D.Multiply(
                    Matrix4x4D.CreateTranslation(
                        position.X + offset.Value.X,
                        position.Y + offset.Value.Y,
                        frame.TopSurfaceZMm + request.StandoffHeightMm),
                    Matrix4x4D.CreateTranslation(0, 0, state.DzMm)),
                StateRotation(state)),
            Matrix4x4D.CreateTranslation(-asset.X, -asset.Y, -asset.Z));

        var pivot = Matrix4x4D.CreateTranslation(position.X, position.Y, 0);
        var transform = Matrix4x4D.Multiply(
            Matrix4x4D.Multiply(
                Matrix4x4D.Multiply(
                    pivot,
                    Matrix4x4D.CreateRotationZ(request.ComponentRotationDeg)),
                Matrix4x4D.CreateTranslation(-position.X, -position.Y, 0)),
            baseTransform);

        return Complete(transform, state);
    }

    private static PlacementTransformResult BuildBottom(
        PlacementTransformRequest request,
        ModelStateInput state)
    {
        if (!IsZeroState(state))
        {
            return Unsupported(
                state,
                PlacementDiagnosticCode.UnsupportedCombination,
                "Bottom compound model state is not supported in the first placement version.");
        }

        var bottomLocal = request.FootprintCalibration?.BottomModelOriginOffsetLocalMm;
        var topOffset = request.FootprintCalibration?.TopModelOriginOffsetBoardMm;
        var bottomBoard = request.FootprintCalibration?.BottomModelOriginOffsetBoardMm;
        var pivotOffset = request.FootprintCalibration?.BottomPivotOffsetMm;
        if (bottomLocal is null || topOffset is null || bottomBoard is null || pivotOffset is null)
            return MissingCalibration(state, "Both Top and Bottom model-origin calibration are required for Bottom rotation.");

        if (!IsQuarterTurn(request.ComponentRotationDeg))
        {
            return Unsupported(
                state,
                PlacementDiagnosticCode.UnsupportedCombination,
                "Bottom rotation is supported only for 0, 90, 180 and -90 degrees.");
        }

        var position = request.ComponentPositionBoardMm;
        var frame = request.BoardSurfaceFrame;
        var asset = request.AssetOriginLocalMm;

        var baseTransform = Matrix4x4D.Multiply(
            Matrix4x4D.Multiply(
                Matrix4x4D.CreateTranslation(
                    position.X + bottomBoard.Value.X,
                    position.Y + bottomBoard.Value.Y,
                    frame.BottomSurfaceZMm - request.StandoffHeightMm),
                Matrix4x4D.CreateRotationX(180)),
            Matrix4x4D.CreateTranslation(-asset.X, -asset.Y, -asset.Z));

        var pivotX = position.X + pivotOffset.Value.X;
        var pivotY = position.Y + pivotOffset.Value.Y;
        var transform = Matrix4x4D.Multiply(
            Matrix4x4D.Multiply(
                Matrix4x4D.Multiply(
                    Matrix4x4D.CreateTranslation(pivotX, pivotY, 0),
                    Matrix4x4D.CreateRotationZ(request.ComponentRotationDeg)),
                Matrix4x4D.CreateTranslation(-pivotX, -pivotY, 0)),
            baseTransform);

        return Complete(transform, state);
    }

    private static PlacementTransformResult BuildProjectionMismatch(
        PlacementTransformRequest request,
        ModelStateInput state)
    {
        if (!IsZeroRotation(request.ComponentRotationDeg) || !IsZeroState(state))
        {
            return Unsupported(
                state,
                PlacementDiagnosticCode.UnsupportedCombination,
                "Projection mismatch is supported only with zero component rotation and zero model state.");
        }

        var offset = request.ComponentSide == ComponentSide.Top
            ? request.FootprintCalibration?.TopModelOriginOffsetBoardMm
            : request.FootprintCalibration?.BottomModelOriginOffsetBoardMm;
        if (offset is null)
            return MissingCalibration(state, "The model-origin offset for the component side is required.");

        var position = request.ComponentPositionBoardMm;
        var frame = request.BoardSurfaceFrame;
        var asset = request.AssetOriginLocalMm;
        var isTopProjection = request.BodyProjection == BodyProjection.Top;
        var bodyOriginZ = isTopProjection
            ? frame.TopSurfaceZMm + request.StandoffHeightMm
            : frame.BottomSurfaceZMm - request.StandoffHeightMm;
        var projection = isTopProjection
            ? Matrix4x4D.Identity
            : Matrix4x4D.CreateRotationX(180);

        var transform = Matrix4x4D.Multiply(
            Matrix4x4D.Multiply(
                Matrix4x4D.CreateTranslation(
                    position.X + offset.Value.X,
                    position.Y + offset.Value.Y,
                    bodyOriginZ),
                projection),
            Matrix4x4D.CreateTranslation(-asset.X, -asset.Y, -asset.Z));

        return Complete(transform, state);
    }

    private static Matrix4x4D StateRotation(ModelStateInput state) =>
        Matrix4x4D.Multiply(
            Matrix4x4D.Multiply(
                Matrix4x4D.CreateRotationZ(state.Rz),
                Matrix4x4D.CreateRotationY(state.Ry)),
            Matrix4x4D.CreateRotationX(state.Rx));

    private static PlacementTransformResult Complete(Matrix4x4D transform, ModelStateInput state)
    {
        if (!transform.TryInvertRigid(out _))
        {
            return Unsupported(
                state,
                PlacementDiagnosticCode.DegenerateTransform,
                "The computed matrix is not a finite rigid transform with determinant +1.");
        }

        return PlacementTransformResult.Supported(transform, state);
    }

    private static PlacementTransformResult MissingCalibration(
        ModelStateInput state,
        string message) =>
        PlacementTransformResult.Unsupported(
            state,
            new PlacementDiagnostic(PlacementDiagnosticCode.MissingFootprintCalibration, message));

    private static bool IsZeroState(ModelStateInput state) =>
        Math.Abs(state.Rx) <= Tolerance &&
        Math.Abs(state.Ry) <= Tolerance &&
        Math.Abs(AngleNormalizer.NormalizeDegrees(state.Rz)) <= Tolerance &&
        Math.Abs(state.DzMm) <= Tolerance;

    private static PlacementTransformResult Unsupported(
        ModelStateInput state,
        PlacementDiagnosticCode code,
        string message) =>
        PlacementTransformResult.Unsupported(state, new PlacementDiagnostic(code, message));

    private static bool IsZeroRotation(double degrees) =>
        Math.Abs(AngleNormalizer.NormalizeDegrees(degrees)) <= Tolerance;

    private static bool IsQuarterTurn(double degrees)
    {
        var normalized = AngleNormalizer.NormalizeDegrees(degrees);
        return Math.Abs(normalized) <= Tolerance ||
               Math.Abs(normalized - 90) <= Tolerance ||
               Math.Abs(normalized + 90) <= Tolerance ||
               Math.Abs(Math.Abs(normalized) - 180) <= Tolerance;
    }
}
