using System.Globalization;
using System.IO;
using System.Text.Json;
using DXP;
using EcadMcadBridge.Altium.Adapter;

namespace EcadMcadBridge.Altium.Application;

/// <summary>
/// Command handler for read-only D-024 diagnostics. The command parameter is the path to a JSON
/// request file; the response is written to the outputPath in that request.
/// </summary>
internal static class ReadBodyIdentityDiagnosticsHandler
{
    public const string CommandName = "ReadBodyIdentityDiagnostics";
    private const string Caption = "EcadMcadBridge";

    private sealed record RequestDto
    {
        public string? PackageDirectory { get; init; }
        public string? OutputPath { get; init; }
        public string[]? LibraryPaths { get; init; }
        public string[]? Designators { get; init; }
    }

    public static void Execute(IServerDocumentView view, ref string parameters)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string message;
        bool warning;
        try
        {
            (message, warning) = Run(view, parameters);
        }
        catch (Exception ex)
        {
            BridgeLogger.Write("ERROR", "E_BODY_DIAGNOSTICS", ex.GetType().Name + ": " + ex.Message);
            message = "Read-only body diagnostics failed: " + ex.Message;
            warning = true;
        }
        finally
        {
            stopwatch.Stop();
            BridgeLogger.Write(
                "INFO",
                "BODY_DIAGNOSTICS_MS",
                stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
        }

        if (warning)
            DXP.Utils.ShowWarning(message, Caption);
        else
            DXP.Utils.ShowInfo(message, Caption);
    }

    private static (string Message, bool Warning) Run(IServerDocumentView view, string parameters)
    {
        var requestPath = parameters?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requestPath) || !Path.IsPathRooted(requestPath) || !File.Exists(requestPath))
            return ("Pass the absolute path to a diagnostics request JSON file as the command parameter.", true);

        RequestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RequestDto>(
                File.ReadAllText(requestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            return ("The diagnostics request is not valid JSON: " + ex.GetType().Name, true);
        }

        if (dto is null
            || string.IsNullOrWhiteSpace(dto.PackageDirectory)
            || string.IsNullOrWhiteSpace(dto.OutputPath)
            || !Path.IsPathRooted(dto.PackageDirectory)
            || !Path.IsPathRooted(dto.OutputPath))
            return ("The request must contain absolute packageDirectory and outputPath values.", true);

        var packageDirectory = dto.PackageDirectory!;
        var outputPath = dto.OutputPath!;
        if (!Directory.Exists(packageDirectory))
            return ("The requested package directory does not exist.", true);

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            return ("The output directory does not exist.", true);

        var request = new AltiumBodyIdentityDiagnosticsReader.Request(
            packageDirectory,
            (dto.LibraryPaths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path))
                .ToArray(),
            dto.Designators ?? Array.Empty<string>(),
            outputPath);

        var response = AltiumBodyIdentityDiagnosticsReader.Read(view, request);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json);

        BridgeLogger.Write(
            "INFO",
            "BODY_DIAGNOSTICS_WRITTEN",
            "path=" + outputPath + " bytes=" +
            json.Length.ToString(CultureInfo.InvariantCulture));

        var status = response is Dictionary<string, object?> map
                     && map.TryGetValue("status", out var value)
            ? value as string
            : null;
        var message = "Read-only body diagnostics written to:\n" + outputPath + "\nstatus=" + status;
        return (message, !string.Equals(status, "ok", StringComparison.Ordinal));
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
