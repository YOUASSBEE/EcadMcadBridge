using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using EcadMcadBridge.Core.Serialization;
using EcadMcadBridge.Core.Validation;
using Xunit;

namespace EcadMcadBridge.Core.Tests;

public sealed class ExchangePackageTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid PackageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string DocumentSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Writer_CreatesCompletePackage()
    {
        using var temp = new TempDirectory();
        var snapshot = BoardSnapshotFixture.CreateValid();

        var result = Write(temp.Path, snapshot);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        var packageDirectory = Assert.IsType<string>(result.PackageDirectory);
        Assert.True(Directory.Exists(packageDirectory));
        Assert.Equal(
            new[] { "board.snapshot.json", "checksums.sha256", "manifest.json" },
            Directory.GetFiles(packageDirectory).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal));

        var snapshotBytes = File.ReadAllBytes(Path.Combine(packageDirectory, "board.snapshot.json"));
        var manifest = PackageManifestJson.Deserialize(
            File.ReadAllText(Path.Combine(packageDirectory, "manifest.json")));
        Assert.Equal(PackageManifest.SchemaName, manifest.Schema);
        Assert.Equal(PackageManifest.SchemaVersionValue, manifest.SchemaVersion);
        Assert.Equal(PackageId, manifest.PackageId);
        Assert.Equal(Created, manifest.CreatedAtUtc);
        Assert.Equal("EcadMcadBridge.Altium", manifest.Producer.Component);
        Assert.Equal("0.3.0", manifest.Producer.Version);
        Assert.Equal("altium", manifest.Source.Cad);
        Assert.Equal(DocumentSha, manifest.Source.DocumentSha256);
        Assert.Equal("altium-iu", manifest.SourceUnits);
        Assert.Equal("mm", manifest.Units);
        Assert.Equal(new[] { "board-outline", "component-placement" }, manifest.Capabilities);
        var entry = Assert.Single(manifest.Files);
        Assert.Equal("board.snapshot.json", entry.Path);
        Assert.Equal(Sha(snapshotBytes), entry.Sha256);
        Assert.Equal(snapshotBytes.Length, entry.Size);

        var checksums = File.ReadAllText(Path.Combine(packageDirectory, "checksums.sha256"));
        Assert.Contains(Sha(snapshotBytes) + "  board.snapshot.json", checksums, StringComparison.Ordinal);
        Assert.Contains(
            Sha(File.ReadAllBytes(Path.Combine(packageDirectory, "manifest.json"))) + "  manifest.json",
            checksums,
            StringComparison.Ordinal);
        Assert.DoesNotContain("checksums.sha256", checksums, StringComparison.Ordinal);
    }

    [Fact]
    public void Writer_RejectsInvalidSnapshot()
    {
        using var temp = new TempDirectory();
        var valid = BoardSnapshotFixture.CreateValid();
        var badComponent = new ComponentInstance
        {
            Id = "component-bad",
            Designator = "X1",
            LayerId = "missing-layer",
            Placement = new Transform
            {
                Position = Point2D.FromIu(0, 0),
                RotationDeg = 0,
                Side = BoardSide.Top,
            },
        };
        var invalid = BoardSnapshotFixture.With(valid, components: new[] { badComponent });

        var result = ExchangePackageWriter.Write(temp.Path, invalid, Options());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == BoardDiagnosticCodes.InvalidReference);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, PackageId.ToString("D"))));
    }

    [Fact]
    public void Writer_DoesNotOverwriteExistingPackage()
    {
        using var temp = new TempDirectory();
        var first = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        Assert.True(first.IsSuccess);
        var manifestPath = Path.Combine(first.PackageDirectory!, "manifest.json");
        var original = File.ReadAllBytes(manifestPath);

        var second = ExchangePackageWriter.Write(
            temp.Path,
            BoardSnapshotFixture.CreateValid(),
            Options());

        Assert.False(second.IsSuccess);
        Assert.Contains(second.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.ExistingPackage);
        Assert.Equal(original, File.ReadAllBytes(manifestPath));
    }

    [Fact]
    public void Writer_LeavesNoPartialPackageAfterPathFailure()
    {
        using var temp = new TempDirectory();
        var blocked = Path.Combine(temp.Path, "blocked");
        File.WriteAllText(blocked, "not a directory");

        var result = ExchangePackageWriter.Write(
            Path.Combine(blocked, "nested"),
            BoardSnapshotFixture.CreateValid(),
            Options());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.WriteFailed);
        Assert.False(Directory.Exists(Path.Combine(blocked, "nested", PackageId.ToString("D"))));
    }

    [Fact]
    public void Writer_RejectsInvalidMetadata()
    {
        using var temp = new TempDirectory();
        var options = new PackageWriteOptions
        {
            PackageId = PackageId,
            CreatedAtUtc = Created,
            Producer = new PackageProducer { Component = "EcadMcadBridge.Altium", Version = "0.3.0" },
            Source = new PackageSource { Cad = "altium", DocumentSha256 = "not-a-sha" },
            Capabilities = new[] { "board-outline" },
        };

        var result = ExchangePackageWriter.Write(temp.Path, BoardSnapshotFixture.CreateValid(), options);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidMetadata);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, PackageId.ToString("D"))));
    }

    [Fact]
    public void Reader_RoundTripsSnapshotAndManifest()
    {
        using var temp = new TempDirectory();
        var snapshot = BoardSnapshotFixture.CreateValid();
        var written = Write(temp.Path, snapshot);
        Assert.True(written.IsSuccess);

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.True(read.IsSuccess);
        Assert.NotNull(read.Manifest);
        Assert.NotNull(read.Snapshot);
        Assert.Equal(PackageId, read.Manifest!.PackageId);
        Assert.Equal(
            BoardSnapshotJson.SerializeToUtf8(snapshot),
            BoardSnapshotJson.SerializeToUtf8(read.Snapshot!));
        Assert.Empty(read.Errors);
    }

    [Fact]
    public void Reader_RejectsMissingManifest()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        File.Delete(Path.Combine(written.PackageDirectory!, "manifest.json"));

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.MissingManifest);
    }

    [Fact]
    public void Reader_RejectsMissingChecksums()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        File.Delete(Path.Combine(written.PackageDirectory!, "checksums.sha256"));

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.MissingChecksums);
    }

    [Fact]
    public void Reader_RejectsMissingSnapshot()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        File.Delete(Path.Combine(written.PackageDirectory!, "board.snapshot.json"));

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.MissingFile);
    }

    [Fact]
    public void Reader_RejectsChecksumMismatch()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        var snapshotPath = Path.Combine(written.PackageDirectory!, "board.snapshot.json");
        File.AppendAllText(snapshotPath, " ");

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.ChecksumMismatch);
    }

    [Fact]
    public void Reader_RejectsSizeMismatch()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        var manifestPath = Path.Combine(written.PackageDirectory!, "manifest.json");
        var manifest = PackageManifestJson.Deserialize(File.ReadAllText(manifestPath));
        var file = manifest.Files[0];
        var changed = new PackageManifest
        {
            PackageId = manifest.PackageId,
            CreatedAtUtc = manifest.CreatedAtUtc,
            Producer = manifest.Producer,
            Source = manifest.Source,
            Files = new[]
            {
                new PackageFileEntry { Path = file.Path, Sha256 = file.Sha256, Size = file.Size + 1 },
            },
            Capabilities = manifest.Capabilities,
        };
        var manifestBytes = PackageManifestJson.SerializeToUtf8(changed);
        File.WriteAllBytes(manifestPath, manifestBytes);
        WriteChecksums(written.PackageDirectory!, manifestBytes);

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.SizeMismatch);
    }

    [Fact]
    public void Reader_RejectsUnsupportedSchema()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        ReplaceManifestText(written.PackageDirectory!, "ecad-mcad-bridge/package", "other/package");

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.UnsupportedSchema);
    }

    [Fact]
    public void Reader_RejectsUnsupportedMajorVersion()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        ReplaceManifestText(written.PackageDirectory!, "\"schemaVersion\": \"1.0.0\"", "\"schemaVersion\": \"2.0.0\"");

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.UnsupportedSchemaVersion);
    }

    [Fact]
    public void Reader_RejectsWrongUnits()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        ReplaceManifestText(written.PackageDirectory!, "\"units\": \"mm\"", "\"units\": \"inch\"");

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidUnits);
    }

    [Fact]
    public void Reader_RejectsInvalidShaFormat()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        var manifestPath = Path.Combine(written.PackageDirectory!, "manifest.json");
        var text = File.ReadAllText(manifestPath);
        var shaStart = text.IndexOf("\"sha256\": \"", StringComparison.Ordinal) + "\"sha256\": \"".Length;
        text = text[..shaStart] + "XYZ" + text[(shaStart + 3)..];
        File.WriteAllText(manifestPath, text);

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidMetadata);
    }

    [Fact]
    public void Reader_RejectsAbsolutePath()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        ReplaceManifestText(written.PackageDirectory!, "\"path\": \"board.snapshot.json\"", "\"path\": \"/board.snapshot.json\"");

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidPath);
    }

    [Fact]
    public void Reader_RejectsParentTraversal()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        ReplaceManifestText(written.PackageDirectory!, "\"path\": \"board.snapshot.json\"", "\"path\": \"../board.snapshot.json\"");

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidPath);
    }

    [Fact]
    public void Reader_RejectsDuplicatePaths()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        var manifestPath = Path.Combine(written.PackageDirectory!, "manifest.json");
        var manifest = PackageManifestJson.Deserialize(File.ReadAllText(manifestPath));
        var file = manifest.Files[0];
        var changed = new PackageManifest
        {
            PackageId = manifest.PackageId,
            CreatedAtUtc = manifest.CreatedAtUtc,
            Producer = manifest.Producer,
            Source = manifest.Source,
            Files = new[] { file, file },
            Capabilities = manifest.Capabilities,
        };
        File.WriteAllBytes(manifestPath, PackageManifestJson.SerializeToUtf8(changed));

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.DuplicatePath);
    }

    [Fact]
    public void Reader_RejectsCorruptManifestJson()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        File.WriteAllText(Path.Combine(written.PackageDirectory!, "manifest.json"), "{");

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidManifest);
    }

    [Fact]
    public void Reader_IgnoresUnknownManifestFields()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        var manifestPath = Path.Combine(written.PackageDirectory!, "manifest.json");
        var text = File.ReadAllText(manifestPath);
        text = text.Replace(
            "\"schema\":",
            "\"futureField\": {\"value\": 1},\n  \"schema\":",
            StringComparison.Ordinal);
        var manifestBytes = new UTF8Encoding(false).GetBytes(text);
        File.WriteAllBytes(manifestPath, manifestBytes);
        WriteChecksums(written.PackageDirectory!, manifestBytes);

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.True(read.IsSuccess);
        Assert.NotNull(read.Snapshot);
    }

    [Fact]
    public void Reader_RejectsMalformedChecksums()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        File.WriteAllText(Path.Combine(written.PackageDirectory!, "checksums.sha256"), "bad\n");

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidChecksumFormat);
    }

    [Fact]
    public void Reader_RejectsCorruptSnapshotJson()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        var snapshotBytes = Encoding.UTF8.GetBytes("{");
        var manifestPath = Path.Combine(written.PackageDirectory!, "manifest.json");
        var manifest = PackageManifestJson.Deserialize(File.ReadAllText(manifestPath));
        var changed = new PackageManifest
        {
            PackageId = manifest.PackageId,
            CreatedAtUtc = manifest.CreatedAtUtc,
            Producer = manifest.Producer,
            Source = manifest.Source,
            Files = new[]
            {
                new PackageFileEntry
                {
                    Path = "board.snapshot.json",
                    Sha256 = Sha(snapshotBytes),
                    Size = snapshotBytes.Length,
                },
            },
            Capabilities = manifest.Capabilities,
        };
        var manifestBytes = PackageManifestJson.SerializeToUtf8(changed);
        File.WriteAllBytes(manifestPath, manifestBytes);
        File.WriteAllBytes(Path.Combine(written.PackageDirectory!, "board.snapshot.json"), snapshotBytes);
        WriteChecksums(written.PackageDirectory!, manifestBytes);

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.CorruptSnapshot);
    }

    [Fact]
    public void Reader_RejectsExtraFile()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        File.WriteAllText(Path.Combine(written.PackageDirectory!, "unexpected.txt"), "extra");

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.ExtraFile);
    }

    [Fact]
    public void Reader_RejectsSnapshotValidationErrors()
    {
        using var temp = new TempDirectory();
        var written = Write(temp.Path, BoardSnapshotFixture.CreateValid());
        var invalid = BoardSnapshotFixture.With(
            BoardSnapshotFixture.CreateValid(),
            components: new[]
            {
                new ComponentInstance
                {
                    Id = "component-invalid",
                    Designator = "X1",
                    LayerId = "missing-layer",
                    Placement = new Transform
                    {
                        Position = Point2D.FromIu(0, 0),
                        RotationDeg = 0,
                        Side = BoardSide.Top,
                    },
                },
            });
        var snapshotBytes = BoardSnapshotJson.SerializeToUtf8(invalid);
        var manifestPath = Path.Combine(written.PackageDirectory!, "manifest.json");
        var manifest = PackageManifestJson.Deserialize(File.ReadAllText(manifestPath));
        var changed = new PackageManifest
        {
            PackageId = manifest.PackageId,
            CreatedAtUtc = manifest.CreatedAtUtc,
            Producer = manifest.Producer,
            Source = manifest.Source,
            Files = new[]
            {
                new PackageFileEntry
                {
                    Path = "board.snapshot.json",
                    Sha256 = Sha(snapshotBytes),
                    Size = snapshotBytes.Length,
                },
            },
            Capabilities = manifest.Capabilities,
        };
        var manifestBytes = PackageManifestJson.SerializeToUtf8(changed);
        File.WriteAllBytes(manifestPath, manifestBytes);
        File.WriteAllBytes(Path.Combine(written.PackageDirectory!, "board.snapshot.json"), snapshotBytes);
        WriteChecksums(written.PackageDirectory!, manifestBytes);

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == BoardDiagnosticCodes.InvalidReference);
    }

    [Fact]
    public void Writer_IsDeterministicForSameMetadata()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();
        var snapshot = BoardSnapshotFixture.CreateValid();

        var firstResult = Write(first.Path, snapshot);
        var secondResult = Write(second.Path, snapshot);

        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsSuccess);
        foreach (var name in new[] { "board.snapshot.json", "manifest.json", "checksums.sha256" })
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(firstResult.PackageDirectory!, name)),
                File.ReadAllBytes(Path.Combine(secondResult.PackageDirectory!, name)));
        }
    }

    private static PackageWriteResult Write(string root, BoardSnapshot snapshot) =>
        ExchangePackageWriter.Write(root, snapshot, Options());

    private static PackageWriteOptions Options() => new()
    {
        PackageId = PackageId,
        CreatedAtUtc = Created,
        Producer = new PackageProducer { Component = "EcadMcadBridge.Altium", Version = "0.3.0" },
        Source = new PackageSource { Cad = "altium", DocumentSha256 = DocumentSha },
        Capabilities = new[] { "component-placement", "board-outline" },
    };

    private static string Sha(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void WriteChecksums(string packageDirectory, byte[] manifestBytes)
    {
        var snapshotBytes = File.ReadAllBytes(Path.Combine(packageDirectory, "board.snapshot.json"));
        var text = Sha(snapshotBytes) + "  board.snapshot.json\n"
            + Sha(manifestBytes) + "  manifest.json\n";
        File.WriteAllText(Path.Combine(packageDirectory, "checksums.sha256"), text, new UTF8Encoding(false));
    }

    private static void ReplaceManifestText(string packageDirectory, string oldValue, string newValue)
    {
        var path = Path.Combine(packageDirectory, "manifest.json");
        var text = File.ReadAllText(path);
        Assert.Contains(oldValue, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(oldValue, newValue, StringComparison.Ordinal));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "EcadMcadBridge.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
