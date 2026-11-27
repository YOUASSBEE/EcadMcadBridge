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
        var requestId = ResolveWorkflowRequestId(parameters);
        AltiumDocumentState? state = null;

        var result = new ExportRequestPipeline(
            Requests,
            () =>
            {
                if (!AltiumDocumentStateReader.TryRead(document, out var before, out var stateError)
                    || before is null)
                {
                    BridgeLogger.Write("WARN", "E_SOURCE_STATE", stateError);
                    return ExportGateResult.Reject(stateError);
                }

                if (before.IsModified)
                {
                    const string unsaved =
                        "The active PCB has unsaved changes; save it and retry the export.";
                    BridgeLogger.Write("WARN", "E_SOURCE_MODIFIED", unsaved);
                    return ExportGateResult.Reject(unsaved);
                }

                BridgeLogger.Write(
                    "INFO",
                    "SOURCE_STATE",
                    $"doc={Path.GetFileName(before.FilePath)} kind={before.DocumentKind} " +
                    "modified=false pathRooted=true");
                state = before;
                return ExportGateResult.Allow();
            },
            () =>
            {
                var resolved = PackageRootResolver.Resolve();
                if (!resolved.IsSuccess || resolved.Root is null)
                {
                    BridgeLogger.Write("WARN", "E_PACKAGE_ROOT", resolved.Message);
                    return ExportPackageRootResult.Reject(resolved.Message);
                }

                return ExportPackageRootResult.Allow(resolved.Root);
            },
            () => AltiumModelExportWorkspace.Create(),
            directory =>
            {
                var cleanupWarnings = new List<string>();
                AltiumModelExportWorkspace.TryDelete(directory, cleanupWarnings);
                foreach (var cleanupWarning in cleanupWarnings)
                    BridgeLogger.Write("WARN", "MODEL_CLEANUP", cleanupWarning);
            },
            () => AltiumBoardSnapshotAdapter.Read(view),
            (snapshot, directory) =>
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    return AltiumComponentModelReader.Read(
                        snapshot.Board,
                        snapshot.Snapshot,
                        snapshot.ComponentIdentities,
                        directory,
                        snapshot.AllowedComponentIds);
                }
                finally
                {
                    stopwatch.Stop();
                    BridgeLogger.Write(
                        "INFO",
                        "MODEL_EXPORT_MS",
                        stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
                }
            },
            (snapshot, directory) => AltiumBoardModelExporter.Export(snapshot.Board, directory),
            WritePackage,
            Capabilities,
            (level, code, message) => BridgeLogger.Write(level, code, message),
            snapshot => TopOnlySelectionFilter.Apply(snapshot, AltiumTopOnlySelectionReader.Read(snapshot.Board)))
            .Execute(requestId);

        if (result.Outcome.Status == ExportRequestStatus.DuplicateRejected)
        {
            return (
                "status=DuplicateRejected\n" +
                "This export request was already processed; no second Exchange Package was created.\n" +
                Correlation(result.Outcome) + DescribeSecondary(result.Outcome),
                true);
        }

        if (result.Outcome.Status != ExportRequestStatus.Completed)
        {
            return (
                "Exchange Package export did not complete.\n" +
                "status=" + result.Outcome.Status + "\n" +
                Correlation(result.Outcome) + "\n" +
                result.Message + DescribeSelection(result.Selection) + DescribeSecondary(result.Outcome),
                true);
        }

        return (
            "status=Completed\n" + result.Message + DescribeSelection(result.Selection) +
            DescribeSecondary(result.Outcome),
            result.Warning || (result.Selection?.Skipped.Count ?? 0) > 0);

        ExportInvocationResult WritePackage(
            ExportPackageWriteRequest request,
            ExportInvocation invocation)
        {
            if (state is null)
            {
                const string missing = "The active PCB state was not captured.";
                BridgeLogger.Write("WARN", "E_SOURCE_CHANGED", missing);
                return new ExportInvocationResult(ExportRequestStatus.Failed, missing);
            }

            if (!AltiumDocumentStateReader.TryRecheck(document, state, out var recheckError))
            {
                BridgeLogger.Write("WARN", "E_SOURCE_CHANGED", recheckError);
                return new ExportInvocationResult(ExportRequestStatus.Failed, recheckError);
            }

            var sourceResult = PackageSourceMetadataReader.Read(state.FilePath, isModified: false);
            if (!sourceResult.IsSuccess || sourceResult.Metadata is null)
            {
                BridgeLogger.Write("WARN", "E_SOURCE_HASH", sourceResult.Message);
                return new ExportInvocationResult(ExportRequestStatus.Failed, sourceResult.Message);
            }

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
                    DocumentSha256 = sourceResult.Metadata.Sha256,
                },
                Capabilities = request.Capabilities,
            };

            var boardRequest = request.BoardModel.IsSuccess
                ? new BoardModelWriteRequest { SourcePath = request.BoardModel.StepPath! }
                : null;
            var package = ExchangePackageWriter.Write(
                request.PackageRoot,
                request.Snapshot.Snapshot,
                options,
                request.Model.Request,
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
                Path.Combine(request.PackageRoot, packageId.ToString("D"));
            var read = ExchangePackageReader.Read(packageDirectory);
            var selectionError = read.IsSuccess
                ? TopOnlyPackageVerifier.Verify(read, request.Snapshot.Snapshot)
                : null;
            if (!read.IsSuccess
                || selectionError is not null
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
                var first = selectionError
                    ?? (read.Errors.Count > 0
                        ? read.Errors[0].Code + " at " + read.Errors[0].Path +
                          ": " + read.Errors[0].Message
                        : "Core Reader did not return the expected Top-only package.");
                return new ExportInvocationResult(
                    ExportRequestStatus.Uncertain,
                    "Exchange Package verification failed; cleanup was requested.\n" + first);
            }

            var partial = request.Model.Diagnostics.BodiesWithoutAsset > 0
                || request.Model.Warnings.Count > 0
                || !request.BoardModel.IsSuccess;
            var message =
                "Exchange Package written to:\n" + packageDirectory +
                "\npackageId=" + packageId.ToString("D") +
                "\nrequestId=" + invocation.RequestId +
                "\ncorrelationId=" + invocation.CorrelationId +
                $"\nlayers={request.Snapshot.Snapshot.Layers.Count} " +
                $"components={request.Snapshot.Snapshot.Components.Count} " +
                $"pads={request.Snapshot.Snapshot.Pads.Count} nets={request.Snapshot.Snapshot.Nets.Count}" +
                $"\nbodies={request.Model.Diagnostics.BodiesDetected} " +
                $"stepAssets={request.Model.Diagnostics.StepExportsSucceeded} " +
                $"bodiesWithoutAsset={request.Model.Diagnostics.BodiesWithoutAsset}" +
                $"\nboardStep={request.BoardModel.Size} bytes sha256={request.BoardModel.Sha256}" +
                $"\nadapterWarnings={request.Snapshot.Warnings.Count} " +
                $"modelWarnings={request.Model.Warnings.Count} " +
                $"packageWarnings={package.Warnings.Count}";
            var successLogError = SecondaryFailureGuard.TryWrite(
                (level, code, logMessage) => BridgeLogger.Write(level, code, logMessage),
                "INFO",
                "PACKAGE_EXPORT_OK",
                $"addin={AddInVersion} cad=26 doc={Path.GetFileName(state.FilePath)} " +
                $"packageId={packageId:D} requestId={invocation.RequestId} " +
                $"correlationId={invocation.CorrelationId} invocationId={invocation.InvocationId} " +
                $"layers={request.Snapshot.Snapshot.Layers.Count} " +
                $"components={request.Snapshot.Snapshot.Components.Count} " +
                $"pads={request.Snapshot.Snapshot.Pads.Count} nets={request.Snapshot.Snapshot.Nets.Count} " +
                request.Model.Diagnostics.Summary() + " " +
                $"boardStepSize={request.BoardModel.Size} boardStepSha={request.BoardModel.Sha256} " +
                $"adapterWarnings={request.Snapshot.Warnings.Count} " +
                $"modelWarnings={request.Model.Warnings.Count} " +
                $"packageWarnings={package.Warnings.Count}");
            return new ExportInvocationResult(
                ExportRequestStatus.Completed,
                Message: message,
                Partial: partial,
                SecondaryErrors: SecondaryFailureGuard.WithError(null, successLogError));
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

    /// <summary>
    /// A command invocation is a new operation by default; an explicit GUID keeps controlled
    /// duplicate rejection. The document-hash fallback is retained only for compatibility tests.
    /// </summary>
    internal static string ResolveWorkflowRequestId(string parameters) =>
        Guid.TryParse(parameters.Trim(), out var explicitId)
            ? explicitId.ToString("D")
            : Guid.NewGuid().ToString("D");

    private static string DescribeSelection(TopOnlyFilterResult? selection)
    {
        if (selection is null)
            return string.Empty;

        var exported = string.Join(", ", selection.Exported.Select(item => item.Reference));
        var skipped = selection.Skipped
            .Select(item => item.Reference + ": " + item.Reason);
        return
            $"\nselected={selection.Selected.Count} exported={selection.Exported.Count} " +
            $"skipped={selection.Skipped.Count}" +
            "\nexportedComponents=" + (exported.Length == 0 ? "none" : exported) +
            "\nskippedComponents=" + (selection.Skipped.Count == 0
                ? "none"
                : string.Join("; ", skipped));
    }

    private static string DescribeSecondary(ExportRequestResult result)
    {
        if (result.SecondaryErrors is null || result.SecondaryErrors.Count == 0)
            return "\nsecondaryErrors=none";

        return "\nsecondaryErrors=" + string.Join("; ", result.SecondaryErrors);
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
