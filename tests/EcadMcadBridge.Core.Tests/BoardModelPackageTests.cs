using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using Xunit;

namespace EcadMcadBridge.Core.Tests;

public sealed class BoardModelPackageTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 26, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid PackageId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string DocumentSha = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string StepText =
        "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('Open CASCADE Model'),'2;1');\n" +
        "FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));\nENDSEC;\nDATA;\nENDSEC;\n" +
        "END-ISO-10303-21;\n";

    [Fact]
    public void WriterAndReader_RoundTripBoardModel()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();

        Assert.True(written.IsSuccess, Describe(written.Errors));
        var directory = written.PackageDirectory!;
        var manifest = ReadManifest(directory);
        Assert.Contains(PackageManifest.BoardModelCapability, manifest.Capabilities);
        Assert.Contains(manifest.Files, file => file.Path == PackageManifest.BoardModelFileName);
        Assert.Equal(3, manifest.Files.Count);

        var read = ExchangePackageReader.Read(directory);

        Assert.True(read.IsSuccess, Describe(read.Errors));
        var board = Assert.IsType<BoardModelDescriptor>(read.BoardModel);
        Assert.Equal(BoardModelDescriptor.SchemaName, board.Schema);
        Assert.Equal("1.0.0", board.SchemaVersion);
        Assert.Equal(ComponentModelAssetFormat.StepAp214, board.AssetFormat);
        Assert.Equal("mm", board.Units);
        Assert.True(board.Frame.IsReference);
        Assert.Equal(scenario.BoardBytes.Length, board.Size);
        Assert.Equal(Sha(scenario.BoardBytes), board.Sha256);
        Assert.Equal("assets/board/" + board.Sha256 + ".step", board.AssetPath);
        Assert.Equal(1_600_000, board.NominalThickness.Iu);
        Assert.Equal(EcadMcadBridge.Core.Diagnostics.AltiumUnits.IuToMm(1_600_000), board.NominalThickness.Mm!.Value, 9);

        var descriptorPath = Path.Combine(directory, PackageManifest.BoardModelFileName);
        Assert.Equal(
            BoardModelJson.SerializeToUtf8(board),
            File.ReadAllBytes(descriptorPath));
        Assert.True(File.Exists(Path.Combine(directory, board.AssetPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void Reader_ReturnsNullBoardModelForLegacyPackage()
    {
        using var scenario = new Scenario();
        var written = ExchangePackageWriter.Write(
            scenario.Root,
            BoardSnapshotFixture.CreateValid(),
            Options());
        Assert.True(written.IsSuccess, Describe(written.Errors));

        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.True(read.IsSuccess, Describe(read.Errors));
        Assert.Null(read.BoardModel);
        Assert.DoesNotContain(
            PackageManifest.BoardModelCapability,
            read.Manifest!.Capabilities);
    }

    [Fact]
    public void Writer_RejectsCapabilityWithoutBoardModel()
    {
        using var scenario = new Scenario();
        var options = new PackageWriteOptions
        {
            PackageId = PackageId,
            CreatedAtUtc = Created,
            Producer = Producer(),
            Source = new PackageSource { Cad = "altium", DocumentSha256 = DocumentSha },
            Capabilities = new[] { PackageManifest.BoardModelCapability },
        };

        var result = ExchangePackageWriter.Write(
            scenario.Root,
            BoardSnapshotFixture.CreateValid(),
            options);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidMetadata);
        Assert.False(Directory.Exists(Path.Combine(scenario.Root, PackageId.ToString("D"))));
    }

    [Fact]
    public void Writer_RejectsMissingBoardAsset()
    {
        using var scenario = new Scenario();
        var result = scenario.Write(new BoardModelWriteRequest
        {
            SourcePath = Path.Combine(scenario.Root, "missing.step"),
        });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidBoardModelAsset);
        Assert.False(Directory.Exists(Path.Combine(scenario.Root, PackageId.ToString("D"))));
    }

    [Fact]
    public void Writer_RejectsEmptyBoardAsset()
    {
        using var scenario = new Scenario();
        var empty = Path.Combine(scenario.Root, "empty.step");
        File.WriteAllBytes(empty, Array.Empty<byte>());

        var result = scenario.Write(new BoardModelWriteRequest { SourcePath = empty });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidBoardModelAsset);
        Assert.False(Directory.Exists(Path.Combine(scenario.Root, PackageId.ToString("D"))));
    }

    [Fact]
    public void Writer_RejectsNonStepBoardFormat()
    {
        using var scenario = new Scenario();
        var result = scenario.Write(new BoardModelWriteRequest
        {
            SourcePath = scenario.BoardPath,
            Format = ComponentModelAssetFormat.ParasolidXT,
        });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidBoardModelAsset);
    }

    [Fact]
    public void Writer_ProducesDeterministicDescriptor()
    {
        using var scenario = new Scenario();
        using var first = new TempDirectory();
        using var second = new TempDirectory();

        var firstResult = scenario.Write(root: first.Path);
        var secondResult = scenario.Write(root: second.Path);

        Assert.True(firstResult.IsSuccess, Describe(firstResult.Errors));
        Assert.True(secondResult.IsSuccess, Describe(secondResult.Errors));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(firstResult.PackageDirectory!, PackageManifest.BoardModelFileName)),
            File.ReadAllBytes(Path.Combine(secondResult.PackageDirectory!, PackageManifest.BoardModelFileName)));
    }

    [Fact]
    public void Reader_RejectsCapabilityWithoutDescriptor()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var directory = written.PackageDirectory!;
        var asset = ReadManifest(directory).Files.Single(
            file => file.Path.StartsWith("assets/board/", StringComparison.Ordinal)).Path;
        File.Delete(Path.Combine(directory, PackageManifest.BoardModelFileName));
        File.Delete(Path.Combine(directory, asset.Replace('/', Path.DirectorySeparatorChar)));
        RewriteManifest(
            directory,
            manifest => CloneManifest(
                manifest,
                manifest.Files.Where(file =>
                    file.Path != PackageManifest.BoardModelFileName && file.Path != asset).ToArray(),
                manifest.Capabilities));

        var read = ExchangePackageReader.Read(directory);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic =>
            diagnostic.Code == PackageDiagnosticCodes.BoardModelCapabilityMismatch);
    }

    [Fact]
    public void Reader_RejectsDescriptorWithoutCapability()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var directory = written.PackageDirectory!;
        RewriteManifest(
            directory,
            manifest => CloneManifest(
                manifest,
                manifest.Files,
                manifest.Capabilities.Where(item => item != PackageManifest.BoardModelCapability).ToArray()));

        var read = ExchangePackageReader.Read(directory);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic =>
            diagnostic.Code == PackageDiagnosticCodes.BoardModelCapabilityMismatch);
    }

    [Fact]
    public void Reader_RejectsCorruptDescriptor()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var directory = written.PackageDirectory!;
        WriteDescriptor(directory, Encoding.UTF8.GetBytes("{"));

        var read = ExchangePackageReader.Read(directory);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic =>
            diagnostic.Code == PackageDiagnosticCodes.InvalidBoardModel);
    }

    [Fact]
    public void Reader_RejectsDescriptorHashMismatch()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var directory = written.PackageDirectory!;
        EditDescriptor(directory, root => root["sha256"] = new string('0', 64));

        var read = ExchangePackageReader.Read(directory);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic =>
            diagnostic.Code == PackageDiagnosticCodes.InvalidBoardModelAsset);
    }

    [Fact]
    public void Reader_RejectsUnsupportedDescriptorVersion()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var directory = written.PackageDirectory!;
        EditDescriptor(directory, root => root["schemaVersion"] = "2.0.0");

        var read = ExchangePackageReader.Read(directory);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic =>
            diagnostic.Code == PackageDiagnosticCodes.UnsupportedBoardModelSchema);
    }

    [Fact]
    public void Reader_RejectsNonReferenceFrame()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var directory = written.PackageDirectory!;
        EditDescriptor(directory, root => root["frame"]!["zAxis"] = "towards-top");

        var read = ExchangePackageReader.Read(directory);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.InvalidBoardModel);
    }

    [Fact]
    public void Reader_RejectsMissingBoardAssetFile()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var directory = written.PackageDirectory!;
        var asset = ReadManifest(directory).Files.Single(
            file => file.Path.StartsWith("assets/board/", StringComparison.Ordinal)).Path;
        File.Delete(Path.Combine(directory, asset.Replace('/', Path.DirectorySeparatorChar)));

        var read = ExchangePackageReader.Read(directory);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic => diagnostic.Code == PackageDiagnosticCodes.MissingFile);
    }

    [Fact]
    public void Reader_RejectsUnreferencedBoardAsset()
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var directory = written.PackageDirectory!;
        var orphan = Encoding.UTF8.GetBytes(StepText + "orphan");
        var sha = Sha(orphan);
        var relative = "assets/board/" + sha + ".step";
        File.WriteAllBytes(Path.Combine(directory, "assets", "board", sha + ".step"), orphan);
        RewriteManifest(
            directory,
            manifest => CloneManifest(
                manifest,
                manifest.Files.Concat(new[]
                {
                    new PackageFileEntry { Path = relative, Sha256 = sha, Size = orphan.LongLength },
                }).ToArray(),
                manifest.Capabilities));

        var read = ExchangePackageReader.Read(directory);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic =>
            diagnostic.Code == PackageDiagnosticCodes.InvalidBoardModelAsset);
    }

    [Theory]
    [InlineData("../escape.step")]
    [InlineData("/assets/board/escape.step")]
    [InlineData("assets\\board\\escape.step")]
    [InlineData("assets/board/C:/escape.step")]
    [InlineData("assets/3d/abc.step")]
    public void Reader_RejectsUnsafeBoardAssetPath(string path)
    {
        using var scenario = new Scenario();
        var written = scenario.Write();
        Assert.True(written.IsSuccess, Describe(written.Errors));
        var directory = written.PackageDirectory!;
        EditDescriptor(directory, root => root["assetPath"] = path);

        var read = ExchangePackageReader.Read(directory);

        Assert.False(read.IsSuccess);
        Assert.Contains(read.Errors, diagnostic =>
            diagnostic.Code == PackageDiagnosticCodes.InvalidBoardModelAsset);
    }

    private static string Describe(IReadOnlyList<PackageDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(item => item.Code + " " + item.Path + ": " + item.Message));

    private static PackageWriteOptions Options() => new()
    {
        PackageId = PackageId,
        CreatedAtUtc = Created,
        Producer = Producer(),
        Source = new PackageSource { Cad = "altium", DocumentSha256 = DocumentSha },
    };

    private static PackageProducer Producer() => new() { Component = "test", Version = "0.1.0" };

    private static PackageManifest ReadManifest(string directory) =>
        PackageManifestJson.Deserialize(
            File.ReadAllText(Path.Combine(directory, PackageManifest.ManifestFileName)));

    private static void EditDescriptor(string directory, Action<JsonObject> edit)
    {
        var path = Path.Combine(directory, PackageManifest.BoardModelFileName);
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(root);
        WriteDescriptor(
            directory,
            Encoding.UTF8.GetBytes(root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n"));
    }

    private static void WriteDescriptor(string directory, byte[] bytes)
    {
        var path = Path.Combine(directory, PackageManifest.BoardModelFileName);
        File.WriteAllBytes(path, bytes);
        RewriteManifest(
            directory,
            manifest => CloneManifest(
                manifest,
                manifest.Files.Select(file => file.Path == PackageManifest.BoardModelFileName
                    ? new PackageFileEntry
                    {
                        Path = file.Path,
                        Sha256 = Sha(bytes),
                        Size = bytes.LongLength,
                    }
                    : file).ToArray(),
                manifest.Capabilities));
    }

    private static void RewriteManifest(string directory, Func<PackageManifest, PackageManifest> change)
    {
        var manifestPath = Path.Combine(directory, PackageManifest.ManifestFileName);
        var manifest = ReadManifest(directory);
        var changed = change(manifest);
        var bytes = PackageManifestJson.SerializeToUtf8(changed);
        File.WriteAllBytes(manifestPath, bytes);

        var entries = new List<(string Hash, string Path)>
        {
            (Sha(bytes), PackageManifest.ManifestFileName),
        };
        foreach (var file in changed.Files)
        {
            var fullPath = Path.Combine(directory, file.Path.Replace('/', Path.DirectorySeparatorChar));
            entries.Add((Sha(File.ReadAllBytes(fullPath)), file.Path));
        }

        var text = string.Join(
            "\n",
            entries.OrderBy(entry => entry.Path, StringComparer.Ordinal)
                .Select(entry => entry.Hash + "  " + entry.Path)) + "\n";
        File.WriteAllText(
            Path.Combine(directory, PackageManifest.ChecksumsFileName),
            text,
            new UTF8Encoding(false));
    }

    private static PackageManifest CloneManifest(
        PackageManifest manifest,
        IReadOnlyList<PackageFileEntry> files,
        IReadOnlyList<string> capabilities) => new()
        {
            Schema = manifest.Schema,
            SchemaVersion = manifest.SchemaVersion,
            PackageId = manifest.PackageId,
            CreatedAtUtc = manifest.CreatedAtUtc,
            Producer = manifest.Producer,
            Source = manifest.Source,
            SourceUnits = manifest.SourceUnits,
            Units = manifest.Units,
            Files = files,
            Capabilities = capabilities,
        };

    private static string Sha(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class Scenario : IDisposable
    {
        public Scenario()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "EcadMcadBridge.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            BoardBytes = Encoding.UTF8.GetBytes(StepText);
            BoardPath = Path.Combine(Root, "board.step");
            File.WriteAllBytes(BoardPath, BoardBytes);
        }

        public string Root { get; }

        public string BoardPath { get; }

        public byte[] BoardBytes { get; }

        public PackageWriteResult Write(
            BoardModelWriteRequest? board = null,
            string? root = null)
        {
            board ??= new BoardModelWriteRequest { SourcePath = BoardPath };
            return ExchangePackageWriter.Write(
                root ?? Root,
                BoardSnapshotFixture.CreateValid(),
                Options(),
                models: null,
                board);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
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
