using System.IO;
using DXP;

namespace EcadMcadBridge.Altium.Application;

internal sealed record AltiumDocumentState(
    string FilePath,
    bool IsModified,
    string DocumentKind);

internal static class AltiumDocumentStateReader
{
    public static bool TryRead(
        IServerDocument? document,
        out AltiumDocumentState? state,
        out string message)
    {
        state = null;
        if (document is null)
        {
            message = "No active document.";
            return false;
        }

        var kind = document.GetKind();
        if (!string.Equals(kind, "PCB", StringComparison.OrdinalIgnoreCase))
        {
            message = "The active document is not a PCB document.";
            return false;
        }

        var path = document.GetFileName();
        if (string.IsNullOrWhiteSpace(path))
        {
            message = "The active PCB has no saved file path; save the PCB and retry the export.";
            return false;
        }

        if (!Path.IsPathRooted(path))
        {
            message = "The active PCB path is not absolute; save the PCB and retry the export.";
            return false;
        }

        if (!File.Exists(path))
        {
            message = "The active PCB file does not exist or is not readable.";
            return false;
        }

        state = new AltiumDocumentState(path, document.GetModified(), kind);
        message = string.Empty;
        return true;
    }

    public static bool TryRecheck(
        IServerDocument? document,
        AltiumDocumentState before,
        out string message)
    {
        if (!TryRead(document, out var after, out message) || after is null)
            return false;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(
                Path.GetFullPath(before.FilePath),
                Path.GetFullPath(after.FilePath),
                comparison))
        {
            message = "The active PCB path changed during export.";
            return false;
        }

        if (after.IsModified)
        {
            message = "The active PCB has unsaved changes; save it and retry the export.";
            return false;
        }

        message = string.Empty;
        return true;
    }
}
