using System.Globalization;
using System.Text;
using EcadMcadBridge.Core.Model;

namespace EcadMcadBridge.Core.Diagnostics;

/// <summary>Writes diagnostic snapshots under %TEMP%\EcadMcadBridge.</summary>
public static class SnapshotFileWriter
{
    public const string DirectoryName = "EcadMcadBridge";

    public static string Write(SmokeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var directory = Path.Combine(Path.GetTempPath(), DirectoryName);
        Directory.CreateDirectory(directory);

        var stamp = snapshot.ExportedAtUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var path = Path.Combine(directory, $"smoke-snapshot-{stamp}.json");
        File.WriteAllText(path, SmokeSnapshotJson.Serialize(snapshot), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
