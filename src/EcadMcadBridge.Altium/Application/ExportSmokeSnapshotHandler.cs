using System.Diagnostics;
using DXP;
using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Altium.Application;

internal static class ExportSmokeSnapshotHandler
{
    public const string CommandName = "ExportSmokeSnapshot";
    private const string AddInVersion = "0.1.0";
    private const string Caption = "EcadMcadBridge";

    public static void Execute(IServerDocumentView view, ref string parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        string message;
        bool warning;
        try
        {
            var data = PcbSnapshotReader.Read(view);
            var snapshot = new SmokeSnapshot
            {
                DocumentFileName = data.DocumentFileName,
                SampleCoordinate = new SmokeCoordinate(
                    data.SampleXIu, data.SampleYIu, data.SampleXMm, data.SampleYMm),
                ComponentCount = data.ComponentCount,
                OutlinePointCount = data.OutlinePointCount,
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Warnings = data.Warnings,
            };

            var path = SnapshotFileWriter.Write(snapshot);
            message = "Smoke snapshot written to:\n" + path;
            warning = false;
            BridgeLogger.Write(
                "INFO",
                "SNAPSHOT_OK",
                $"addin={AddInVersion} cad=26 doc={data.DocumentFileName} components={data.ComponentCount} " +
                $"outlinePoints={data.OutlinePointCount} warnings={data.Warnings.Count}");
        }
        catch (Exception ex)
        {
            BridgeLogger.Write("ERROR", ErrorCode(ex), ex.Message);
            message = ex.Message;
            warning = true;
        }
        finally
        {
            stopwatch.Stop();
            BridgeLogger.Write("INFO", "DURATION_MS", stopwatch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (warning)
            DXP.Utils.ShowWarning(message, Caption);
        else
            DXP.Utils.ShowInfo(message, Caption);
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

    private static string ErrorCode(Exception ex) => ex.Message.Contains("No active PCB", StringComparison.Ordinal)
        ? "E_NO_ACTIVE_BOARD"
        : "E_SNAPSHOT_FAILED";
}
