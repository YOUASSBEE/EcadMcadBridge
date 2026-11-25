using EcadMcadBridge.Core.Placement;

namespace EcadMcadBridge.SolidWorks.Placement;

/// <summary>Residuals measured after a SOLIDWORKS occurrence transform readback.</summary>
public sealed record SolidWorksTransformResiduals(
    double TranslationMm,
    double RotationBasisMax,
    double Determinant);

/// <summary>
/// Converts the Core column-vector matrix into the SOLIDWORKS MathTransform ArrayData layout:
/// row-major R^T in 0..8, translation in metres in 9..11, 1 in 12, zeros in 13..15.
/// </summary>
public static class SolidWorksTransformConverter
{
    public static bool TryToArrayData(
        Matrix4x4D matrix,
        out double[] arrayData,
        out string error)
    {
        arrayData = Array.Empty<double>();
        if (!matrix.TryInvertRigid(out _))
        {
            error = "The Core matrix is not a finite rigid transform with determinant +1.";
            return false;
        }

        var values = new double[16];
        values[0] = matrix[0, 0];
        values[1] = matrix[1, 0];
        values[2] = matrix[2, 0];
        values[3] = matrix[0, 1];
        values[4] = matrix[1, 1];
        values[5] = matrix[2, 1];
        values[6] = matrix[0, 2];
        values[7] = matrix[1, 2];
        values[8] = matrix[2, 2];
        values[9] = matrix[0, 3] / 1000.0;
        values[10] = matrix[1, 3] / 1000.0;
        values[11] = matrix[2, 3] / 1000.0;
        values[12] = 1.0;
        values[13] = 0.0;
        values[14] = 0.0;
        values[15] = 0.0;

        if (values.Any(value => !double.IsFinite(value)))
        {
            error = "The converted MathTransform contains a non-finite value.";
            return false;
        }

        arrayData = values;
        error = string.Empty;
        return true;
    }

    public static bool TryFromArrayData(
        IReadOnlyList<double> data,
        out Matrix4x4D matrix,
        out string error)
    {
        matrix = default;
        if (data.Count < 16)
        {
            error = "MathTransform.ArrayData must contain 16 values.";
            return false;
        }

        if (data.Any(value => !double.IsFinite(value)))
        {
            error = "MathTransform.ArrayData contains a non-finite value.";
            return false;
        }

        if (Math.Abs(data[12] - 1.0) > 1e-9
            || Math.Abs(data[13]) > 1e-9
            || Math.Abs(data[14]) > 1e-9
            || Math.Abs(data[15]) > 1e-9)
        {
            error = "MathTransform.ArrayData has an unsupported scale or affine bottom row.";
            return false;
        }

        matrix = new Matrix4x4D(
            data[0], data[3], data[6], data[9] * 1000.0,
            data[1], data[4], data[7], data[10] * 1000.0,
            data[2], data[5], data[8], data[11] * 1000.0,
            0, 0, 0, 1);

        if (!matrix.TryInvertRigid(out _))
        {
            error = "The MathTransform is not a finite rigid transform with determinant +1.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static SolidWorksTransformResiduals Measure(
        Matrix4x4D expected,
        Matrix4x4D actual)
    {
        var dx = expected[0, 3] - actual[0, 3];
        var dy = expected[1, 3] - actual[1, 3];
        var dz = expected[2, 3] - actual[2, 3];
        var translation = Math.Sqrt(dx * dx + dy * dy + dz * dz);

        var rotation = 0.0;
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                rotation = Math.Max(rotation, Math.Abs(expected[row, column] - actual[row, column]));
            }
        }

        return new SolidWorksTransformResiduals(
            translation,
            rotation,
            actual.Determinant3x3());
    }
}
