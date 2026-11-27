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

internal sealed record ExportInvocationResult(
    ExportRequestStatus Status,
    string? Error = null,
    string? Message = null,
    bool Partial = false,
    IReadOnlyList<string>? SecondaryErrors = null);

internal sealed record ExportRequestResult(
    string RequestId,
    string CorrelationId,
    string? InvocationId,
    ExportRequestStatus Status,
    bool Invoked,
    string? Error,
    string? Message = null,
    bool Partial = false,
    IReadOnlyList<string>? SecondaryErrors = null);

/// <summary>
/// Request-scoped at-most-once dispatch. The request and its invocation are reserved before the
/// operation delegate is called, so a duplicate cannot reach any pipeline boundary. A request keeps
/// its terminal state for the lifetime of the process; uncertainty is never retried automatically.
/// Logging is a secondary boundary: its failure is diagnosed but never changes a confirmed outcome.
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
        Func<ExportInvocation, ExportInvocationResult> operation)
    {
        ArgumentException.ThrowIfNullOrEmpty(requestId);

        var diagnostics = new List<string>();
        Entry entry;
        ExportInvocation invocation;
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
                var duplicateError = SecondaryFailureGuard.TryWrite(
                    _write,
                    "WARN",
                    "EXPORT_DUPLICATE_REJECTED",
                    Describe(duplicate));
                return duplicate with
                {
                    SecondaryErrors = SecondaryFailureGuard.WithError(null, duplicateError),
                };
            }

            invocation = new ExportInvocation(
                requestId,
                Guid.NewGuid().ToString("D"),
                Guid.NewGuid().ToString("D"));
            entry = new Entry(invocation.CorrelationId) { InvocationId = invocation.InvocationId };
            _entries.Add(requestId, entry);
        }

        var startError = SecondaryFailureGuard.TryWrite(
            _write,
            "INFO",
            "EXPORT_REQUEST_START",
            Describe(entry, requestId));
        diagnostics.AddIfNotNull(startError);

        ExportInvocationResult result;
        try
        {
            result = operation(invocation)
                ?? throw new InvalidOperationException("Export operation returned no result.");
        }
        catch (Exception ex)
        {
            result = new ExportInvocationResult(
                ExportRequestStatus.Failed,
                SecondaryFailureGuard.Failure(ex));
        }

        lock (_sync)
        {
            entry.Status = result.Status;
        }

        if (result.SecondaryErrors is not null)
            diagnostics.AddRange(result.SecondaryErrors);

        var response = new ExportRequestResult(
            requestId,
            entry.CorrelationId,
            entry.InvocationId,
            result.Status,
            Invoked: true,
            result.Error,
            result.Message,
            result.Partial);
        var level = result.Status == ExportRequestStatus.Completed ? "INFO" : "WARN";
        var endError = SecondaryFailureGuard.TryWrite(
            _write,
            level,
            "EXPORT_REQUEST_END",
            Describe(response));
        diagnostics.AddIfNotNull(endError);

        return response with
        {
            SecondaryErrors = diagnostics.Count > 0 ? diagnostics.ToArray() : null,
        };
    }

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

internal static class DiagnosticListExtensions
{
    public static void AddIfNotNull(this List<string> diagnostics, string? error)
    {
        if (error is not null)
            diagnostics.Add(error);
    }
}
