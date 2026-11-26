using System.Security.Cryptography;
using System.Text;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;

namespace EcadMcadBridge.SolidWorks.Importing;

public enum SolidWorksDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record SolidWorksDiagnostic(
    string Code,
    SolidWorksDiagnosticSeverity Severity,
    string Path,
    string Message);

/// <summary>A body reference in the package, ready for native-part mapping.</summary>
public sealed record SolidWorksBodyReference(
    string ComponentId,
    string Designator,
    int BodyOrdinal,
    string AssetPath,
    ComponentModelRepresentation Representation,
    ComponentModelSourceOrigin SourceOrigin);

/// <summary>One unique STEP asset and all body references that point to it.</summary>
public sealed record SolidWorksStepAsset(
    string AssetPath,
    string FullPath,
    string NativePartPath,
    string AssetName,
    IReadOnlyList<SolidWorksBodyReference> BodyReferences);

/// <summary>The board substrate STEP asset, imported as a separate native part.</summary>
public sealed record SolidWorksBoardAsset(
    string AssetPath,
    string FullPath,
    string NativePartPath,
    long ExpectedSize);

public sealed record SolidWorksImportPlan(
    IReadOnlyList<SolidWorksStepAsset> Assets,
    IReadOnlyList<SolidWorksBodyReference> BodyReferences,
    IReadOnlyList<SolidWorksDiagnostic> Diagnostics)
{
    /// <summary>Board substrate asset; null for packages without board.model.json.</summary>
    public SolidWorksBoardAsset? BoardAsset { get; init; }

    /// <summary>Snapshot that produced the plan; required when <see cref="BoardAsset"/> is set.</summary>
    public BoardSnapshot? Snapshot { get; init; }

    public bool IsValid => Diagnostics.All(item => item.Severity != SolidWorksDiagnosticSeverity.Error);

    public IReadOnlyList<SolidWorksDiagnostic> Errors =>
        Diagnostics.Where(item => item.Severity == SolidWorksDiagnosticSeverity.Error).ToArray();

    public IReadOnlyList<SolidWorksDiagnostic> Warnings =>
        Diagnostics.Where(item => item.Severity == SolidWorksDiagnosticSeverity.Warning).ToArray();

    /// <summary>Returns a plan containing only the requested content-addressed assets.</summary>
    public SolidWorksImportPlan SelectAssets(IEnumerable<string> assetPaths)
    {
        var requested = new HashSet<string>(assetPaths, StringComparer.Ordinal);
        var assets = Assets
            .Where(asset => requested.Contains(asset.AssetPath))
            .OrderBy(asset => asset.AssetPath, StringComparer.Ordinal)
            .ToArray();
        var assetSet = new HashSet<string>(assets.Select(asset => asset.AssetPath), StringComparer.Ordinal);
        var references = BodyReferences
            .Where(body => assetSet.Contains(body.AssetPath))
            .OrderBy(body => body.ComponentId, StringComparer.Ordinal)
            .ThenBy(body => body.BodyOrdinal)
            .ToArray();
        var relevantPaths = new HashSet<string>(assets.Select(asset => asset.AssetPath), StringComparer.Ordinal);
        var diagnostics = Diagnostics
            .Where(item => string.IsNullOrEmpty(item.Path) || !item.Path.StartsWith("assets/3d/", StringComparison.Ordinal)
                || relevantPaths.Contains(item.Path))
            .ToArray();
        return new SolidWorksImportPlan(assets, references, diagnostics)
        {
            BoardAsset = BoardAsset,
            Snapshot = Snapshot,
        };
    }
}

