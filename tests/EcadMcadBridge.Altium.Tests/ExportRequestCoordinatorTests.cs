using EcadMcadBridge.Altium.Application;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class ExportRequestCoordinatorTests
{
    [Fact]
    public void SingleRequest_ReservesBeforeOperationAndLogsAllIdentifiers()
    {
        var logs = new List<LogEntry>();
        var coordinator = CreateCoordinator(logs);
        var operationCalls = 0;
        ExportInvocation? invocation = null;

        var result = coordinator.Execute(
            "request-1",
            request =>
            {
                operationCalls++;
                invocation = request;
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            });

        Assert.Equal(ExportRequestStatus.Completed, result.Status);
        Assert.True(result.Invoked);
        Assert.Equal(1, operationCalls);
        Assert.NotNull(invocation);
        Assert.Equal("request-1", invocation!.RequestId);
        Assert.False(string.IsNullOrWhiteSpace(invocation.CorrelationId));
        Assert.False(string.IsNullOrWhiteSpace(invocation.InvocationId));
        Assert.Equal(invocation.CorrelationId, result.CorrelationId);
        Assert.Equal(invocation.InvocationId, result.InvocationId);

        var start = Assert.Single(logs, x => x.Code == "EXPORT_REQUEST_START");
        var end = Assert.Single(logs, x => x.Code == "EXPORT_REQUEST_END");
        Assert.Contains("requestId=request-1", start.Message, StringComparison.Ordinal);
        Assert.Contains("correlationId=" + result.CorrelationId, start.Message, StringComparison.Ordinal);
        Assert.Contains("invocationId=" + result.InvocationId, start.Message, StringComparison.Ordinal);
        Assert.Contains("outcome=" + nameof(ExportRequestStatus.InProgress), start.Message, StringComparison.Ordinal);
        Assert.Contains("requestId=request-1", end.Message, StringComparison.Ordinal);
        Assert.Contains("correlationId=" + result.CorrelationId, end.Message, StringComparison.Ordinal);
        Assert.Contains("invocationId=" + result.InvocationId, end.Message, StringComparison.Ordinal);
        Assert.Contains("outcome=" + nameof(ExportRequestStatus.Completed), end.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SameRequestIdRetry_IsRejectedWithoutSecondOperation()
    {
        var logs = new List<LogEntry>();
        var coordinator = CreateCoordinator(logs);
        var operationCalls = 0;

        ExportInvocationResult Operation(ExportInvocation _)
        {
            operationCalls++;
            return new ExportInvocationResult(ExportRequestStatus.Completed);
        }

        var first = coordinator.Execute("duplicate", Operation);
        var second = coordinator.Execute(
            "duplicate",
            _ => throw new InvalidOperationException("operation must not run on duplicate"));

        Assert.Equal(ExportRequestStatus.Completed, first.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Status);
        Assert.False(second.Invoked);
        Assert.Equal(first.CorrelationId, second.CorrelationId);
        Assert.Equal(1, operationCalls);
        Assert.Contains(
            logs,
            x => x.Code == "EXPORT_DUPLICATE_REJECTED"
                 && x.Level == "WARN"
                 && x.Message.Contains("requestId=duplicate", StringComparison.Ordinal));
        Assert.Contains(
            logs,
            x => x.Code == "EXPORT_DUPLICATE_REJECTED"
                 && x.Message.Contains("outcome=" + nameof(ExportRequestStatus.DuplicateRejected), StringComparison.Ordinal));
    }

    [Fact]
    public void DifferentRequestIds_AreProcessedIndependently()
    {
        var logs = new List<LogEntry>();
        var coordinator = CreateCoordinator(logs);
        var invocationIds = new List<string>();

        ExportInvocationResult Operation(ExportInvocation request)
        {
            invocationIds.Add(request.InvocationId);
            return new ExportInvocationResult(ExportRequestStatus.Completed);
        }

        var first = coordinator.Execute("request-a", Operation);
        var second = coordinator.Execute("request-b", Operation);

        Assert.Equal(ExportRequestStatus.Completed, first.Status);
        Assert.Equal(ExportRequestStatus.Completed, second.Status);
        Assert.Equal(2, invocationIds.Count);
        Assert.NotEqual(invocationIds[0], invocationIds[1]);
        Assert.NotEqual(first.CorrelationId, second.CorrelationId);
        Assert.Equal(2, logs.Count(x => x.Code == "EXPORT_REQUEST_START"));
    }

    [Fact]
    public async Task ConcurrentSameRequestId_HasExactlyOneOperation()
    {
        var coordinator = CreateCoordinator(new List<LogEntry>());
        using var operationEntered = new ManualResetEventSlim(false);
        using var releaseOperation = new ManualResetEventSlim(false);
        var operationCalls = 0;

        var first = Task.Run(() => coordinator.Execute(
            "concurrent",
            _ =>
            {
                Interlocked.Increment(ref operationCalls);
                operationEntered.Set();
                Assert.True(releaseOperation.Wait(TimeSpan.FromSeconds(5)));
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            }));

        Assert.True(operationEntered.Wait(TimeSpan.FromSeconds(5)));
        var second = coordinator.Execute(
            "concurrent",
            _ =>
            {
                Interlocked.Increment(ref operationCalls);
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            });
        releaseOperation.Set();
        await first;

        Assert.Equal(ExportRequestStatus.Completed, (await first).Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Status);
        Assert.Equal(1, operationCalls);
    }

    [Fact]
    public void GateRejectedOutcome_IsTerminal()
    {
        var coordinator = CreateCoordinator(new List<LogEntry>());
        var operationCalls = 0;

        ExportInvocationResult Operation(ExportInvocation _)
        {
            operationCalls++;
            return new ExportInvocationResult(
                ExportRequestStatus.GateRejected,
                "document validation failed");
        }

        var first = coordinator.Execute("gate-rejected", Operation);
        var second = coordinator.Execute("gate-rejected", Operation);

        Assert.Equal(ExportRequestStatus.GateRejected, first.Status);
        Assert.Equal("document validation failed", first.Error);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Status);
        Assert.Equal(1, operationCalls);
    }

    [Fact]
    public void OperationException_FailsClosedAndRejectsRetry()
    {
        var coordinator = CreateCoordinator(new List<LogEntry>());
        var operationCalls = 0;

        var first = coordinator.Execute(
            "operation-exception",
            _ =>
            {
                operationCalls++;
                throw new InvalidOperationException("pipeline failed");
            });
        var second = coordinator.Execute(
            "operation-exception",
            _ =>
            {
                operationCalls++;
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            });

        Assert.Equal(ExportRequestStatus.Failed, first.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Status);
        Assert.Equal(1, operationCalls);
        Assert.Contains("InvalidOperationException: pipeline failed", first.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UncertainOutcome_IsTerminalAndRejectsRetry()
    {
        var coordinator = CreateCoordinator(new List<LogEntry>());
        var operationCalls = 0;

        var first = coordinator.Execute(
            "uncertain",
            _ =>
            {
                operationCalls++;
                return new ExportInvocationResult(ExportRequestStatus.Uncertain, "verification failed");
            });
        var second = coordinator.Execute(
            "uncertain",
            _ =>
            {
                operationCalls++;
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            });

        Assert.Equal(ExportRequestStatus.Uncertain, first.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Status);
        Assert.Equal(1, operationCalls);
    }

    [Fact]
    public void ResolveRequestId_UsesExplicitIdOrStableDocumentFallback()
    {
        const string explicitId = "11111111-2222-3333-4444-555555555555";
        var explicitResult = ExportBoardSnapshotHandler.ResolveRequestId(
            " " + explicitId + " ",
            "C:\\boards\\fixture.PcbDoc");
        var fallbackResult = ExportBoardSnapshotHandler.ResolveRequestId(
            string.Empty,
            "C:\\boards\\fixture.PcbDoc");

        Assert.Equal(explicitId, explicitResult);
        Assert.StartsWith("doc-", fallbackResult, StringComparison.Ordinal);
        Assert.Equal(68, fallbackResult.Length);
        Assert.All(fallbackResult[4..], c => Assert.True(char.IsAsciiHexDigit(c)));
    }

    private static ExportRequestCoordinator CreateCoordinator(List<LogEntry> logs) =>
        new((level, code, message) => logs.Add(new LogEntry(level, code, message)));

    private sealed record LogEntry(string Level, string Code, string Message);
}
