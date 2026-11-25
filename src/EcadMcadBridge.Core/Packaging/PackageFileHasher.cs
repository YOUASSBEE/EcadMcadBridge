using System.Security.Cryptography;

namespace EcadMcadBridge.Core.Packaging;

public sealed record PackageFileHashResult(
    bool IsSuccess,
    string? Sha256,
    long Size,
    DateTime LastWriteUtc,
    string Message);

public sealed record PackageSourceMetadata(
    string FilePath,
    string Sha256,
    long Size,
    DateTime LastWriteUtc);

public sealed record PackageSourceResult(
    bool IsSuccess,
    PackageSourceMetadata? Metadata,
    string Message);

public static class PackageFileHasher
{
    /// <summary>Reads a file in a stream and verifies that size and mtime did not change.</summary>
    public static PackageFileHashResult ComputeStableSha256(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Failure("File path is empty.");

        try
        {
            var before = new FileInfo(path);
            if (!before.Exists)
                return Failure("File does not exist.");

            var sizeBefore = before.Length;
            var writeBefore = before.LastWriteTimeUtc;
            string sha;
            long size;
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete,
                       bufferSize: 81920,
                       FileOptions.SequentialScan))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                size = 0;
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    size += read;
                }

                sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            var after = new FileInfo(path);
            if (!after.Exists || after.Length != sizeBefore || after.LastWriteTimeUtc != writeBefore)
                return Failure("File changed while it was being read.");

            return new PackageFileHashResult(true, sha, size, writeBefore, "File hash is stable.");
        }
        catch (Exception ex)
        {
            return Failure("File hash failed: " + ex.GetType().Name);
        }
    }

    private static PackageFileHashResult Failure(string message) =>
        new(false, null, 0, default, message);
}

public static class PackageSourceMetadataReader
{
    public static PackageSourceResult Read(string? filePath, bool isModified)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Failure("The active PCB has no saved file path.");

        if (isModified)
            return Failure("The active PCB has unsaved changes; save it and retry the export.");

        if (!Path.IsPathRooted(filePath))
            return Failure("The active PCB path is not absolute.");

        if (!File.Exists(filePath))
            return Failure("The active PCB file does not exist or is not readable.");

        var hash = PackageFileHasher.ComputeStableSha256(filePath);
        if (!hash.IsSuccess)
            return Failure(hash.Message);

        return new PackageSourceResult(
            true,
            new PackageSourceMetadata(filePath, hash.Sha256!, hash.Size, hash.LastWriteUtc),
            "Source document metadata is valid.");
    }

    private static PackageSourceResult Failure(string message) =>
        new(false, null, message);
}
