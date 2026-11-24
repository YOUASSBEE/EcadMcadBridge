using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Core.Packaging;

/// <summary>Versioned manifest of an Exchange Package. No absolute user paths are stored.</summary>
public sealed class PackageManifest
{
    public const string SchemaName = "ecad-mcad-bridge/package";

    public const string SchemaVersionValue = "1.0.0";

    public const string SnapshotFileName = "board.snapshot.json";

    public const string ManifestFileName = "manifest.json";

    public const string ChecksumsFileName = "checksums.sha256";

    public string Schema { get; init; } = SchemaName;

    public string SchemaVersion { get; init; } = SchemaVersionValue;

    public Guid PackageId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public PackageProducer Producer { get; init; } = new();

    public PackageSource Source { get; init; } = new();

    public string SourceUnits { get; init; } = "altium-iu";

    public string Units { get; init; } = "mm";

    public IReadOnlyList<PackageFileEntry> Files { get; init; } = Array.Empty<PackageFileEntry>();

    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
}

public sealed class PackageProducer
{
    public string Component { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;
}

public sealed class PackageSource
{
    public string Cad { get; init; } = "altium";

    public string DocumentSha256 { get; init; } = string.Empty;
}

public sealed class PackageFileEntry
{
    public string Path { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;

    public long Size { get; init; }
}

/// <summary>Caller-supplied package metadata; time and package id are not read from system clocks.</summary>
public sealed class PackageWriteOptions
{
    public Guid PackageId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public PackageProducer Producer { get; init; } = new();

    public PackageSource Source { get; init; } = new();

    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
}

public sealed class PackageWriteResult
{
    private PackageWriteResult(
        bool isSuccess,
        string? packageDirectory,
        PackageManifest? manifest,
        IReadOnlyList<PackageDiagnostic> diagnostics)
    {
        IsSuccess = isSuccess;
        PackageDirectory = packageDirectory;
        Manifest = manifest;
        Diagnostics = diagnostics;
        Errors = diagnostics.Where(item => item.Severity == PackageDiagnosticSeverity.Error).ToArray();
        Warnings = diagnostics.Where(item => item.Severity == PackageDiagnosticSeverity.Warning).ToArray();
    }

    public bool IsSuccess { get; }

    public string? PackageDirectory { get; }

    public PackageManifest? Manifest { get; }

    public IReadOnlyList<PackageDiagnostic> Diagnostics { get; }

    public IReadOnlyList<PackageDiagnostic> Errors { get; }

    public IReadOnlyList<PackageDiagnostic> Warnings { get; }

    internal static PackageWriteResult Success(
        string packageDirectory,
        PackageManifest manifest,
        IReadOnlyList<PackageDiagnostic> diagnostics) =>
        new(true, packageDirectory, manifest, diagnostics);

    internal static PackageWriteResult Failure(IReadOnlyList<PackageDiagnostic> diagnostics) =>
        new(false, null, null, diagnostics);
}

public sealed class PackageReadResult
{
    private PackageReadResult(
        bool isSuccess,
        PackageManifest? manifest,
        BoardSnapshot? snapshot,
        IReadOnlyList<PackageDiagnostic> diagnostics)
    {
        IsSuccess = isSuccess;
        Manifest = manifest;
        Snapshot = snapshot;
        Diagnostics = diagnostics;
        Errors = diagnostics.Where(item => item.Severity == PackageDiagnosticSeverity.Error).ToArray();
        Warnings = diagnostics.Where(item => item.Severity == PackageDiagnosticSeverity.Warning).ToArray();
    }

    public bool IsSuccess { get; }

    public PackageManifest? Manifest { get; }

    public BoardSnapshot? Snapshot { get; }

    public IReadOnlyList<PackageDiagnostic> Diagnostics { get; }

    public IReadOnlyList<PackageDiagnostic> Errors { get; }

    public IReadOnlyList<PackageDiagnostic> Warnings { get; }

    internal static PackageReadResult Success(
        PackageManifest manifest,
        BoardSnapshot snapshot,
        IReadOnlyList<PackageDiagnostic> diagnostics) =>
        new(true, manifest, snapshot, diagnostics);

    internal static PackageReadResult Failure(
        PackageManifest? manifest,
        BoardSnapshot? snapshot,
        IReadOnlyList<PackageDiagnostic> diagnostics) =>
        new(false, manifest, snapshot, diagnostics);
}
