using System.Diagnostics;
using System.Text.Json;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using EcadMcadBridge.Core.Placement;
using EcadMcadBridge.SolidWorks.Assemblies;
using EcadMcadBridge.SolidWorks.Importing;
using EcadMcadBridge.SolidWorks.Interop;
using EcadMcadBridge.SolidWorks.Placement;
using SolidWorks.Interop.sldworks;

namespace EcadMcadBridge.SolidWorks.Smoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        SolidWorksInteropResolver.Register();
        return SmokeRunner.Run(args);
    }
}

internal static class SolidWorksInteropResolver
{
    public static void Register() =>
        AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
        {
            var name = new System.Reflection.AssemblyName(eventArgs.Name).Name;
            if (string.IsNullOrWhiteSpace(name))
                return null;
            var path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            return File.Exists(path)
                ? System.Reflection.Assembly.LoadFrom(path)
                : null;
        };
}

internal static class SmokeRunner
{
    [STAThread]
    public static int Run(string[] args)
    {
        var options = SmokeOptions.Parse(args);
        if (options.Error is not null)
        {
            Console.Error.WriteLine(options.Error);
            return 2;
        }

        Directory.CreateDirectory(options.OutputDirectory);
        var resultPath = Path.Combine(options.OutputDirectory, "smoke-result.json");
        var total = Stopwatch.StartNew();

        if (!SolidWorksConnection.TryConnect(out var solidWorks, out var connectionMessage)
            || solidWorks is null)
        {
            Console.Error.WriteLine(connectionMessage);
            File.WriteAllText(
                resultPath,
                JsonSerializer.Serialize(
                    new { success = false, connection = connectionMessage },
                    JsonOptions()));
            return 2;
        }

        string revision;
        string build;
        try
        {
            revision = solidWorks.RevisionNumber();
            solidWorks.GetBuildNumbers2(out var baseVersion, out var currentVersion, out var hotFixes);
            build = baseVersion + "/" + currentVersion + "/" + hotFixes;
        }
        catch
        {
            revision = "unknown";
            build = "unknown";
        }

        var read = ExchangePackageReader.Read(options.PackageDirectory);
        var plan = SolidWorksImportPlanBuilder.Build(
            read,
            options.PackageDirectory,
            Path.Combine(options.OutputDirectory, "native"));
        var baselineDocuments = ReadOpenDocuments(solidWorks);
        SolidWorksImportPlan selectedPlan = plan;
        SolidWorksPlacementPlan? placementPlan = null;
        if (options.PlacedAssembly)
        {
            placementPlan = SolidWorksPlacementRequestBuilder.Build(
                read,
                plan,
                options.Designators,
                options.MaxComponents);
            var assetPaths = placementPlan.Requests
                .Select(request => request.AssetPath)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            selectedPlan = plan.SelectAssets(assetPaths);
        }
        else if (options.OnlyAsset is not null)
        {
            selectedPlan = plan.SelectAssets(new[] { options.OnlyAsset });
        }
        else if (options.Sample)
        {
            var sampleAssets = SelectSampleAssets(plan);
            if (sampleAssets.Count == 0)
            {
                Console.Error.WriteLine("No representative STEP assets were found.");
                return 1;
            }

            selectedPlan = plan.SelectAssets(sampleAssets);
        }

        var importTimer = Stopwatch.StartNew();
        var importResult = selectedPlan.IsValid
            ? new SolidWorksImportService(solidWorks).Import(selectedPlan)
            : new SolidWorksImportResult(
                Array.Empty<ImportedBodyPart>(),
                Array.Empty<SolidWorksImportedAsset>(),
                selectedPlan.Errors,
                new SolidWorksDocumentLifecycle(0, 0, 0, 0, Array.Empty<string>()));
        importTimer.Stop();

        SolidWorksAssemblyResult? assembly = null;
        if (options.Assembly)
        {
            if (string.IsNullOrWhiteSpace(options.AssemblyTemplate))
            {
                Console.Error.WriteLine("--assembly requires --template <asmdot>.");
                return 2;
            }

            var partPaths = importResult.Assets
                .Where(asset => asset.SaveSucceeded && asset.ReopenSucceeded)
                .Select(asset => asset.NativePartPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            assembly = new SolidWorksAssemblyService(solidWorks).Create(
                options.AssemblyTemplate,
                Path.Combine(options.OutputDirectory, "assembly", "smoke-assembly.SLDASM"),
                partPaths);
        }

        SolidWorksPlacedAssemblyResult? placedAssembly = null;
        BoardPlacementTransformResult? boardTransform = null;
        SolidWorksPlacedBoardOccurrence? boardOccurrence = null;
        if (options.PlacedAssembly)
        {
            var occurrences = placementPlan?.Requests
                .Select(request => new SolidWorksPlacedOccurrence(
                    request.ComponentId,
                    request.Designator,
                    request.BodyOrdinal,
                    request.NativePartPath,
                    request.Transform))
                .ToArray() ?? Array.Empty<SolidWorksPlacedOccurrence>();
            if (importResult.Board is not null
                && importResult.Board.Geometry?.Geometry is { } boardGeometry
                && read.Snapshot is not null)
            {
                boardTransform = BoardPlacementTransformBuilder.Build(
                    read.Snapshot.Properties.Thickness.Mm ?? 0,
                    boardGeometry);
                if (boardTransform.IsSupported)
                {
                    boardOccurrence = new SolidWorksPlacedBoardOccurrence(
                        importResult.Board.NativePartPath,
                        boardTransform.Transform!.Value);
                }
            }

            placedAssembly = new SolidWorksPlacedAssemblyService(solidWorks).Create(
                options.AssemblyTemplate!,
                Path.Combine(options.OutputDirectory, "assembly", "smoke-placed-assembly.SLDASM"),
                occurrences,
                boardOccurrence);
        }

        total.Stop();
        var remainingDocuments = ReadOpenDocuments(solidWorks);
        var baselineAvailable = baselineDocuments.IsAvailable;
        var finalAvailable = remainingDocuments.IsAvailable;
        string[]? createdDocumentsRemaining = null;
        bool? baselinePreserved = null;
        if (baselineAvailable && finalAvailable)
        {
            var baselineSet = new HashSet<string>(
                baselineDocuments.Identities,
                StringComparer.OrdinalIgnoreCase);
            createdDocumentsRemaining = remainingDocuments.Identities
                .Where(item => !baselineSet.Contains(item))
                .ToArray();
            baselinePreserved = baselineDocuments.Identities.All(item =>
                remainingDocuments.Identities.Contains(item, StringComparer.OrdinalIgnoreCase));
        }

        var diagnostics = plan.Diagnostics
            .Concat(placementPlan?.Diagnostics ?? Array.Empty<SolidWorksDiagnostic>())
            .Concat(importResult.Diagnostics)
            .Concat(assembly?.Diagnostics ?? Array.Empty<SolidWorksDiagnostic>())
            .Concat(placedAssembly?.Diagnostics ?? Array.Empty<SolidWorksDiagnostic>())
            .ToArray();
        var importedAll = importResult.Assets.Count == selectedPlan.Assets.Count
            && importResult.Assets.All(asset =>
                asset.SaveSucceeded
                && asset.ReopenSucceeded
                && asset.ImportedBodyCount == asset.ReopenedBodyCount
                && asset.ReopenedBodyCount > 0);
        var boardImported = importResult.Board is null
            || (importResult.Board.SaveSucceeded
                && importResult.Board.ReopenSucceeded
                && importResult.Board.ImportedBodyCount == importResult.Board.ReopenedBodyCount
                && importResult.Board.Geometry is { IsValid: true });
        var mappedAll = importResult.BodyParts.Count == selectedPlan.BodyReferences.Count;
        var assemblyOk = assembly is null || (assembly.Saved && assembly.Reopened);
        var placementOk = !options.PlacedAssembly
            || (placementPlan is { IsValid: true }
                && placedAssembly is { Saved: true, Reopened: true, TransformsVerified: true }
                && (importResult.Board is null
                    || (boardOccurrence is not null
                        && placedAssembly.BoardOccurrence is { Verified: true, Fixed: true })));
        var cleanupVerified = importResult.Lifecycle.CleanupStatus == SolidWorksCleanupStatus.Verified
            && (assembly is null || assembly.Lifecycle.CleanupStatus == SolidWorksCleanupStatus.Verified)
            && (placedAssembly is null || placedAssembly.Lifecycle.CleanupStatus == SolidWorksCleanupStatus.Verified);
        var success = plan.IsValid
            && (placementPlan is null || placementPlan.IsValid)
            && importResult.Assets.Count == selectedPlan.Assets.Count
            && importedAll
            && boardImported
            && mappedAll
            && assemblyOk
            && placementOk
            && baselineAvailable
            && finalAvailable
            && baselinePreserved == true
            && createdDocumentsRemaining is { Length: 0 }
            && cleanupVerified
            && diagnostics.All(item => item.Severity != SolidWorksDiagnosticSeverity.Error);

        var report = new
        {
            success,
            mode = options.PlacedAssembly ? "placed" : options.Sample ? "sample" : "all",
            solidWorksRevision = revision,
            solidWorksBuild = build,
            packageId = read.Manifest?.PackageId,
            packageReadSuccess = read.IsSuccess,
            packageComponents = read.Snapshot?.Components.Count ?? 0,
            catalogComponents = read.Models?.Components.Count ?? 0,
            selectedUniqueAssets = selectedPlan.Assets.Count,
            selectedBodyReferences = selectedPlan.BodyReferences.Count,
            importedAssets = importResult.Assets.Count,
            nativeParts = importResult.Assets.Count(asset => asset.SaveSucceeded && asset.ReopenSucceeded),
            mappedBodyParts = importResult.BodyParts.Count,
            importMilliseconds = importTimer.ElapsedMilliseconds,
            totalMilliseconds = total.ElapsedMilliseconds,
            assets = importResult.Assets,
            bodyParts = importResult.BodyParts,
            board = importResult.Board is null
                ? null
                : new
                {
                    assetPath = importResult.Board.AssetPath,
                    nativePartPath = importResult.Board.NativePartPath,
                    importedBodyCount = importResult.Board.ImportedBodyCount,
                    reopenedBodyCount = importResult.Board.ReopenedBodyCount,
                    saveSucceeded = importResult.Board.SaveSucceeded,
                    reopenSucceeded = importResult.Board.ReopenSucceeded,
                    geometry = importResult.Board.Geometry?.Geometry,
                    geometryDiagnostics = importResult.Board.Geometry?.Diagnostics,
                },
            boardPlacement = boardTransform is null && boardOccurrence is null
                ? null
                : new
                {
                    supported = boardTransform?.IsSupported,
                    translationXMm = boardTransform?.TranslationXMm,
                    translationYMm = boardTransform?.TranslationYMm,
                    translationZMm = boardTransform?.TranslationZMm,
                    expectedTopSurfaceZMm = boardTransform?.ExpectedTopSurfaceZMm,
                    resultingTopSurfaceZMm = boardTransform?.ResultingTopSurfaceZMm,
                    transform = boardTransform?.Transform is { } matrix ? MatrixArray(matrix) : null,
                    diagnostics = boardTransform?.Diagnostics ?? Array.Empty<SolidWorksDiagnostic>(),
                },
            assembly,
            placement = placementPlan is null
                ? null
                : new
                {
                    requestedDesignators = placementPlan.RequestedDesignators,
                    selectedDesignators = placementPlan.SelectedDesignators,
                    missingDesignators = placementPlan.MissingDesignators,
                    supportedBodyReferences = placementPlan.SupportedBodyReferences,
                    unsupportedBodyReferences = placementPlan.UnsupportedBodyReferences,
                    boardOriginMm = placementPlan.BoardOriginMm,
                    requests = placementPlan.Requests.Select(PlacementRequestReport).ToArray(),
                    diagnostics = placementPlan.Diagnostics,
                },
            placedAssembly = placedAssembly is null ? null : PlacedAssemblyReport(placedAssembly),
            baselineSnapshot = SnapshotReport(baselineDocuments),
            finalSnapshot = SnapshotReport(remainingDocuments),
            baselineDocuments = baselineAvailable ? baselineDocuments.Identities : null,
            remainingDocuments = finalAvailable ? remainingDocuments.Identities : null,
            createdDocumentsRemaining,
            baselinePreserved,
            cleanupStatus = new
            {
                import = importResult.Lifecycle.CleanupStatus.ToString(),
                assembly = assembly?.Lifecycle.CleanupStatus.ToString(),
                placedAssembly = placedAssembly?.Lifecycle.CleanupStatus.ToString(),
                verified = cleanupVerified,
            },
            documentLifecycle = new
            {
                import = importResult.Lifecycle,
                assembly = assembly?.Lifecycle,
                placedAssembly = placedAssembly?.Lifecycle,
            },
            diagnostics,
        };

        File.WriteAllText(resultPath, JsonSerializer.Serialize(report, JsonOptions()));
        Console.WriteLine("mode=" + report.mode);
        Console.WriteLine("packageId=" + report.packageId);
        Console.WriteLine("assets=" + selectedPlan.Assets.Count + " imported=" + importResult.Assets.Count);
        Console.WriteLine("bodyReferences=" + selectedPlan.BodyReferences.Count + " mapped=" + importResult.BodyParts.Count);
        if (importResult.Board is not null)
        {
            var geometry = importResult.Board.Geometry?.Geometry;
            Console.WriteLine(
                "boardBodies=" + importResult.Board.ReopenedBodyCount +
                " boardBounds=" + (geometry is null ? "unavailable" : $"{geometry.WidthMm:R}x{geometry.HeightMm:R}x{geometry.ThicknessMm:R}") +
                " boardVolume=" + (geometry is null ? "unavailable" : geometry.VolumeMm3.ToString("R")) +
                " geometryValid=" + (importResult.Board.Geometry?.IsValid ?? false));
        }
        Console.WriteLine("revision=" + revision + " build=" + build);
        Console.WriteLine("result=" + resultPath);
        foreach (var diagnostic in diagnostics)
            Console.WriteLine(diagnostic.Severity + " " + diagnostic.Code + " " + diagnostic.Path + ": " + diagnostic.Message);
        if (assembly is not null)
            Console.WriteLine("assemblyComponents=" + assembly.ComponentCount + " reopened=" + assembly.ReopenedComponentCount);
        if (placedAssembly is not null)
        {
            Console.WriteLine(
                "placedOccurrences=" + placedAssembly.AddedOccurrences +
                " reopened=" + placedAssembly.ReopenedOccurrences +
                " transformsVerified=" + placedAssembly.TransformsVerified);
            if (placedAssembly.BoardOccurrence is not null)
            {
                Console.WriteLine(
                    "boardOccurrenceFixed=" + placedAssembly.BoardOccurrence.Fixed +
                    " boardVerified=" + placedAssembly.BoardOccurrence.Verified +
                    " boardResidual=" + placedAssembly.BoardOccurrence.TranslationResidualMm.ToString("R"));
            }
        }

        Console.WriteLine("success=" + success);
        Console.WriteLine(
            "baseline=" + baselineDocuments.Status + "/" + baselineDocuments.Count +
            " final=" + remainingDocuments.Status + "/" + remainingDocuments.Count +
            " createdDocumentsRemaining=" + (createdDocumentsRemaining is null ? "unknown" : createdDocumentsRemaining.Length.ToString()) +
            " baselinePreserved=" + (baselinePreserved is null ? "unknown" : baselinePreserved.Value.ToString()) +
            " cleanup=" + importResult.Lifecycle.CleanupStatus +
            (assembly is null ? string.Empty : "/" + assembly.Lifecycle.CleanupStatus) +
            (placedAssembly is null ? string.Empty : "/" + placedAssembly.Lifecycle.CleanupStatus));
        return success ? 0 : 1;
    }

    private static object PlacementRequestReport(SolidWorksPlacementRequest request) =>
        new
        {
            componentId = request.ComponentId,
            designator = request.Designator,
            bodyOrdinal = request.BodyOrdinal,
            assetPath = request.AssetPath,
            nativePartPath = request.NativePartPath,
            transform = MatrixArray(request.Transform),
            modelState = new
            {
                rx = request.ModelState.Rx,
                ry = request.ModelState.Ry,
                rz = request.ModelState.Rz,
                dzMm = request.ModelState.DzMm,
            },
            modelStateSource = request.ModelStateSource,
            bodyProjection = request.BodyProjection.ToString(),
            componentSide = request.ComponentSide.ToString(),
            standoffHeightMm = request.StandoffHeightMm,
            overallHeightMm = request.OverallHeightMm,
            modelRotationDegRaw = request.ModelRotationDegRaw,
            modelOriginBoardMm = new { x = request.ModelOriginBoardMm.X, y = request.ModelOriginBoardMm.Y },
            localModelOriginOffsetMm = new
            {
                x = request.LocalModelOriginOffsetMm.X,
                y = request.LocalModelOriginOffsetMm.Y,
            },
            boardOriginMm = new { x = request.BoardOriginMm.X, y = request.BoardOriginMm.Y },
        };

    private static object PlacedAssemblyReport(SolidWorksPlacedAssemblyResult result) =>
        new
        {
            assemblyPath = result.AssemblyPath,
            requestedOccurrences = result.RequestedOccurrences,
            addedOccurrences = result.AddedOccurrences,
            reopenedOccurrences = result.ReopenedOccurrences,
            saved = result.Saved,
            reopened = result.Reopened,
            transformsVerified = result.TransformsVerified,
            occurrences = result.Occurrences.Select(item => new
            {
                componentId = item.ComponentId,
                designator = item.Designator,
                bodyOrdinal = item.BodyOrdinal,
                nativePartPath = item.NativePartPath,
                occurrenceName = item.OccurrenceName,
                requestedTransform = MatrixArray(item.RequestedTransform),
                readbackTransform = MatrixArray(item.ReadbackTransform),
                translationResidualMm = item.TranslationResidualMm,
                rotationBasisResidual = item.RotationBasisResidual,
                determinant = item.Determinant,
                verified = item.Verified,
            }).ToArray(),
            diagnostics = result.Diagnostics,
            lifecycle = result.Lifecycle,
            board = result.BoardOccurrence is null
                ? null
                : new
                {
                    nativePartPath = result.BoardOccurrence.NativePartPath,
                    occurrenceName = result.BoardOccurrence.OccurrenceName,
                    requestedTransform = MatrixArray(result.BoardOccurrence.RequestedTransform),
                    readbackTransform = MatrixArray(result.BoardOccurrence.ReadbackTransform),
                    translationResidualMm = result.BoardOccurrence.TranslationResidualMm,
                    rotationBasisResidual = result.BoardOccurrence.RotationBasisResidual,
                    determinant = result.BoardOccurrence.Determinant,
                    fixedFlag = result.BoardOccurrence.Fixed,
                    verified = result.BoardOccurrence.Verified,
                },
        };

    private static double[] MatrixArray(Matrix4x4D matrix) =>
        new[]
        {
            matrix[0, 0], matrix[0, 1], matrix[0, 2], matrix[0, 3],
            matrix[1, 0], matrix[1, 1], matrix[1, 2], matrix[1, 3],
            matrix[2, 0], matrix[2, 1], matrix[2, 2], matrix[2, 3],
            matrix[3, 0], matrix[3, 1], matrix[3, 2], matrix[3, 3],
        };

    private static object SnapshotReport(SolidWorksDocumentSnapshot snapshot) =>
        new
        {
            status = snapshot.Status.ToString(),
            count = snapshot.Count,
            documents = snapshot.Identities,
            message = snapshot.Message,
        };

    private static SolidWorksDocumentSnapshot ReadOpenDocuments(ISldWorks solidWorks)
    {
        object? raw;
        try
        {
            raw = solidWorks.GetDocuments();
        }
        catch (Exception ex)
        {
            return SolidWorksDocumentSnapshot.Unavailable(Describe("GetDocuments", ex));
        }

        ModelDoc2[] documents;
        var unexpectedEntry = false;
        switch (raw)
        {
            case null:
                documents = Array.Empty<ModelDoc2>();
                break;
            case object[] array:
                documents = array.OfType<ModelDoc2>().ToArray();
                unexpectedEntry = documents.Length != array.Length;
                break;
            case ModelDoc2 single:
                documents = new[] { single };
                break;
            default:
                return SolidWorksDocumentSnapshot.Incomplete(
                    Array.Empty<string>(),
                    "GetDocuments returned an unsupported value.");
        }

        var identities = new List<string>();
        var incomplete = unexpectedEntry;
        string? message = unexpectedEntry
            ? "GetDocuments returned at least one unsupported entry."
            : null;
        foreach (var document in documents)
        {
            try
            {
                var path = document.GetPathName();
                var title = document.GetTitle();
                var identity = string.IsNullOrWhiteSpace(path)
                    ? (string.IsNullOrWhiteSpace(title) ? null : "title:" + title)
                    : SolidWorksPath.Normalize(path) ?? path;
                if (identity is null)
                {
                    incomplete = true;
                    message = "At least one open document has no usable identity.";
                    continue;
                }

                identities.Add(identity);
            }
            catch (Exception ex)
            {
                if (SolidWorksComFailure.IsUnavailable(ex))
                {
                    return SolidWorksDocumentSnapshot.Unavailable(Describe("GetDocuments identity", ex));
                }

                incomplete = true;
                message = Describe("GetDocuments identity", ex);
            }
        }

        identities.Sort(StringComparer.OrdinalIgnoreCase);
        return incomplete
            ? SolidWorksDocumentSnapshot.Incomplete(identities, message ?? "At least one open document could not be identified.")
            : SolidWorksDocumentSnapshot.Available(identities);
    }

    private static string Describe(string operation, Exception ex) =>
        operation + " failed " + ex.GetType().Name + " 0x" +
        ex.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + " " + ex.Message;

    private static IReadOnlyList<string> SelectSampleAssets(SolidWorksImportPlan plan)
    {
        var selected = new List<string>();
        var references = plan.BodyReferences;

        AddFirst(references, body =>
            body.Representation == ComponentModelRepresentation.Generic
            && body.SourceOrigin == ComponentModelSourceOrigin.Embedded);
        AddFirst(references, body =>
            body.Representation == ComponentModelRepresentation.Native
            && body.SourceOrigin == ComponentModelSourceOrigin.Native);

        var multiBody = references
            .GroupBy(body => body.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (multiBody is not null)
        {
            foreach (var body in multiBody)
                Add(body.AssetPath);
        }

        foreach (var body in references)
            Add(body.AssetPath);

        return selected.Take(3).ToArray();

        void AddFirst(IReadOnlyList<SolidWorksBodyReference> source, Func<SolidWorksBodyReference, bool> predicate)
        {
            var match = source.FirstOrDefault(predicate);
            if (match is not null)
                Add(match.AssetPath);
        }

        void Add(string assetPath)
        {
            if (!string.IsNullOrWhiteSpace(assetPath) && !selected.Contains(assetPath, StringComparer.Ordinal))
                selected.Add(assetPath);
        }
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };

    private sealed record SmokeOptions(
        string PackageDirectory,
        string OutputDirectory,
        string? AssemblyTemplate,
        bool Sample,
        bool Assembly,
        bool PlacedAssembly,
        string? OnlyAsset,
        IReadOnlyList<string>? Designators,
        int MaxComponents,
        string? Error)
    {
        public static SmokeOptions Parse(string[] args)
        {
            string? package = null;
            string? output = null;
            string? template = null;
            string? onlyAsset = null;
            string? designatorsValue = null;
            var sample = false;
            var assembly = false;
            var placedAssembly = false;
            var maxComponents = 20;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--package":
                        package = Next(args, ref i);
                        break;
                    case "--output":
                        output = Next(args, ref i);
                        break;
                    case "--template":
                        template = Next(args, ref i);
                        break;
                    case "--asset":
                    case "--only-asset":
                        onlyAsset = Next(args, ref i);
                        break;
                    case "--sample":
                        sample = true;
                        break;
                    case "--assembly":
                        assembly = true;
                        break;
                    case "--placed-assembly":
                        placedAssembly = true;
                        break;
                    case "--designators":
                        designatorsValue = Next(args, ref i);
                        break;
                    case "--max-components":
                        if (!int.TryParse(
                                Next(args, ref i),
                                System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out maxComponents)
                            || maxComponents <= 0)
                        {
                            return new SmokeOptions("", "", null, false, false, false, null, null, 0,
                                "--max-components must be a positive integer.");
                        }

                        break;
                    default:
                        return new SmokeOptions("", "", null, false, false, false, null, null, 0,
                            "Unknown argument: " + args[i]);
                }
            }

            if (string.IsNullOrWhiteSpace(package))
                return new SmokeOptions("", "", template, sample, assembly, placedAssembly, onlyAsset,
                    ParseDesignators(designatorsValue), maxComponents, "--package is required.");
            if (!Directory.Exists(package))
                return new SmokeOptions(package, output ?? "", template, sample, assembly, placedAssembly, onlyAsset,
                    ParseDesignators(designatorsValue), maxComponents, "Package directory does not exist.");
            if (placedAssembly && string.IsNullOrWhiteSpace(template))
                return new SmokeOptions(package, output ?? "", template, sample, assembly, placedAssembly, onlyAsset,
                    ParseDesignators(designatorsValue), maxComponents, "--placed-assembly requires --template <asmdot>.");

            output ??= Path.Combine(
                Path.GetTempPath(),
                "EcadMcadBridge",
                placedAssembly ? "BRIDGE-000019" : "BRIDGE-000015",
                "smoke-" + Guid.NewGuid().ToString("N"));
            return new SmokeOptions(package, output, template, sample, assembly, placedAssembly, onlyAsset,
                ParseDesignators(designatorsValue), maxComponents, null);
        }

        private static IReadOnlyList<string>? ParseDesignators(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
        }

        private static string? Next(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
                return null;
            index++;
            return args[index];
        }
    }
}
