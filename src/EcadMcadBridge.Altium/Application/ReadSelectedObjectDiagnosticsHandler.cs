using System.Threading;
using DXP;

namespace EcadMcadBridge.Altium.Application;

internal static class ReadSelectedObjectDiagnosticsHandler
{
    public const string CommandName = "ReadSelectedObjectDiagnostics";
    private const string Caption = "EcadMcadBridge";

    public static void Execute(IServerDocumentView view, ref string parameters)
    {
        WriteThreadTrace("ENTER", null);
        try
        {
            var report = SelectedObjectDiagnosticsReader.Read(view);
            var path = SelectedObjectDiagnosticsWriter.Write(report);
            BridgeLogger.Write(
                "INFO",
                "SELECTED_DIAGNOSTICS_OK",
                "objects=" + report.ObjectCount + " status=" + report.ReadStatus.Status);
            WriteThreadTrace(
                "EXIT",
                "objects=" + report.ObjectCount + " status=" + report.ReadStatus.Status);
            DXP.Utils.ShowInfo("Selected-object diagnostics written to:\n" + path, Caption);
        }
        catch (Exception ex)
        {
            BridgeLogger.Write("ERROR", "E_SELECTED_DIAGNOSTICS_FAILED", ex.GetType().Name);
            WriteThreadTrace("EXIT", "error=" + ex.GetType().Name);
            DXP.Utils.ShowWarning("Selected-object diagnostics failed: " + ex.GetType().Name + ".", Caption);
        }
    }

    internal static string FormatThreadTrace(
        string phase,
        int threadId,
        ApartmentState apartmentState,
        bool isThreadPoolThread,
        string? detail)
    {
        var trace = "phase=" + phase +
            " threadId=" + threadId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            " apartment=" + apartmentState +
            " threadPool=" + (isThreadPoolThread ? "true" : "false");
        return detail is null ? trace : trace + " " + detail;
    }

    private static void WriteThreadTrace(string phase, string? detail) =>
        BridgeLogger.Write(
            "INFO",
            "SELECTED_DIAGNOSTICS_THREAD",
            FormatThreadTrace(
                phase,
                Environment.CurrentManagedThreadId,
                Thread.CurrentThread.GetApartmentState(),
                Thread.CurrentThread.IsThreadPoolThread,
                detail));

    public static void GetState(
        IServerDocumentView view,
        ref string parameters,
        ref bool enabled,
        ref bool @checked,
        ref bool visible,
        ref string caption,
        ref string imageFile)
    {
        enabled = true;
        visible = true;
    }
}
