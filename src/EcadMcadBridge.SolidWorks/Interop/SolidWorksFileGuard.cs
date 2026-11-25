using System.IO;
using SolidWorks.Interop.sldworks;

namespace EcadMcadBridge.SolidWorks.Interop;

/// <summary>No-overwrite and SaveAs result guards.</summary>
public static class SolidWorksFileGuard
{
    public static bool TryValidateNewTarget(
        string path,
        ISldWorks solidWorks,
        out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "Target path is empty.";
            return false;
        }

        if (File.Exists(path))
        {
            reason = "Target file already exists.";
            return false;
        }

        if (Directory.Exists(path))
        {
            reason = "Target path is a directory.";
            return false;
        }

        try
        {
            if (solidWorks.GetOpenDocument(path) is not null)
            {
                reason = "Target document is already open in SOLIDWORKS.";
                return false;
            }
        }
        catch (Exception ex)
        {
            reason = "Target open state could not be verified: " + ex.GetType().Name + ".";
            return false;
        }

        return true;
    }

    public static bool IsSaveSuccessful(
        bool returned,
        int errors,
        string path) =>
        returned
        && errors == 0
        && File.Exists(path)
        && new FileInfo(path).Length > 0;
}
