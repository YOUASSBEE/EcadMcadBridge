using System.IO;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>Creates and removes the temporary per-body STEP export directory.</summary>
internal static class AltiumModelExportWorkspace
{
    public static string Create()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "EcadMcadBridge",
            "model-export",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void TryDelete(string? path, ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            warnings.Add(
                "The temporary model export directory could not be removed: " +
                ex.GetType().Name + ".");
        }
    }
}
