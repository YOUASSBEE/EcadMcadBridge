using DXP;

namespace EcadMcadBridge.Altium.Application;

internal static class ReadSelectedObjectDiagnosticsHandler
{
    public const string CommandName = "ReadSelectedObjectDiagnostics";
    private const string Caption = "EcadMcadBridge";

    public static void Execute(IServerDocumentView view, ref string parameters)
    {
        try
        {
            var report = SelectedObjectDiagnosticsReader.Read(view);
            var path = SelectedObjectDiagnosticsWriter.Write(report);
            BridgeLogger.Write(
                "INFO",
                "SELECTED_DIAGNOSTICS_OK",
                "objects=" + report.ObjectCount + " status=" + report.ReadStatus.Status);
            DXP.Utils.ShowInfo("Selected-object diagnostics written to:\n" + path, Caption);
        }
        catch (Exception ex)
        {
            BridgeLogger.Write("ERROR", "E_SELECTED_DIAGNOSTICS_FAILED", ex.GetType().Name);
            DXP.Utils.ShowWarning("Selected-object diagnostics failed: " + ex.GetType().Name + ".", Caption);
        }
    }

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