/// <summary>Builds a deterministic native-import plan from an already validated Exchange Package.</summary>
public static class SolidWorksImportPlanBuilder
{
    public static SolidWorksImportPlan Build(
        PackageReadResult package,
        string packageDirectory,
        string nativeOutputDirectory)
    {
        if (package is null)
            throw new ArgumentNullException(nameof(package));

        var diagnostics = new List<SolidWorksDiagnostic>();
        if (!package.IsSuccess || package.Snapshot is null)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-PLAN-001",
                SolidWorksDiagnosticSeverity.Error,
                "package",
                "Exchange Package was not read successfully."));
            return new SolidWorksImportPlan(
                Array.Empty<SolidWorksStepAsset>(),
                Array.Empty<SolidWorksBodyReference>(),
                diagnostics);
        }

        return Build(
            package.Snapshot,
            package.Models,
            packageDirectory,
            nativeOutputDirectory,
            package.BoardModel);
    }

    public static SolidWorksImportPlan Build(
        BoardSnapshot snapshot,
        ComponentModelCatalog? models,
        string packageDirectory,
        string nativeOutputDirectory,
        BoardModelDescriptor? boardModel = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<SolidWorksDiagnostic>();
        var assets = new Dictionary<string, SolidWorksStepAsset>(StringComparer.Ordinal);
        var bodyReferences = new List<SolidWorksBodyReference>();
        var usedNativeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (models is null)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-PLAN-002",
                SolidWorksDiagnosticSeverity.Error,
                "models",
                "Package has no component model catalog."));
            return new SolidWorksImportPlan(
                Array.Empty<SolidWorksStepAsset>(),
                Array.Empty<SolidWorksBodyReference>(),
                diagnostics);
        }

        if (string.IsNullOrWhiteSpace(packageDirectory) || !Directory.Exists(packageDirectory))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-PLAN-003",
                SolidWorksDiagnosticSeverity.Error,
                "package",
                "Exchange Package directory is missing."));
            return new SolidWorksImportPlan(
                Array.Empty<SolidWorksStepAsset>(),
                Array.Empty<SolidWorksBodyReference>(),
                diagnostics);
        }

        if (string.IsNullOrWhiteSpace(nativeOutputDirectory))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-PLAN-004",
                SolidWorksDiagnosticSeverity.Error,
                "output",
                "Native part output directory is required."));
            return new SolidWorksImportPlan(
                Array.Empty<SolidWorksStepAsset>(),
                Array.Empty<SolidWorksBodyReference>(),
                diagnostics);
        }

        var packageRoot = Path.GetFullPath(packageDirectory);
        var outputRoot = Path.GetFullPath(nativeOutputDirectory);
        var components = snapshot.Components.ToDictionary(component => component.Id, StringComparer.Ordinal);

        SolidWorksBoardAsset? boardAsset = null;
        if (boardModel is not null)
        {
            boardAsset = BuildBoardAsset(
                boardModel,
                packageRoot,
                outputRoot,
                diagnostics);
        }

        foreach (var component in models.Components ?? Array.Empty<ComponentModelEntry>())
        {
            if (component is null)
                continue;

            if (!components.TryGetValue(component.ComponentId, out var snapshotComponent))
            {
                diagnostics.Add(new SolidWorksDiagnostic(
                    "SW-PLAN-005",
                    SolidWorksDiagnosticSeverity.Error,
                    component.ComponentId,
                    "Model catalog references a component absent from the snapshot."));
                continue;
            }

            foreach (var body in component.Bodies ?? Array.Empty<ComponentBodyEntry>())
            {
                if (body is null || body.AssetPath is null)
                {
                    diagnostics.Add(new SolidWorksDiagnostic(
                        "SW-PLAN-006",
                        SolidWorksDiagnosticSeverity.Warning,
                        component.ComponentId,
                        "Body has no STEP asset; it is skipped by the SOLIDWORKS import plan."));
                    continue;
                }

                if (body.AssetFormat != ComponentModelAssetFormat.StepAp214)
                {
                    diagnostics.Add(new SolidWorksDiagnostic(
                        "SW-PLAN-007",
                        SolidWorksDiagnosticSeverity.Error,
                        body.AssetPath,
                        "Only step-ap214 assets are supported by the SOLIDWORKS import plan."));
                    continue;
                }

                if (!TryResolveAssetPath(packageRoot, body.AssetPath, out var fullPath))
                {
                    diagnostics.Add(new SolidWorksDiagnostic(
                        "SW-PLAN-008",
                        SolidWorksDiagnosticSeverity.Error,
                        body.AssetPath,
                        "Asset path is not a safe relative package path."));
                    continue;
                }

                if (!File.Exists(fullPath) || new FileInfo(fullPath).Length <= 0)
                {
                    diagnostics.Add(new SolidWorksDiagnostic(
                        "SW-PLAN-009",
                        SolidWorksDiagnosticSeverity.Error,
                        body.AssetPath,
                        "STEP asset is missing or empty."));
                    continue;
                }

                if (!assets.TryGetValue(body.AssetPath, out var asset))
                {
                    var nativeName = BuildNativeName(body.AssetPath, usedNativeNames);
                    asset = new SolidWorksStepAsset(
                        body.AssetPath,
                        fullPath,
                        Path.Combine(outputRoot, nativeName),
                        Path.GetFileNameWithoutExtension(nativeName),
                        new List<SolidWorksBodyReference>());
                    assets[body.AssetPath] = asset;
                }

                var reference = new SolidWorksBodyReference(
                    component.ComponentId,
                    snapshotComponent.Designator,
                    body.BodyOrdinal,
                    body.AssetPath,
                    body.Representation,
                    body.SourceOrigin);
                bodyReferences.Add(reference);
                ((List<SolidWorksBodyReference>)asset.BodyReferences).Add(reference);
            }
        }

        var orderedAssets = assets.Values
            .OrderBy(asset => asset.AssetPath, StringComparer.Ordinal)
            .ToArray();
        var orderedReferences = bodyReferences
            .OrderBy(body => body.ComponentId, StringComparer.Ordinal)
            .ThenBy(body => body.BodyOrdinal)
            .ToArray();
        return new SolidWorksImportPlan(orderedAssets, orderedReferences, diagnostics)
        {
            BoardAsset = boardAsset,
            Snapshot = snapshot,
        };
    }

    private static SolidWorksBoardAsset? BuildBoardAsset(
        BoardModelDescriptor boardModel,
        string packageRoot,
        string outputRoot,
        List<SolidWorksDiagnostic> diagnostics)
    {
        if (boardModel.AssetFormat != ComponentModelAssetFormat.StepAp214)
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-PLAN-030",
                SolidWorksDiagnosticSeverity.Error,
                "board.model.json",
                "Only step-ap214 board assets are supported by the SOLIDWORKS import plan."));
            return null;
        }

        if (boardModel.AssetPath is null
            || !boardModel.AssetPath.StartsWith("assets/board/", StringComparison.Ordinal)
            || !TryResolveAssetPath(packageRoot, boardModel.AssetPath, out var fullPath))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-PLAN-031",
                SolidWorksDiagnosticSeverity.Error,
                "board.model.json",
                "Board asset path is not a safe assets/board relative package path."));
            return null;
        }

        if (!File.Exists(fullPath))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-PLAN-032",
                SolidWorksDiagnosticSeverity.Error,
                boardModel.AssetPath,
                "Board STEP asset is missing."));
            return null;
        }

        var size = new FileInfo(fullPath).Length;
        if (size <= 0 || (boardModel.Size > 0 && size != boardModel.Size))
        {
            diagnostics.Add(new SolidWorksDiagnostic(
                "SW-PLAN-032",
                SolidWorksDiagnosticSeverity.Error,
                boardModel.AssetPath,
                "Board STEP asset size does not match the descriptor."));
            return null;
        }

        return new SolidWorksBoardAsset(
            boardModel.AssetPath,
            fullPath,
            Path.Combine(outputRoot, "board.SLDPRT"),
            boardModel.Size);
    }

    public static string BuildNativeName(string assetPath, ISet<string>? usedNames = null)
    {
        var fileName = Path.GetFileNameWithoutExtension(assetPath);
        var safe = fileName.Length == 64 && fileName.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? fileName
            : "asset-" + Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(assetPath)))
                .ToLowerInvariant()[..16];
        if (string.IsNullOrWhiteSpace(safe))
            safe = "asset";

        var candidate = safe + ".SLDPRT";
        if (usedNames is null)
            return candidate;

        var suffix = 1;
        while (!usedNames.Add(candidate))
        {
            candidate = safe + "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".SLDPRT";
            suffix++;
        }

        return candidate;
    }

    private static bool TryResolveAssetPath(string packageRoot, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (relativePath.Contains("\\", StringComparison.Ordinal)
            || relativePath.Contains(":", StringComparison.Ordinal)
            || relativePath.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var segments = relativePath.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            return false;

        var candidate = Path.GetFullPath(
            Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = packageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        fullPath = candidate;
        return true;
    }
}
