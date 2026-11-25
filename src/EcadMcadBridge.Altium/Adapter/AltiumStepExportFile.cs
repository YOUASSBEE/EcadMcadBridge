using System.IO;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>Validates the per-body STEP file without parsing geometry.</summary>
internal static class AltiumStepExportFile
{
    public const string Extension = ".step";

    public static bool IsValid(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (!string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort: a failed export must not leave a file for the package.
        }
    }
}
