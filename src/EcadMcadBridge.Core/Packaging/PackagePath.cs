namespace EcadMcadBridge.Core.Packaging;

internal static class PackagePath
{
    public static bool IsSafeRelative(string? path, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "path is empty";
            return false;
        }

        if (path.Contains("\\", StringComparison.Ordinal)
            || path.Contains(":", StringComparison.Ordinal)
            || path.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(path))
        {
            reason = "path must be a relative POSIX path";
            return false;
        }

        var segments = path.Split('/');
        if (segments.Length == 0)
        {
            reason = "path has no segments";
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                reason = "path contains an empty, '.' or '..' segment";
                return false;
            }
        }

        return true;
    }

    public static bool TryGetFullPath(
        string packageRoot,
        string relativePath,
        out string fullPath,
        out string reason)
    {
        fullPath = string.Empty;
        if (!IsSafeRelative(relativePath, out reason))
            return false;

        var root = Path.GetFullPath(packageRoot);
        var candidate = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(prefix, comparison))
        {
            reason = "path escapes the package directory";
            return false;
        }

        fullPath = candidate;
        return true;
    }
}
