using System.Globalization;
using System.IO;
using System.Text;

namespace EcadMcadBridge.Altium.Application;

internal static class BridgeLogger
{
    private static readonly object Sync = new();

    public static void Write(string level, string code, string message)
    {
        var directory = Path.Combine(Path.GetTempPath(), "EcadMcadBridge", "logs");
        Directory.CreateDirectory(directory);

        var safeMessage = message.Replace('\r', ' ').Replace('\n', ' ');
        var line = string.Concat(
            DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            "\t", level, "\t", code, "\t", safeMessage, "\n");

        lock (Sync)
            File.AppendAllText(Path.Combine(directory, "bridge.log"), line, new UTF8Encoding(false));
    }
}
