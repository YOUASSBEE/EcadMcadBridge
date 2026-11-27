using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Altium.Application;

internal delegate ExportGateResult ExportOwnershipGate();

internal delegate ExportPackageRootResult ExportPackageRootResolver();

internal delegate AltiumBoardSnapshotResult ExportSnapshotReader();

internal delegate AltiumComponentModelResult ExportComponentModelReader(
    AltiumBoardSnapshotResult snapshot,
    string exportDirectory);

internal delegate AltiumBoardModelExportResult ExportBoardModelExporter(
    AltiumBoardSnapshotResult snapshot,
    string exportDirectory);

internal delegate ExportInvocationResult ExportPackageWriter(
    ExportPackageWriteRequest request,
    ExportInvocation invocation);

internal sealed record ExportPackageRootResult(bool Allowed, string? Root, string? Error)
{
    public static ExportPackageRootResult Allow(string root) => new(true, root, null);

    public static ExportPackageRootResult Reject(string error) => new(false, null, error);
}

internal sealed record ExportPackageWriteRequest(
    AltiumBoardSnapshotResult Snapshot,
    AltiumComponentModelResult Model,
    AltiumBoardModelExportResult BoardModel,
    string PackageRoot,
    string[] Capabilities);

internal sealed record ExportPipelineResult(
    ExportRequestResult Outcome,
    string Message,
    bool Warning);

/// <summary>
/// Whole-request export operation. Reservation happens in <see cref="ExportRequestCoordinator"/>
/// before this class is entered; every boundary below is therefore called at most once per request.
/// </summary>
internal sealed class ExportRequestPipeline
{
    private const string InventoryMessage =
        "3D component model inventory is incomplete; the Exchange Package was not created.\n";

    private readonly ExportRequestCoordinator _coordinator;
    private readonly ExportOwnershipGate _gate;
    private readonly ExportPackageRootResolver _resolvePackageRoot;
    private readonly Func<string> _createWorkspace;
    private readonly Action<string> _cleanupWorkspace;
    private readonly ExportSnapshotReader _readSnapshot;
    private readonly ExportComponentModelReader _readComponentModels;
    private readonly ExportBoardModelExporter _exportBoardModel;
    private readonly ExportPackageWriter _writePackage;
    private readonly IReadOnlyList<string> _baseCapabilities;
    private readonly Action<string, string, string> _write;

    public ExportRequestPipeline(
        ExportRequestCoordinator coordinator,
        ExportOwnershipGate gate,
        ExportPackageRootResolver resolvePackageRoot,
        Func<string> createWorkspace,
        Action<string> cleanupWorkspace,
        ExportSnapshotReader readSnapshot,
        ExportComponentModelReader readComponentModels,
        ExportBoardModelExporter exportBoardModel,
        ExportPackageWriter writePackage,
        IReadOnlyList<string> baseCapabilities,
        Action<string, string, string> write)
    {
        _coordinator = coordinator;
        _gate = gate;
        _resolvePackageRoot = resolvePackageRoot;
        _createWorkspace = createWorkspace;
        _cleanupWorkspace = cleanupWorkspace;
        _readSnapshot = readSnapshot;
        _readComponentModels = readComponentModels;
        _exportBoardModel = exportBoardModel;
        _writePackage = writePackage;
        _baseCapabilities = baseCapabilities;
        _write = write;
    }

    public ExportPipelineResult Execute(string requestId)
    {
        var outcome = _coordinator.Execute(requestId, Run);
        var message = outcome.Message ?? outcome.Error ?? outcome.Status.ToString();
        return new ExportPipelineResult(
            outcome,
            message,
            outcome.Status != ExportRequestStatus.Completed || outcome.Partial);
    }

    private ExportInvocationResult Run(ExportInvocation invocation)
    {
        var gate = _gate() ?? throw new InvalidOperationException("Ownership gate returned no result.");
        if (!gate.Allowed)
            return new ExportInvocationResult(
                ExportRequestStatus.GateRejected,
                gate.Error ?? "Export gate rejected the request.");

        string? tempDirectory = null;
        try
        {
            var root = _resolvePackageRoot()
                ?? throw new InvalidOperationException("Package root resolver returned no result.");
            if (!root.Allowed || root.Root is null)
                return new ExportInvocationResult(
                    ExportRequestStatus.Failed,
                    root.Error ?? "Package root is unavailable.");

            tempDirectory = _createWorkspace();
            if (string.IsNullOrWhiteSpace(tempDirectory))
                return new ExportInvocationResult(
                    ExportRequestStatus.Failed,
                    "Model export workspace is unavailable.");

            var snapshot = _readSnapshot()
                ?? throw new InvalidOperationException("Snapshot reader returned no result.");
            foreach (var warning in snapshot.Warnings)
                _write("WARN", "ADAPTER", warning);
            _write("INFO", "ADAPTER_SUMMARY", snapshot.Diagnostics.Summary());

            var model = _readComponentModels(snapshot, tempDirectory)
                ?? throw new InvalidOperationException("Component model reader returned no result.");
            foreach (var warning in model.Warnings)
                _write("WARN", "MODEL_WARNING", warning);
            _write("INFO", "MODEL_EXPORT_SUMMARY", model.Diagnostics.Summary());

            var boardModel = _exportBoardModel(snapshot, tempDirectory)
                ?? throw new InvalidOperationException("Board model exporter returned no result.");
            foreach (var warning in boardModel.Warnings)
                _write("WARN", "BOARD_MODEL", warning);
            if (boardModel.IsSuccess)
            {
                _write(
                    "INFO",
                    "BOARD_MODEL_EXPORT",
                    $"boardStepSize={boardModel.Size} boardStepSha={boardModel.Sha256}");
            }
            else
            {
                _write("WARN", "E_BOARD_MODEL", boardModel.Error ?? "Board model export failed.");
            }

            var capabilities = _baseCapabilities.ToList();
            if (boardModel.IsSuccess)
                capabilities.Add(BoardModelDescriptor.Capability);

            return _writePackage(
                       new ExportPackageWriteRequest(
                           snapshot,
                           model,
                           boardModel,
                           root.Root,
                           capabilities.ToArray()),
                       invocation)
                   ?? throw new InvalidOperationException("Package writer returned no result.");
        }
        catch (AltiumModelInventoryException ex)
        {
            return new ExportInvocationResult(ExportRequestStatus.Failed, InventoryMessage + ex.Message);
        }
        catch (Exception ex)
        {
            return new ExportInvocationResult(
                ExportRequestStatus.Failed,
                ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            if (tempDirectory is not null)
                _cleanupWorkspace(tempDirectory);
        }
    }
}
