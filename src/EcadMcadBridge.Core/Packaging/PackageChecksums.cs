using System.Security.Cryptography;
using System.Text;

namespace EcadMcadBridge.Core.Packaging;

internal sealed record PackageChecksumEntry(string Path, string Sha256);

internal static class PackageChecksums
{
    public const string FileName = "checksums.sha256";

    public static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string ComputeFileSha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static byte[] Serialize(IReadOnlyList<PackageChecksumEntry> entries)
    {
        var builder = new StringBuilder();
        foreach (var entry in entries.OrderBy(item => item.Path, StringComparer.Ordinal))
            builder.Append(entry.Sha256).Append("  ").Append(entry.Path).Append('\n');

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString());
    }

    public static bool TryParse(
        string text,
        out IReadOnlyList<PackageChecksumEntry> entries,
        out string error)
    {
        var result = new List<PackageChecksumEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var previous = string.Empty;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                continue;

            if (line.Length < 67 || line[64] != ' ' || line[65] != ' ')
            {
                entries = Array.Empty<PackageChecksumEntry>();
                error = $"line {i + 1} must be '<sha256>  <path>'";
                return false;
            }

            var sha = line[..64];
            var path = line[66..];
            if (!IsLowerHexSha256(sha))
            {
                entries = Array.Empty<PackageChecksumEntry>();
                error = $"line {i + 1} has an invalid SHA-256";
                return false;
            }

            if (!PackagePath.IsSafeRelative(path, out var pathReason))
            {
                entries = Array.Empty<PackageChecksumEntry>();
                error = $"line {i + 1}: {pathReason}";
                return false;
            }

            if (!seen.Add(path))
            {
                entries = Array.Empty<PackageChecksumEntry>();
                error = $"line {i + 1} repeats path '{path}'";
                return false;
            }

            if (string.CompareOrdinal(previous, path) >= 0)
            {
                entries = Array.Empty<PackageChecksumEntry>();
                error = "checksum paths must be sorted by ordinal and unique";
                return false;
            }

            previous = path;
            result.Add(new PackageChecksumEntry(path, sha));
        }

        entries = result;
        error = string.Empty;
        return true;
    }

    public static bool IsLowerHexSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
