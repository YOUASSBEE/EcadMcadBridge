using System.Security.Cryptography;
using EcadMcadBridge.Core.Packaging;
using Xunit;

namespace EcadMcadBridge.Core.Tests;

public sealed class PackageSourceTests
{
    [Fact]
    public void RootResolver_UsesEnvironmentVariable()
    {
        var result = PackageRootResolver.Resolve(
            @"D:\runtime\packages",
            @"C:\Users\test\AppData\Local");

        Assert.True(result.IsSuccess);
        Assert.Equal(@"D:\runtime\packages", result.Root);
    }

    [Fact]
    public void RootResolver_UsesLocalAppDataFallback()
    {
        var result = PackageRootResolver.Resolve(
            null,
            @"C:\Users\test\AppData\Local");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Path.Combine(@"C:\Users\test\AppData\Local", "EcadMcadBridge", "Packages"),
            result.Root);
    }

    [Fact]
    public void RootResolver_RejectsRelativeEnvironmentPath()
    {
        var result = PackageRootResolver.Resolve("relative/packages", @"C:\Users\test\AppData\Local");

        Assert.False(result.IsSuccess);
        Assert.Contains("absolute", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RootResolver_RejectsMissingLocalAppData()
    {
        var result = PackageRootResolver.Resolve(string.Empty, string.Empty);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void FileHasher_ComputesStableSha256()
    {
        using var temp = new TempFile();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(temp.Path, bytes);

        var result = PackageFileHasher.ComputeStableSha256(temp.Path);

        Assert.True(result.IsSuccess);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.Sha256);
        Assert.Equal(bytes.Length, result.Size);
    }

    [Fact]
    public void FileHasher_RejectsMissingFile()
    {
        var result = PackageFileHasher.ComputeStableSha256(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.bin"));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void SourceMetadata_UsesDocumentFileHash()
    {
        using var temp = new TempFile();
        var bytes = "pcb-bytes"u8.ToArray();
        File.WriteAllBytes(temp.Path, bytes);

        var result = PackageSourceMetadataReader.Read(temp.Path, isModified: false);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Metadata);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            result.Metadata!.Sha256);
    }

    [Fact]
    public void SourceMetadata_RejectsModifiedDocument()
    {
        using var temp = new TempFile();
        File.WriteAllBytes(temp.Path, new byte[] { 1 });

        var result = PackageSourceMetadataReader.Read(temp.Path, isModified: true);

        Assert.False(result.IsSuccess);
        Assert.Contains("unsaved", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceMetadata_RejectsMissingPath()
    {
        var result = PackageSourceMetadataReader.Read(null, isModified: false);

        Assert.False(result.IsSuccess);
        Assert.Contains("saved file path", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "EcadMcadBridge.Tests",
                Guid.NewGuid().ToString("N") + ".bin");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }
}
