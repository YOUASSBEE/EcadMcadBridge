namespace EcadMcadBridge.Altium.Application;

/// <summary>
/// Isolates secondary boundaries (logging and cleanup) from the confirmed primary export outcome.
/// A secondary failure is returned as a diagnostic string and never propagated as an operation error.
/// </summary>
internal static class SecondaryFailureGuard
{
    public static string? TryWrite(
        Action<string, string, string> write,
        string level,
        string code,
        string message)
    {
        try
        {
            write(level, code, message);
            return null;
        }
        catch (Exception ex)
        {
            var failure = Failure(ex);
            try
            {
                write("ERROR", "LOG_WRITE_FAILED", code + ": " + failure);
            }
            catch
            {
                // A completely unavailable sink must not affect the primary outcome.
            }

            return code + " logging failed: " + failure;
        }
    }

    public static string Failure(Exception ex) => ex.GetType().Name + ": " + ex.Message;

    public static IReadOnlyList<string>? WithError(IReadOnlyList<string>? existing, string? error)
    {
        if (string.IsNullOrEmpty(error))
            return existing;

        var combined = new List<string>();
        if (existing is not null)
            combined.AddRange(existing);
        combined.Add(error);
        return combined;
    }

    public static IReadOnlyList<string>? Combine(
        IReadOnlyList<string>? first,
        IReadOnlyList<string>? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;

        var combined = new List<string>(first.Count + second.Count);
        combined.AddRange(first);
        combined.AddRange(second);
        return combined;
    }
}
