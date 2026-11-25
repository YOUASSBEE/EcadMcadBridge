using System.Diagnostics;
using System.Text.Json;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using EcadMcadBridge.SolidWorks.Assemblies;
using EcadMcadBridge.SolidWorks.Importing;
using EcadMcadBridge.SolidWorks.Interop;
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
        if (options.OnlyAsset is not null)
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

        total.Stop();
        var remainingDocuments = ReadOpenDocuments(solidWorks);
        var baselineSet = new HashSet<string>(baselineDocuments, StringComparer.OrdinalIgnoreCase);
        var createdDocumentsRemaining = remainingDocuments
            .Where(item => !baselineSet.Contains(item))
            .ToArray();
        var baselinePreserved = baselineDocuments.All(item =>
            remainingDocuments.Contains(item, StringComparer.OrdinalIgnoreCase));
        var diagnostics = plan.Diagnostics
            .Concat(importResult.Diagnostics)
            .Concat(assembly?.Diagnostics ?? Array.Empty<SolidWorksDiagnostic>())
            .ToArray();
        var importedAll = importResult.Assets.Count == selectedPlan.Assets.Count
            && importResult.Assets.All(asset =>
                asset.SaveSucceeded
                && asset.ReopenSucceeded
                && asset.ImportedBodyCount == asset.ReopenedBodyCount
                && asset.ReopenedBodyCount > 0);
        var mappedAll = importResult.BodyParts.Count == selectedPlan.BodyReferences.Count;
        var assemblyOk = assembly is null || (assembly.Saved && assembly.Reopened);
        var success = plan.IsValid
            && importResult.Assets.Count == selectedPlan.Assets.Count
            && importedAll
            && mappedAll
            && assemblyOk
            && baselinePreserved
            && createdDocumentsRemaining.Length == 0
            && diagnostics.All(item => item.Severity != SolidWorksDiagnosticSeverity.Error);

        var report = new
        {
            success,
            mode = options.Sample ? "sample" : "all",
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
            assembly,
            baselineDocuments,
            remainingDocuments,
            createdDocumentsRemaining,
            baselinePreserved,
            documentLifecycle = new
            {
                import = importResult.Lifecycle,
                assembly = assembly?.Lifecycle,
            },
            diagnostics,
        };

        File.WriteAllText(resultPath, JsonSerializer.Serialize(report, JsonOptions()));
        Console.WriteLine("mode=" + report.mode);
        Console.WriteLine("packageId=" + report.packageId);
        Console.WriteLine("assets=" + selectedPlan.Assets.Count + " imported=" + importResult.Assets.Count);
        Console.WriteLine("bodyReferences=" + selectedPlan.BodyReferences.Count + " mapped=" + importResult.BodyParts.Count);
        Console.WriteLine("revision=" + revision + " build=" + build);
        Console.WriteLine("result=" + resultPath);
        foreach (var diagnostic in diagnostics)
            Console.WriteLine(diagnostic.Severity + " " + diagnostic.Code + " " + diagnostic.Path + ": " + diagnostic.Message);
        if (assembly is not null)
            Console.WriteLine("assemblyComponents=" + assembly.ComponentCount + " reopened=" + assembly.ReopenedComponentCount);
        Console.WriteLine("success=" + success);
        Console.WriteLine("baselineDocuments=" + baselineDocuments.Count + " remainingDocuments=" + remainingDocuments.Count + " createdDocumentsRemaining=" + createdDocumentsRemaining.Length + " baselinePreserved=" + baselinePreserved);
        return success ? 0 : 1;
    }

    private static IReadOnlyList<string> ReadOpenDocuments(ISldWorks solidWorks)
    {
        try
        {
            var raw = solidWorks.GetDocuments();
            var documents = raw switch
            {
                object[] array => array.OfType<ModelDoc2>().ToArray(),
                ModelDoc2 single => new[] { single },
                _ => Array.Empty<ModelDoc2>(),
            };
            return documents
                .Select(document =>
                {
                    var path = document.GetPathName();
                    return string.IsNullOrWhiteSpace(path)
                        ? "title:" + document.GetTitle()
                        : SolidWorksPath.Normalize(path) ?? path;
                })
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

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
        string? OnlyAsset,
        string? Error)
    {
        public static SmokeOptions Parse(string[] args)
        {
            string? package = null;
            string? output = null;
            string? template = null;
            string? onlyAsset = null;
            var sample = false;
            var assembly = false;
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
                        onlyAsset = Next(args, ref i);
                        break;
                    case "--sample":
                        sample = true;
                        break;
                    case "--assembly":
                        assembly = true;
                        break;
                    default:
                        return new SmokeOptions("", "", null, false, false, null, "Unknown argument: " + args[i]);
                }
            }

            if (string.IsNullOrWhiteSpace(package))
                return new SmokeOptions("", "", template, sample, assembly, onlyAsset, "--package is required.");
            if (!Directory.Exists(package))
                return new SmokeOptions(package, output ?? "", template, sample, assembly, onlyAsset, "Package directory does not exist.");

            output ??= Path.Combine(
                Path.GetTempPath(),
                "EcadMcadBridge",
                "BRIDGE-000015",
                "smoke-" + Guid.NewGuid().ToString("N"));
            return new SmokeOptions(package, output, template, sample, assembly, onlyAsset, null);
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
