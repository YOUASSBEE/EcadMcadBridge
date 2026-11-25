using System.Diagnostics;
using System.Globalization;
using System.IO;
using DXP;
using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Packaging;

namespace EcadMcadBridge.Altium.Application;

internal static class ExportBoardSnapshotHandler
{
    public const string CommandName = "ExportBoardSnapshot";
    private const string AddInVersion = "0.4.0";
    private const string Caption = "EcadMcadBridge";
    private const string ProducerComponent = "EcadMcadBridge.Altium";

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
            (message, warning) = Export(view);
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

    private static (string Message, bool Warning) Export(IServerDocumentView view)
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

            if (!AltiumDocumentStateReader.TryRecheck(document, before, out var recheckError))
            {
                BridgeLogger.Write("WARN", "E_SOURCE_CHANGED", recheckError);
                return (recheckError, true);
            }

            var source = PackageSourceMetadataReader.Read(before.FilePath, isModified: false);
            if (!source.IsSuccess || source.Metadata is null)
            {
                BridgeLogger.Write("WARN", "E_SOURCE_HASH", source.Message);
                return (source.Message, true);
            }

            var packageRoot = PackageRootResolver.Resolve();
            if (!packageRoot.IsSuccess || packageRoot.Root is null)
            {
                BridgeLogger.Write("WARN", "E_PACKAGE_ROOT", packageRoot.Message);
                return (packageRoot.Message, true);
            }

            var packageId = Guid.NewGuid();
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
                    DocumentSha256 = source.Metadata.Sha256,
                },
                Capabilities = Capabilities,
            };

            var package = ExchangePackageWriter.Write(
                packageRoot.Root,
                data.Snapshot,
                options,
                modelResult.Request);
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
                return ("Exchange Package export failed.\n" + first, true);
            }

            var packageDirectory = package.PackageDirectory ??
                Path.Combine(packageRoot.Root, packageId.ToString("D"));
            var read = ExchangePackageReader.Read(packageDirectory);
            if (!read.IsSuccess
                || read.Models is null
                || read.Models.Components.Count != data.Snapshot.Components.Count)
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
                return ("Exchange Package verification failed; the package was removed.\n" + first, true);
            }

            var partial = modelResult.Diagnostics.BodiesWithoutAsset > 0
                || modelResult.Warnings.Count > 0;
            var message =
                "Exchange Package written to:\n" + packageDirectory +
                "\npackageId=" + packageId.ToString("D") +
                $"\nlayers={data.Snapshot.Layers.Count} components={data.Snapshot.Components.Count} " +
                $"pads={data.Snapshot.Pads.Count} nets={data.Snapshot.Nets.Count}" +
                $"\nbodies={modelResult.Diagnostics.BodiesDetected} " +
                $"stepAssets={modelResult.Diagnostics.StepExportsSucceeded} " +
                $"bodiesWithoutAsset={modelResult.Diagnostics.BodiesWithoutAsset}" +
                $"\nadapterWarnings={data.Warnings.Count} modelWarnings={modelResult.Warnings.Count} " +
                $"packageWarnings={package.Warnings.Count}";
            BridgeLogger.Write(
                "INFO",
                "PACKAGE_EXPORT_OK",
                $"addin={AddInVersion} cad=26 doc={Path.GetFileName(before.FilePath)} " +
                $"packageId={packageId:D} layers={data.Snapshot.Layers.Count} " +
                $"components={data.Snapshot.Components.Count} pads={data.Snapshot.Pads.Count} " +
                $"nets={data.Snapshot.Nets.Count} " + modelResult.Diagnostics.Summary() + " " +
                $"adapterWarnings={data.Warnings.Count} modelWarnings={modelResult.Warnings.Count} " +
                $"packageWarnings={package.Warnings.Count}");
            return (message, partial);
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
