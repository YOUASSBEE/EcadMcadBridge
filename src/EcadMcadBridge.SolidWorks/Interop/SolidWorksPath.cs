using System.IO;

namespace EcadMcadBridge.SolidWorks.Interop;

public static class SolidWorksPath
{
    /// <summary>Normalizes a Windows path for identity comparison; null means no path.</summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    public static bool Same(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}
