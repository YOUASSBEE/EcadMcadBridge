using System.Globalization;
using System.IO;
using System.Text.Json;
using DXP;
using EcadMcadBridge.Altium.Application;
using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using PCB;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>
/// Read-only research diagnostic for D-024 identity/origin inputs. It never exports STEP,
/// never writes a PCB document and never substitutes bounding-box coordinates for a model origin.
/// </summary>
internal static class AltiumBodyIdentityDiagnosticsReader
{
    public const string SchemaVersion = "bridge-readonly-diagnostics/1";

    internal sealed record Request(
        string PackageDirectory,
        IReadOnlyList<string> LibraryPaths,
        IReadOnlyList<string> Designators,
        string OutputPath);

    public static object Read(IServerDocumentView view, Request request)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(request);

        var document = view.GetOwnerDocument();
        if (!AltiumDocumentStateReader.TryRead(document, out var source, out var stateError) || source is null)
            return Error("E_NO_ACTIVE_BOARD", stateError);

        var data = AltiumBoardSnapshotAdapter.Read(view);
        var resolver = AltiumComponentIdentityResolver.Build(data.ComponentIdentities);
        var ordinals = new AltiumBodyOrdinalTracker();
        var package = PackageAssetIndex.Build(request.PackageDirectory);
        var libraries = FootprintLibraryIndex.Build(request.LibraryPaths);
        var wanted = new HashSet<string>(
            request.Designators.Where(value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);

        var bodies = new List<object>();
        var totalBodies = 0;
        var selectedBodies = 0;
        var iterator = data.Board.BoardIterator_Create();
        if (iterator is null)
            return Error("E_BODY_ITERATOR", "The component body iterator is unavailable.");

        try
        {
            iterator.AddFilter_ObjectSet(new TObjectSet { TObjectId.eComponentBodyObject });
            iterator.AddFilter_Method(TIterationMethod.eProcessAll);

            for (var primitive = iterator.FirstPCBObject(); primitive is not null; primitive = iterator.NextPCBObject())
            {
                if (primitive is not IPCB_ComponentBody body)
                    continue;

                var entry = ReadBody(body, resolver, ordinals, package, libraries, ref totalBodies);
                var designator = TryGet(() => entry.Designator, out _) ?? string.Empty;
                if (wanted.Count > 0 && !wanted.Contains(designator))
                    continue;

                selectedBodies++;
                bodies.Add(entry.ToJson());
            }
        }
        finally
        {
            data.Board.BoardIterator_Destroy(ref iterator);
            libraries.Dispose();
        }

        var missing = wanted.Where(value => bodies.All(body =>
            !string.Equals(Field(body, "designator"), value, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new Dictionary<string, object?>
        {
            ["schemaVersion"] = SchemaVersion,
            ["status"] = missing.Length == 0 && package.Verified ? "ok" : "partial",
            ["sourceDocument"] = new Dictionary<string, object?>
            {
                ["path"] = source.FilePath,
                ["sha256OnDisk"] = TryHash(source.FilePath),
                ["modifiedInEditor"] = source.IsModified,
            },
            ["units"] = new Dictionary<string, object?>
            {
                ["rawUnit"] = "iu",
                ["millimetersPerIu"] = AltiumUnits.MillimetersPerIu,
                ["componentPositionCoordinateSystem"] = "pcb board coordinates, raw IU; rotation in degrees",
                ["modelOriginCoordinateSystem"] = "IPCB_Model.GetOrigin() model-local X/Y, raw IU; GetOrigin has no Z component",
            },
            ["package"] = package.ToJson(),
            ["libraries"] = libraries.ToJson(),
            ["bodies"] = bodies,
            ["summary"] = new Dictionary<string, object?>
            {
                ["bodiesEnumerated"] = totalBodies,
                ["bodiesReported"] = selectedBodies,
                ["missingRequestedDesignators"] = missing,
                ["requestedDesignators"] = wanted.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            },
            ["adapterWarnings"] = data.Warnings,
        };
    }

    private static string? Field(object body, string key) =>
        body is Dictionary<string, object?> map && map.TryGetValue(key, out var value)
            ? value as string
            : null;

    private static BodyObservation ReadBody(
        IPCB_ComponentBody body,
        AltiumComponentIdentityResolver resolver,
        AltiumBodyOrdinalTracker ordinals,
        PackageAssetIndex package,
        FootprintLibraryIndex libraries,
        ref int totalBodies)
    {
        totalBodies++;
        var diagnostics = new List<string>();
        var owner = TryGet(body.GetState_Component, out var ownerError);
        if (owner is null)
        {
            diagnostics.Add("BODY_OWNER_UNAVAILABLE: " + ownerError);
            return new BodyObservation(string.Empty, null, null, null, null, null, null, diagnostics);
        }

        var designator = TryGet(() => owner.GetState_Name()?.GetState_Text(), out _) ?? string.Empty;
        var pattern = TryGet(owner.GetState_Pattern, out _) ?? string.Empty;
        var sourceLibrary = TryGet(owner.GetState_SourceFootprintLibrary, out _) ?? string.Empty;
        var placedSourceUniqueId = Normalize(TryGet(owner.GetState_SourceUniqueId, out _));
        var placedUniqueId = Normalize(TryGet(owner.GetState_UniqueId, out _));

        var key = new AltiumComponentKey(
            placedUniqueId,
            placedSourceUniqueId,
            designator,
            TryGetValue(owner.GetState_XLocation, out _) ?? 0,
            TryGetValue(owner.GetState_YLocation, out _) ?? 0,
            TryGetValue(owner.GetState_Rotation, out _) ?? 0,
            AltiumBoardSnapshotAdapter.ResolveComponentSide(owner));
        var resolution = resolver.Resolve(key);
        string? componentId = null;
        if (resolution.Status == AltiumComponentResolutionStatus.Resolved)
            componentId = resolution.ComponentId;
        else
            diagnostics.Add("COMPONENT_ID_" + resolution.Status.ToString().ToUpperInvariant());

        int? bodyOrdinal = componentId is null ? null : ordinals.Next(componentId);
        if (bodyOrdinal is null)
            diagnostics.Add("BODY_ORDINAL_UNAVAILABLE");

        var placedBodyUniqueId = Normalize(TryGet(body.GetState_UniqueId, out _));
        var model = TryGet(body.GetModel, out var modelError);
        if (model is null)
            diagnostics.Add("MODEL_UNAVAILABLE: " + modelError);

        var originError = string.Empty;
        var origin = model is null ? null : TryGet(model.GetOrigin, out originError);
        if (origin is null && model is not null)
            diagnostics.Add("MODEL_ORIGIN_UNAVAILABLE: " + (string.IsNullOrWhiteSpace(originError) ? "GetOrigin returned null." : originError));
        diagnostics.Add("MODEL_ORIGIN_Z_UNAVAILABLE: IPCB_Model.GetOrigin returns ICoordPoint (X/Y only).");

        var componentPositionXIu = TryGetValue(owner.GetState_XLocation, out _);
        var componentPositionYIu = TryGetValue(owner.GetState_YLocation, out _);
        var rotation = TryGetValue(owner.GetState_Rotation, out _);
        var side = AltiumBoardSnapshotAdapter.ResolveComponentSide(owner).ToString();

        var footprint = libraries.Match(pattern, sourceLibrary);
        diagnostics.AddRange(footprint.Diagnostics);

        string? footprintSourceUniqueId = null;
        string footprintSourceStatus = "unavailable";
        if (footprint.Status == "available" && footprint.FootprintSourceUniqueId is not null)
        {
            footprintSourceUniqueId = footprint.FootprintSourceUniqueId;
            footprintSourceStatus = "available";
        }

        string? bodyUniqueId = null;
        string bodyUniqueIdStatus = "unavailable";
        var libraryBodyId = bodyOrdinal is null ? null : footprint.BodyUniqueIds.GetValueOrDefault(bodyOrdinal.Value);
        if (footprint.Status == "available" && libraryBodyId is not null && placedBodyUniqueId is not null)
        {
            if (string.Equals(placedBodyUniqueId, libraryBodyId, StringComparison.Ordinal))
            {
                bodyUniqueId = libraryBodyId;
                bodyUniqueIdStatus = "available";
            }
            else
            {
                bodyUniqueIdStatus = "ambiguous";
                diagnostics.Add(
                    "PLACED_BODY_ID_DIFFERS_FROM_LIBRARY: placed=" + placedBodyUniqueId +
                    " library=" + libraryBodyId);
            }
        }
        else
        {
            diagnostics.Add(placedBodyUniqueId is null
                ? "PLACED_BODY_ID_UNAVAILABLE: library body ID cannot be proven stable for this instance."
                : "BODY_ID_NOT_PROVEN_LIBRARY_STABLE");
        }

        var asset = package.FindAsset(designator);
        diagnostics.AddRange(asset.Diagnostics);

        return new BodyObservation(
            designator,
            componentId,
            bodyOrdinal,
            origin is null ? null : AltiumModelMetadataMapper.MapOrigin(origin.X, origin.Y),
            componentPositionXIu.HasValue && componentPositionYIu.HasValue
                ? AltiumModelMetadataMapper.MapOrigin(componentPositionXIu, componentPositionYIu)
                : null,
            rotation,
            side,
            diagnostics,
            pattern,
            sourceLibrary,
            placedSourceUniqueId,
            placedUniqueId,
            placedBodyUniqueId,
            footprint.Status,
            footprint.FootprintSourceUniqueId,
            footprintSourceStatus,
            bodyUniqueId,
            bodyUniqueIdStatus,
            libraryBodyId,
            asset);
    }

    private sealed record BodyObservation(
        string Designator,
        string? ComponentId,
        int? BodyOrdinal,
        ModelOrigin? ModelOrigin,
        ModelOrigin? ComponentPosition,
        double? RotationDeg,
        string? Side,
        List<string> Diagnostics,
        string? Pattern = null,
        string? SourceLibrary = null,
        string? PlacedSourceUniqueId = null,
        string? PlacedComponentUniqueId = null,
        string? PlacedBodyUniqueId = null,
        string? FootprintMatchStatus = null,
        string? LibraryFootprintUniqueId = null,
        string? FootprintSourceStatus = null,
        string? BodyUniqueId = null,
        string? BodyUniqueIdStatus = null,
        string? LibraryBodyUniqueId = null,
        PackageAssetMatch? Asset = null)
    {
        public Dictionary<string, object?> ToJson()
        {
            var origin = ModelOrigin is null
                ? new Dictionary<string, object?>
                {
                    ["xIu"] = null, ["yIu"] = null, ["zIu"] = null,
                    ["xMm"] = null, ["yMm"] = null, ["zMm"] = null,
                    ["status"] = "unavailable",
                    ["units"] = "iu",
                }
                : new Dictionary<string, object?>
                {
                    ["xIu"] = ModelOrigin.XIu,
                    ["yIu"] = ModelOrigin.YIu,
                    ["zIu"] = ModelOrigin.ZIu,
                    ["xMm"] = ModelOrigin.XMm,
                    ["yMm"] = ModelOrigin.YMm,
                    ["zMm"] = ModelOrigin.ZMm,
                    ["status"] = "xy-available-z-unavailable",
                    ["units"] = "iu",
                };

            return new Dictionary<string, object?>
            {
                ["designator"] = Designator,
                ["componentId"] = ComponentId,
                ["bodyOrdinal"] = BodyOrdinal,
                ["side"] = Side,
                ["rotationDeg"] = RotationDeg,
                ["footprintPattern"] = Pattern,
                ["sourceFootprintLibrary"] = SourceLibrary,
                ["placedComponentSourceUniqueId"] = PlacedSourceUniqueId,
                ["placedComponentUniqueId"] = PlacedComponentUniqueId,
                ["footprintSourceUniqueId"] = FootprintSourceStatus == "available"
                    ? LibraryFootprintUniqueId
                    : null,
                ["footprintSourceUniqueIdStatus"] = FootprintSourceStatus,
                ["footprintMatchStatus"] = FootprintMatchStatus,
                ["bodyUniqueId"] = BodyUniqueId,
                ["bodyUniqueIdStatus"] = BodyUniqueIdStatus,
                ["placedBodyUniqueId"] = PlacedBodyUniqueId,
                ["libraryBodyUniqueId"] = LibraryBodyUniqueId,
                ["modelOrigin"] = origin,
                ["componentPosition"] = ComponentPosition is null
                    ? null
                    : new Dictionary<string, object?>
                    {
                        ["xIu"] = ComponentPosition.XIu,
                        ["yIu"] = ComponentPosition.YIu,
                        ["xMm"] = ComponentPosition.XMm,
                        ["yMm"] = ComponentPosition.YMm,
                    },
                ["stepAsset"] = Asset?.ToJson(),
                ["diagnostics"] = Diagnostics,
            };
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static T? TryGet<T>(Func<T> read, out string error)
    {
        try
        {
            error = string.Empty;
            return read();
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return default;
        }
    }

    private static T? TryGetValue<T>(Func<T> read, out string error) where T : struct
    {
        try
        {
            error = string.Empty;
            return read();
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return null;
        }
    }

    private static string? TryHash(string path)
    {
        var hash = PackageFileHasher.ComputeStableSha256(path);
        return hash.IsSuccess ? hash.Sha256 : null;
    }

    private static bool TryResolve(string root, string relative, out string fullPath, out string reason)
    {
        fullPath = string.Empty;
        reason = string.Empty;
        var normalized = relative.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Split('/').Any(segment => segment == ".."))
        {
            reason = "path escapes package root";
            return false;
        }

        try
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            fullPath = Path.GetFullPath(Path.Combine(rootFull, normalized));
            if (!fullPath.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                reason = "path escapes package root";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            reason = ex.GetType().Name;
            return false;
        }
    }

    private static object Error(string code, string message) => new Dictionary<string, object?>
    {
        ["schemaVersion"] = SchemaVersion,
        ["status"] = "error",
        ["error"] = new Dictionary<string, object?> { ["code"] = code, ["message"] = message },
    };

    private sealed record PackageAssetMatch(
        string? AssetPath,
        string? Sha256,
        long? Size,
        bool Verified,
        IReadOnlyList<string> Diagnostics)
    {
        public Dictionary<string, object?> ToJson() => new()
        {
            ["assetPath"] = AssetPath,
            ["sha256"] = Sha256,
            ["size"] = Size,
            ["verified"] = Verified,
            ["diagnostics"] = Diagnostics,
        };
    }

    private sealed class PackageAssetIndex
    {
        private readonly Dictionary<string, string> _hashes = new(StringComparer.Ordinal);

        private PackageAssetIndex(string directory)
        {
            Directory = directory;
            FilesVerified = 0;
            Verified = false;
        }

        public string Directory { get; }

        public bool Verified { get; private set; }

        public int FilesVerified { get; private set; }

        public List<string> Diagnostics { get; } = new();

        public static PackageAssetIndex Build(string directory)
        {
            var index = new PackageAssetIndex(directory);
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                index.Diagnostics.Add("PACKAGE_MANIFEST_MISSING");
                return index;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                foreach (var file in document.RootElement.GetProperty("files").EnumerateArray())
                {
                    var path = file.GetProperty("path").GetString();
                    var sha = file.GetProperty("sha256").GetString();
                    if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(sha))
                        continue;

                    index._hashes[path] = sha;
                }
            }
            catch (Exception ex)
            {
                index.Diagnostics.Add("PACKAGE_MANIFEST_INVALID: " + ex.GetType().Name);
                return index;
            }

            var bad = new List<string>();
            foreach (var (relative, expected) in index._hashes)
            {
                if (!TryResolve(directory, relative, out var full, out var reason))
                {
                    index.Diagnostics.Add("PACKAGE_PATH_UNSAFE: " + relative + " " + reason);
                    bad.Add(relative);
                    continue;
                }

                var hash = PackageFileHasher.ComputeStableSha256(full);
                if (!hash.IsSuccess || !string.Equals(hash.Sha256, expected, StringComparison.Ordinal))
                {
                    bad.Add(relative);
                    continue;
                }

                index.FilesVerified++;
            }

            index.Verified = bad.Count == 0 && index._hashes.Count > 0;
            if (bad.Count > 0)
                index.Diagnostics.Add("PACKAGE_CHECKSUM_MISMATCH: " + string.Join(",", bad.OrderBy(x => x, StringComparer.Ordinal)));

            return index;
        }

        public PackageAssetMatch FindAsset(string designator)
        {
            if (string.IsNullOrWhiteSpace(designator))
                return new PackageAssetMatch(null, null, null, false, new[] { "ASSET_DESIGNATOR_UNAVAILABLE" });

            var relative = "assets/" + designator + ".step";
            if (!_hashes.TryGetValue(relative, out var expected))
                return new PackageAssetMatch(null, null, null, false, new[] { "ASSET_NOT_IN_PACKAGE_MANIFEST: " + relative });

            if (!TryResolve(Directory, relative, out var full, out var reason))
                return new PackageAssetMatch(relative, null, null, false, new[] { "ASSET_PATH_UNSAFE: " + reason });

            var hash = PackageFileHasher.ComputeStableSha256(full);
            if (!hash.IsSuccess || !string.Equals(hash.Sha256, expected, StringComparison.Ordinal))
                return new PackageAssetMatch(relative, hash.Sha256, hash.Size, false, new[] { "ASSET_CHECKSUM_MISMATCH" });

            return new PackageAssetMatch(relative, hash.Sha256, hash.Size, true, Array.Empty<string>());
        }

        public Dictionary<string, object?> ToJson() => new()
        {
            ["directory"] = Directory,
            ["verified"] = Verified,
            ["filesVerified"] = FilesVerified,
            ["filesListed"] = _hashes.Count,
            ["diagnostics"] = Diagnostics,
        };
    }

    private sealed record LibraryFootprint(
        string LibraryPath,
        string Pattern,
        string Status,
        string? FootprintSourceUniqueId,
        IReadOnlyDictionary<int, string> BodyUniqueIds,
        IReadOnlyList<string> Diagnostics);

    private sealed class FootprintLibraryIndex : IDisposable
    {
        private readonly List<(IPCB_Library Library, bool Loaded)> _opened = new();
        private readonly List<LibraryFootprint> _footprints = new();
        private readonly List<string> _diagnostics = new();

        private FootprintLibraryIndex()
        {
        }

        public static FootprintLibraryIndex Build(IReadOnlyList<string> paths)
        {
            var index = new FootprintLibraryIndex();
            var server = PCB.GlobalVars.PCBServer;
            if (server is null)
            {
                index._diagnostics.Add("PCB_SERVER_UNAVAILABLE");
                return index;
            }

            foreach (var path in paths)
            {
                index.ReadLibrary(server, path);
            }

            return index;
        }

        private void ReadLibrary(IPCB_ServerInterface server, string path)
        {
            var libraryHash = TryHash(path);
            if (libraryHash is null)
            {
                _diagnostics.Add("LIBRARY_FILE_UNAVAILABLE: " + path);
                return;
            }

            IPCB_Library? library = null;
            var loaded = false;
            try
            {
                library = TryGet(() => server.GetPCBLibraryByPath(path), out _);
                if (library is null)
                {
                    library = TryGet(() => server.LoadPCBLibraryByPath(path), out _);
                    loaded = library is not null;
                }

                if (library is null)
                {
                    _diagnostics.Add("LIBRARY_OPEN_FAILED: " + path);
                    return;
                }

                _opened.Add((library, loaded));
                var count = TryGetValue(library.ComponentCount, out _) ?? 0;
                for (var i = 0; i < count; i++)
                {
                    var component = TryGet(() => library.GetComponent(i), out var error);
                    if (component is null)
                    {
                        _diagnostics.Add("LIBRARY_COMPONENT_UNAVAILABLE: " + path + " index=" +
                                         i.ToString(CultureInfo.InvariantCulture) + " " + error);
                        continue;
                    }

                    var pattern = Normalize(TryGet(component.GetState_Pattern, out _)) ?? string.Empty;
                    var uniqueId = Normalize(TryGet(component.GetState_UniqueId, out _));
                    var bodyIds = ReadLibraryBodyIds(component);
                    _footprints.Add(new LibraryFootprint(
                        path,
                        pattern,
                        "available",
                        uniqueId,
                        bodyIds,
                        uniqueId is null
                            ? new[] { "LIBRARY_FOOTPRINT_UNIQUE_ID_UNAVAILABLE: " + path + " / " + pattern }
                            : Array.Empty<string>()));
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Add("LIBRARY_READ_FAILED: " + path + " " + ex.GetType().Name);
            }
            finally
            {
                if (library is not null && !_opened.Any(item => ReferenceEquals(item.Library, library)) && loaded)
                    TryGetValue(() => { server.DestroyPCBLibrary(ref library); return true; }, out _);
            }
        }

        private static IReadOnlyDictionary<int, string> ReadLibraryBodyIds(IPCB_LibComponent component)
        {
            var result = new Dictionary<int, string>();
            var iterator = TryGet(component.GroupIterator_Create, out var error);
            if (iterator is null)
                return result;

            try
            {
                iterator.AddFilter_ObjectSet(new TObjectSet { TObjectId.eComponentBodyObject });
                var ordinal = 0;
                for (var primitive = iterator.FirstPCBObject(); primitive is not null; primitive = iterator.NextPCBObject())
                {
                    if (primitive is not IPCB_ComponentBody body)
                        continue;
                    var uniqueId = Normalize(TryGet(body.GetState_UniqueId, out _));
                    if (uniqueId is not null)
                        result[ordinal] = uniqueId;
                    ordinal++;
                }
            }
            catch (Exception ex)
            {
                result.Clear();
                result[-1] = "ERROR:" + ex.GetType().Name;
            }
            finally
            {
                TryGet(() => { component.GroupIterator_Destroy(ref iterator); return true; }, out _);
            }

            return result;
        }

        public LibraryFootprint Match(string pattern, string sourceLibrary)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return new LibraryFootprint(string.Empty, string.Empty, "unavailable", null,
                    new Dictionary<int, string>(), new[] { "FOOTPRINT_PATTERN_UNAVAILABLE" });

            var candidates = _footprints
                .Where(item => string.Equals(item.Pattern, pattern, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 0)
                return new LibraryFootprint(string.Empty, pattern, "unavailable", null,
                    new Dictionary<int, string>(),
                    new[] { "LIBRARY_FOOTPRINT_NOT_FOUND: " + pattern + " libraryHint=" + sourceLibrary });

            var distinctIds = candidates
                .Where(item => item.FootprintSourceUniqueId is not null)
                .Select(item => item.FootprintSourceUniqueId!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (distinctIds.Length > 1)
                return new LibraryFootprint(string.Empty, pattern, "ambiguous", null,
                    new Dictionary<int, string>(),
                    new[] { "LIBRARY_FOOTPRINT_AMBIGUOUS: " + string.Join(",", distinctIds) });

            var chosen = candidates[0];
            var diagnostics = new List<string>(chosen.Diagnostics);
            if (candidates.Count > 1)
                diagnostics.Add("LIBRARY_FOOTPRINT_DUPLICATE_NAME: " + candidates.Count);
            return chosen with { Diagnostics = diagnostics };
        }

        public IReadOnlyList<object?> ToJson() => _footprints.GroupBy(item => item.LibraryPath).Select(group =>
            (object?)new Dictionary<string, object?>
            {
                ["path"] = group.Key,
                ["footprints"] = group.Select(item => new Dictionary<string, object?>
                {
                    ["pattern"] = item.Pattern,
                    ["sourceUniqueId"] = item.FootprintSourceUniqueId,
                    ["bodyUniqueIds"] = item.BodyUniqueIds
                        .Where(pair => pair.Key >= 0)
                        .OrderBy(pair => pair.Key)
                        .Select(pair => new Dictionary<string, object?>
                        {
                            ["bodyOrdinal"] = pair.Key,
                            ["uniqueId"] = pair.Value,
                        })
                        .ToArray(),
                    ["diagnostics"] = item.Diagnostics,
                }).ToArray(),
            }).ToArray();

        public void Dispose()
        {
            var server = PCB.GlobalVars.PCBServer;
            if (server is null)
                return;
            foreach (var (library, loaded) in _opened)
            {
                if (!loaded)
                    continue;
                var local = library;
                TryGetValue(() => { server.DestroyPCBLibrary(ref local); return true; }, out _);
            }
            _opened.Clear();
        }
    }
}
