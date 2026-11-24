namespace EcadMcadBridge.Core.Packaging;

public sealed record PackageRootResult(bool IsSuccess, string? Root, string Message);

/// <summary>Resolves the runtime package root without embedding user paths in source.</summary>
public static class PackageRootResolver
{
    public const string EnvironmentVariable = "ECAD_MCAD_BRIDGE_PACKAGE_ROOT";

    public static PackageRootResult Resolve(
        string? environmentValue = null,
        string? localAppData = null)
    {
        var configured = environmentValue ?? Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathRooted(configured))
                return new(false, null, "Package root must be an absolute path.");

            try
            {
                return new(true, Path.GetFullPath(configured), "Configured package root.");
            }
            catch (Exception ex)
            {
                return new(false, null, "Package root is invalid: " + ex.GetType().Name);
            }
        }

        var local = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            return new(false, null, "LocalApplicationData is unavailable and no package root is configured.");

        try
        {
            return new(
                true,
                Path.Combine(Path.GetFullPath(local), "EcadMcadBridge", "Packages"),
                "Default package root.");
        }
        catch (Exception ex)
        {
            return new(false, null, "Default package root is invalid: " + ex.GetType().Name);
        }
    }
}
