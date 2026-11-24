using System.Diagnostics;
using System.Globalization;
using System.IO;
using DXP;
using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Serialization;
using EcadMcadBridge.Core.Validation;

namespace EcadMcadBridge.Altium.Application;

internal static class ExportBoardSnapshotHandler
{
    public const string CommandName = "ExportBoardSnapshot";
    private const string AddInVersion = "0.2.0";
    private const string Caption = "EcadMcadBridge";

    public static void Execute(IServerDocumentView view, ref string parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        string message;
        var warning = false;
        try
        {
            var result = AltiumBoardSnapshotAdapter.Read(view);
            foreach (var adapterWarning in result.Warnings)
                BridgeLogger.Write("WARN", "ADAPTER", adapterWarning);

            var validation = BoardSnapshotValidator.Validate(result.Snapshot);
            foreach (var diagnostic in validation.Diagnostics.Take(50))
            {
                BridgeLogger.Write(
                    diagnostic.Severity == BoardDiagnosticSeverity.Error ? "ERROR" : "WARN",
                    diagnostic.Code,
                    diagnostic.Path + ": " + diagnostic.Message);
            }

            if (validation.Diagnostics.Count > 50)
            {
                BridgeLogger.Write(
                    "INFO",
                    "VALIDATION_TRUNCATED",
                    $"{validation.Diagnostics.Count - 50} additional diagnostics were not logged.");
            }

            if (!validation.IsValid)
            {
                warning = true;
                var first = validation.Errors[0];
                message = $"Board snapshot validation failed: {validation.Errors.Count} error(s).\n" +
                    $"{first.Code} at {first.Path}: {first.Message}";
            }
            else
            {
                var directory = Path.Combine(Path.GetTempPath(), "EcadMcadBridge");
                Directory.CreateDirectory(directory);
                var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
                var path = Path.Combine(directory, "board-snapshot-" + stamp + ".json");
                File.WriteAllBytes(path, BoardSnapshotJson.SerializeToUtf8(result.Snapshot));
                message = "Board snapshot written to:\n" + path +
                    $"\nlayers={result.Snapshot.Layers.Count} components={result.Snapshot.Components.Count} " +
                    $"pads={result.Snapshot.Pads.Count} nets={result.Snapshot.Nets.Count} " +
                    $"warnings={validation.Warnings.Count + result.Warnings.Count}";
                BridgeLogger.Write(
                    "INFO",
                    "BOARD_SNAPSHOT_OK",
                    $"addin={AddInVersion} cad=26 doc={result.Snapshot.Source.DocumentFileName} " +
                    $"layers={result.Snapshot.Layers.Count} components={result.Snapshot.Components.Count} " +
                    $"pads={result.Snapshot.Pads.Count} nets={result.Snapshot.Nets.Count} " +
                    $"adapterWarnings={result.Warnings.Count} validationWarnings={validation.Warnings.Count}");
            }
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
            BridgeLogger.Write(
                "INFO",
                "DURATION_MS",
                stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
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
        : "E_BOARD_SNAPSHOT_FAILED";
}
