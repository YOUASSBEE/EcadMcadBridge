namespace EcadMcadBridge.Altium.Application;

internal enum ExportRequestStatus
{
    InProgress,
    Completed,
    Failed,
    Uncertain,
    GateRejected,
    DuplicateRejected,
}

internal sealed record ExportGateResult(bool Allowed, string? Error = null)
{
    public static ExportGateResult Allow() => new(true);

    public static ExportGateResult Reject(string error) => new(false, error);
}

internal sealed record ExportInvocation(string RequestId, string CorrelationId, string InvocationId);

internal sealed record ExportInvocationResult(ExportRequestStatus Status, string? Error = null);

internal sealed record ExportRequestResult(
    string RequestId,
    string CorrelationId,
    string? InvocationId,
    ExportRequestStatus Status,
    bool Invoked,
    string? Error);

/// <summary>
/// Request-scoped at-most-once dispatch for the package export boundary. A request keeps its
/// terminal state for the lifetime of the process; retries and duplicate command dispatches
/// are rejected instead of creating a second package.
/// </summary>
internal sealed class ExportRequestCoordinator
{
    private sealed class Entry(string correlationId)
    {
        public string CorrelationId { get; } = correlationId;

        public string? InvocationId { get; set; }

        public ExportRequestStatus Status { get; set; } = ExportRequestStatus.InProgress;
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Action<string, string, string> _write;

    public ExportRequestCoordinator(Action<string, string, string> write)
    {
        _write = write;
    }

    public ExportRequestResult Execute(
        string requestId,
        Func<ExportGateResult> gate,
        Func<ExportInvocation, ExportInvocationResult> export)
    {
        ArgumentException.ThrowIfNullOrEmpty(requestId);

        Entry entry;
        lock (_sync)
        {
            if (_entries.TryGetValue(requestId, out var existing))
            {
                var duplicate = new ExportRequestResult(
                    requestId,
                    existing.CorrelationId,
                    existing.InvocationId,
                    ExportRequestStatus.DuplicateRejected,
                    Invoked: false,
                    "Original request is " + existing.Status + ".");
                _write("WARN", "EXPORT_DUPLICATE_REJECTED", Describe(duplicate));
                return duplicate;
            }

            entry = new Entry(Guid.NewGuid().ToString("D"));
            _entries.Add(requestId, entry);
        }

        _write("INFO", "EXPORT_REQUEST_START", Describe(entry, requestId));

        ExportGateResult gateResult;
        try
        {
            gateResult = gate() ?? throw new InvalidOperationException("Export gate returned no result.");
        }
        catch (Exception ex)
        {
            return Complete(entry, requestId, new ExportInvocationResult(
                ExportRequestStatus.Failed,
                Failure(ex)),
                Invoked: false);
        }

        if (!gateResult.Allowed)
        {
            return Complete(entry, requestId, new ExportInvocationResult(
                ExportRequestStatus.GateRejected,
                gateResult.Error ?? "Export gate rejected the request."),
                Invoked: false);
        }

        var invocation = new ExportInvocation(
            requestId,
            entry.CorrelationId,
            Guid.NewGuid().ToString("D"));
        lock (_sync)
        {
            entry.InvocationId = invocation.InvocationId;
        }

        ExportInvocationResult result;
        try
        {
            result = export(invocation) ?? throw new InvalidOperationException("Export returned no result.");
        }
        catch (Exception ex)
        {
            return Complete(entry, requestId, new ExportInvocationResult(
                ExportRequestStatus.Failed,
                Failure(ex)),
                Invoked: true);
        }

        return Complete(entry, requestId, result, Invoked: true);
    }

    private ExportRequestResult Complete(
        Entry entry,
        string requestId,
        ExportInvocationResult result,
        bool Invoked)
    {
        lock (_sync)
        {
            entry.Status = result.Status;
        }

        var response = new ExportRequestResult(
            requestId,
            entry.CorrelationId,
            entry.InvocationId,
            result.Status,
            Invoked,
            result.Error);
        var level = result.Status == ExportRequestStatus.Completed ? "INFO" : "WARN";
        _write(level, "EXPORT_REQUEST_END", Describe(response));
        return response;
    }

    private static string Failure(Exception ex) => ex.GetType().Name + ": " + ex.Message;

    private static string Describe(Entry entry, string requestId) => string.Concat(
        "requestId=", requestId,
        " correlationId=", entry.CorrelationId,
        " invocationId=", entry.InvocationId ?? "none",
        " outcome=", entry.Status);

    private static string Describe(ExportRequestResult result) => string.Concat(
        "requestId=", result.RequestId,
        " correlationId=", result.CorrelationId,
        " invocationId=", result.InvocationId ?? "none",
        " outcome=", result.Status,
        string.IsNullOrEmpty(result.Error) ? string.Empty : " error=" + result.Error);
}
