using EcadMcadBridge.Altium.Application;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class ExportRequestCoordinatorTests
{
    [Fact]
    public void SingleRequest_InvokesExportBoundaryOnceAndLogsAllIdentifiers()
    {
        var logs = new List<LogEntry>();
        var coordinator = CreateCoordinator(logs);
        var gateCalls = 0;
        var exportCalls = 0;
        ExportInvocation? invocation = null;

        var result = coordinator.Execute(
            "request-1",
            () =>
            {
                gateCalls++;
                return ExportGateResult.Allow();
            },
            request =>
            {
                exportCalls++;
                invocation = request;
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            });

        Assert.Equal(ExportRequestStatus.Completed, result.Status);
        Assert.True(result.Invoked);
        Assert.Equal(1, gateCalls);
        Assert.Equal(1, exportCalls);
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
        Assert.Contains("outcome=" + nameof(ExportRequestStatus.InProgress), start.Message, StringComparison.Ordinal);
        Assert.Contains("requestId=request-1", end.Message, StringComparison.Ordinal);
        Assert.Contains("correlationId=" + result.CorrelationId, end.Message, StringComparison.Ordinal);
        Assert.Contains("invocationId=" + result.InvocationId, end.Message, StringComparison.Ordinal);
        Assert.Contains("outcome=" + nameof(ExportRequestStatus.Completed), end.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SameRequestIdRetry_IsRejectedWithoutSecondExport()
    {
        var logs = new List<LogEntry>();
        var coordinator = CreateCoordinator(logs);
        var exportCalls = 0;
        ExportInvocationResult Export(ExportInvocation _)
        {
            exportCalls++;
            return new ExportInvocationResult(ExportRequestStatus.Completed);
        }

        var first = coordinator.Execute("duplicate", ExportGateResult.Allow, Export);
        var second = coordinator.Execute(
            "duplicate",
            () => throw new InvalidOperationException("gate must not run on duplicate"),
            _ => throw new InvalidOperationException("export must not run on duplicate"));

        Assert.Equal(ExportRequestStatus.Completed, first.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Status);
        Assert.False(second.Invoked);
        Assert.Equal(first.CorrelationId, second.CorrelationId);
        Assert.Equal(1, exportCalls);
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

        var first = coordinator.Execute(
            "request-a",
            ExportGateResult.Allow,
            request =>
            {
                invocationIds.Add(request.InvocationId);
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            });
        var second = coordinator.Execute(
            "request-b",
            ExportGateResult.Allow,
            request =>
            {
                invocationIds.Add(request.InvocationId);
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            });

        Assert.Equal(ExportRequestStatus.Completed, first.Status);
        Assert.Equal(ExportRequestStatus.Completed, second.Status);
        Assert.Equal(2, invocationIds.Count);
        Assert.NotEqual(invocationIds[0], invocationIds[1]);
        Assert.NotEqual(first.CorrelationId, second.CorrelationId);
        Assert.Equal(2, logs.Count(x => x.Code == "EXPORT_REQUEST_START"));
    }

    [Fact]
    public async Task ConcurrentSameRequestId_HasExactlyOneExportBoundaryCall()
    {
        var coordinator = CreateCoordinator(new List<LogEntry>());
        using var exportEntered = new ManualResetEventSlim(false);
        using var releaseExport = new ManualResetEventSlim(false);
        var gateCalls = 0;
        var exportCalls = 0;

        var first = Task.Run(() => coordinator.Execute(
            "concurrent",
            () =>
            {
                Interlocked.Increment(ref gateCalls);
                return ExportGateResult.Allow();
            },
            _ =>
            {
                Interlocked.Increment(ref exportCalls);
                exportEntered.Set();
                Assert.True(releaseExport.Wait(TimeSpan.FromSeconds(5)));
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            }));

        Assert.True(exportEntered.Wait(TimeSpan.FromSeconds(5)));
        var second = coordinator.Execute(
            "concurrent",
            () =>
            {
                Interlocked.Increment(ref gateCalls);
                return ExportGateResult.Allow();
            },
            _ =>
            {
                Interlocked.Increment(ref exportCalls);
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            });
        releaseExport.Set();
        await first;

        Assert.Equal(ExportRequestStatus.Completed, (await first).Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Status);
        Assert.Equal(1, gateCalls);
        Assert.Equal(1, exportCalls);
    }

    [Fact]
    public void GateRejection_DoesNotCallExportBoundaryAndRemainsTerminal()
    {
        var coordinator = CreateCoordinator(new List<LogEntry>());
        var exportCalls = 0;

        var first = coordinator.Execute(
            "gate-rejected",
            () => ExportGateResult.Reject("document validation failed"),
            _ =>
            {
                exportCalls++;
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            });
        var second = coordinator.Execute(
            "gate-rejected",
            ExportGateResult.Allow,
            _ =>
            {
                exportCalls++;
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            });

        Assert.Equal(ExportRequestStatus.GateRejected, first.Status);
        Assert.Equal("document validation failed", first.Error);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Status);
        Assert.Equal(0, exportCalls);
    }

    [Fact]
    public void ExceptionBeforeExport_FailsClosedWithoutExportBoundaryCall()
    {
        var coordinator = CreateCoordinator(new List<LogEntry>());
        var exportCalls = 0;

        var result = coordinator.Execute(
            "gate-exception",
            () => throw new IOException("state check failed"),
            _ =>
            {
                exportCalls++;
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            });

        Assert.Equal(ExportRequestStatus.Failed, result.Status);
        Assert.False(result.Invoked);
        Assert.Equal(0, exportCalls);
        Assert.Contains("IOException: state check failed", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionDuringExport_FailsClosedAndRejectsRetry()
    {
        var coordinator = CreateCoordinator(new List<LogEntry>());
        var exportCalls = 0;

        var first = coordinator.Execute(
            "export-exception",
            ExportGateResult.Allow,
            _ =>
            {
                exportCalls++;
                throw new InvalidOperationException("writer failed");
            });
        var second = coordinator.Execute(
            "export-exception",
            ExportGateResult.Allow,
            _ =>
            {
                exportCalls++;
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            });

        Assert.Equal(ExportRequestStatus.Failed, first.Status);
        Assert.True(first.Invoked);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Status);
        Assert.Equal(1, exportCalls);
        Assert.Contains("InvalidOperationException: writer failed", first.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UncertainExportOutcome_IsTerminalAndRejectsRetry()
    {
        var coordinator = CreateCoordinator(new List<LogEntry>());
        var exportCalls = 0;

        var first = coordinator.Execute(
            "uncertain",
            ExportGateResult.Allow,
            _ =>
            {
                exportCalls++;
                return new ExportInvocationResult(
                    ExportRequestStatus.Uncertain,
                    "verification failed");
            });
        var second = coordinator.Execute(
            "uncertain",
            ExportGateResult.Allow,
            _ =>
            {
                exportCalls++;
                return new ExportInvocationResult(ExportRequestStatus.Completed);
            });

        Assert.Equal(ExportRequestStatus.Uncertain, first.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Status);
        Assert.Equal(1, exportCalls);
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
