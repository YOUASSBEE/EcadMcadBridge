using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DXP;
using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;

namespace EcadMcadBridge.Altium.Application;

internal static class ExportBoardSnapshotHandler
{
    public const string CommandName = "ExportBoardSnapshot";
    private const string AddInVersion = "0.4.0";
    private const string Caption = "EcadMcadBridge";
    private const string ProducerComponent = "EcadMcadBridge.Altium";

    private static readonly ExportRequestCoordinator Requests =
        new((level, code, message) => BridgeLogger.Write(level, code, message));

    private static readonly string[] Capabilities =
    {
        "board-outline",
        "component-placement",
        "layers",
        "nets",
        "pad-stacks",
        "pads",
        "regions",
        "texts",
        "tracks",
        "vias",
    };

    public static void Execute(IServerDocumentView view, ref string parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        string message;
        bool warning;
        try
        {
            (message, warning) = Export(view, parameters);
        }
        catch (Exception ex)
        {
            BridgeLogger.Write("ERROR", ErrorCode(ex), ex.GetType().Name);
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

    private static (string Message, bool Warning) Export(IServerDocumentView view, string parameters)
    {
        var document = view?.GetOwnerDocument();
        if (!AltiumDocumentStateReader.TryRead(document, out var before, out var stateError)
            || before is null)
        {
            BridgeLogger.Write("WARN", "E_SOURCE_STATE", stateError);
            return (stateError, true);
        }

        if (before.IsModified)
        {
            const string unsaved = "The active PCB has unsaved changes; save it and retry the export.";
            BridgeLogger.Write("WARN", "E_SOURCE_MODIFIED", unsaved);
            return (unsaved, true);
        }

        BridgeLogger.Write(
            "INFO",
            "SOURCE_STATE",
            $"doc={Path.GetFileName(before.FilePath)} kind={before.DocumentKind} " +
            "modified=false pathRooted=true");

        var data = AltiumBoardSnapshotAdapter.Read(view);
        foreach (var adapterWarning in data.Warnings)
            BridgeLogger.Write("WARN", "ADAPTER", adapterWarning);
        BridgeLogger.Write("INFO", "ADAPTER_SUMMARY", data.Diagnostics.Summary());

        string? tempDirectory = null;
        try
        {
            tempDirectory = AltiumModelExportWorkspace.Create();
            var modelStopwatch = Stopwatch.StartNew();
            AltiumComponentModelResult modelResult;
            try
            {
                modelResult = AltiumComponentModelReader.Read(
                    data.Board,
                    data.Snapshot,
                    data.ComponentIdentities,
                    tempDirectory);
            }
            catch (AltiumModelInventoryException ex)
            {
                BridgeLogger.Write("ERROR", "E_MODEL_INVENTORY", ex.Message);
                return (
                    "3D component model inventory is incomplete; the Exchange Package was not created.\n" +
                    ex.Message,
                    true);
            }
            finally
            {
                modelStopwatch.Stop();
                BridgeLogger.Write(
                    "INFO",
                    "MODEL_EXPORT_MS",
                    modelStopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
            }

            foreach (var modelWarning in modelResult.Warnings)
                BridgeLogger.Write("WARN", "MODEL_WARNING", modelWarning);
            BridgeLogger.Write("INFO", "MODEL_EXPORT_SUMMARY", modelResult.Diagnostics.Summary());

            var capabilities = Capabilities.ToList();
            var boardModelResult = AltiumBoardModelExporter.Export(data.Board, tempDirectory);
            foreach (var warning in boardModelResult.Warnings)
                BridgeLogger.Write("WARN", "BOARD_MODEL", warning);
            if (boardModelResult.IsSuccess)
            {
                capabilities.Add(BoardModelDescriptor.Capability);
                BridgeLogger.Write(
                    "INFO",
                    "BOARD_MODEL_EXPORT",
                    $"boardStepSize={boardModelResult.Size} boardStepSha={boardModelResult.Sha256}");
            }
            else
            {
                BridgeLogger.Write("WARN", "E_BOARD_MODEL", boardModelResult.Error ?? "Board model export failed.");
            }

            var packageRoot = PackageRootResolver.Resolve();
            if (!packageRoot.IsSuccess || packageRoot.Root is null)
            {
                BridgeLogger.Write("WARN", "E_PACKAGE_ROOT", packageRoot.Message);
                return (packageRoot.Message, true);
            }

            var requestId = ResolveRequestId(parameters, before.FilePath);
            PackageSourceMetadata? source = null;
            string? successMessage = null;
            var partial = modelResult.Diagnostics.BodiesWithoutAsset > 0
                || modelResult.Warnings.Count > 0
                || !boardModelResult.IsSuccess;

            var outcome = Requests.Execute(
                requestId,
                () =>
                {
                    if (!AltiumDocumentStateReader.TryRecheck(document, before, out var recheckError))
                    {
                        BridgeLogger.Write("WARN", "E_SOURCE_CHANGED", recheckError);
                        return ExportGateResult.Reject(recheckError);
                    }

                    var sourceResult = PackageSourceMetadataReader.Read(before.FilePath, isModified: false);
                    if (!sourceResult.IsSuccess || sourceResult.Metadata is null)
                    {
                        BridgeLogger.Write("WARN", "E_SOURCE_HASH", sourceResult.Message);
                        return ExportGateResult.Reject(sourceResult.Message);
                    }

                    source = sourceResult.Metadata;
                    return ExportGateResult.Allow();
                },
                invocation => WritePackage(invocation));

            return outcome.Status switch
            {
                ExportRequestStatus.Completed => (successMessage!, partial),
                ExportRequestStatus.DuplicateRejected => (
                    "This export request was already processed; no second Exchange Package was created.\n" +
                    Correlation(outcome),
                    true),
                _ => (
                    "Exchange Package export did not complete.\n" + Correlation(outcome) + "\n" +
                    (outcome.Error ?? outcome.Status.ToString()),
                    true),
            };

            ExportInvocationResult WritePackage(ExportInvocation invocation)
            {
                var packageId = Guid.Parse(invocation.InvocationId);
                var options = new PackageWriteOptions
                {
                    PackageId = packageId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Producer = new PackageProducer
                    {
                        Component = ProducerComponent,
                        Version = AddInVersion,
                    },
                    Source = new PackageSource
                    {
                        Cad = "altium",
                        DocumentSha256 = source!.Sha256,
                    },
                    Capabilities = capabilities.ToArray(),
                };

                var boardRequest = boardModelResult.IsSuccess
                    ? new BoardModelWriteRequest { SourcePath = boardModelResult.StepPath! }
                    : null;
                var package = ExchangePackageWriter.Write(
                    packageRoot.Root,
                    data.Snapshot,
                    options,
                    modelResult.Request,
                    boardRequest);
                if (!package.IsSuccess)
                {
                    foreach (var diagnostic in package.Errors)
                    {
                        BridgeLogger.Write(
                            "ERROR",
                            diagnostic.Code,
                            diagnostic.Path + ": " + diagnostic.Message);
                    }

                    var first = package.Errors.Count > 0
                        ? package.Errors[0].Code + " at " + package.Errors[0].Path +
                          ": " + package.Errors[0].Message
                        : "Package writer returned failure without a diagnostic.";
                    return new ExportInvocationResult(ExportRequestStatus.Failed, first);
                }

                var packageDirectory = package.PackageDirectory ??
                    Path.Combine(packageRoot.Root, packageId.ToString("D"));
                var read = ExchangePackageReader.Read(packageDirectory);
                if (!read.IsSuccess
                    || read.Models is null
                    || read.Models.Components.Count != data.Snapshot.Components.Count
                    || (boardRequest is not null && read.BoardModel is null))
                {
                    foreach (var diagnostic in read.Errors)
                    {
                        BridgeLogger.Write(
                            "ERROR",
                            diagnostic.Code,
                            diagnostic.Path + ": " + diagnostic.Message);
                    }

                    TryDeletePackage(packageDirectory);
                    var first = read.Errors.Count > 0
                        ? read.Errors[0].Code + " at " + read.Errors[0].Path +
                          ": " + read.Errors[0].Message
                        : "Core Reader did not return the expected component model catalog.";
                    return new ExportInvocationResult(
                        ExportRequestStatus.Uncertain,
                        "Exchange Package verification failed; cleanup was requested.\n" + first);
                }

                successMessage =
                    "Exchange Package written to:\n" + packageDirectory +
                    "\npackageId=" + packageId.ToString("D") +
                    "\nrequestId=" + invocation.RequestId +
                    "\ncorrelationId=" + invocation.CorrelationId +
                    $"\nlayers={data.Snapshot.Layers.Count} components={data.Snapshot.Components.Count} " +
                    $"pads={data.Snapshot.Pads.Count} nets={data.Snapshot.Nets.Count}" +
                    $"\nbodies={modelResult.Diagnostics.BodiesDetected} " +
                    $"stepAssets={modelResult.Diagnostics.StepExportsSucceeded} " +
                    $"bodiesWithoutAsset={modelResult.Diagnostics.BodiesWithoutAsset}" +
                    $"\nboardStep={boardModelResult.Size} bytes sha256={boardModelResult.Sha256}" +
                    $"\nadapterWarnings={data.Warnings.Count} modelWarnings={modelResult.Warnings.Count} " +
                    $"packageWarnings={package.Warnings.Count}";
                BridgeLogger.Write(
                    "INFO",
                    "PACKAGE_EXPORT_OK",
                    $"addin={AddInVersion} cad=26 doc={Path.GetFileName(before.FilePath)} " +
                    $"packageId={packageId:D} requestId={invocation.RequestId} " +
                    $"correlationId={invocation.CorrelationId} invocationId={invocation.InvocationId} " +
                    $"layers={data.Snapshot.Layers.Count} components={data.Snapshot.Components.Count} " +
                    $"pads={data.Snapshot.Pads.Count} nets={data.Snapshot.Nets.Count} " +
                    modelResult.Diagnostics.Summary() + " " +
                    $"boardStepSize={boardModelResult.Size} boardStepSha={boardModelResult.Sha256} " +
                    $"adapterWarnings={data.Warnings.Count} modelWarnings={modelResult.Warnings.Count} " +
                    $"packageWarnings={package.Warnings.Count}");
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            }
        }
        finally
        {
            if (tempDirectory is not null)
            {
                var cleanupWarnings = new List<string>();
                AltiumModelExportWorkspace.TryDelete(tempDirectory, cleanupWarnings);
                foreach (var cleanupWarning in cleanupWarnings)
                    BridgeLogger.Write("WARN", "MODEL_CLEANUP", cleanupWarning);
            }
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

    internal static string ResolveRequestId(string parameters, string filePath)
    {
        if (Guid.TryParse(parameters.Trim(), out var explicitId))
            return explicitId.ToString("D");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(filePath));
        return "doc-" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Correlation(ExportRequestResult result) =>
        "requestId=" + result.RequestId +
        " correlationId=" + result.CorrelationId +
        (result.InvocationId is null ? string.Empty : " invocationId=" + result.InvocationId);

    private static void TryDeletePackage(string packageDirectory)
    {
        try
        {
            if (Directory.Exists(packageDirectory))
                Directory.Delete(packageDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            BridgeLogger.Write("WARN", "PACKAGE_CLEANUP", ex.GetType().Name);
        }
    }

    private static string ErrorCode(Exception ex) => ex.Message.Contains("No active PCB", StringComparison.Ordinal)
        ? "E_NO_ACTIVE_BOARD"
        : "E_PACKAGE_EXPORT_FAILED";
}
