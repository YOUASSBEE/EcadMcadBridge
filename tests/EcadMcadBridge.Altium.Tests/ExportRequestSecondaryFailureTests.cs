using System.IO;
using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Altium.Application;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

/// <summary>
/// BRIDGE-000050: secondary logging and cleanup failures must never change a confirmed
/// export outcome. All scenarios inject real exceptions into the relevant boundaries and
/// assert actual boundary invocation counts.
/// </summary>
public sealed class ExportRequestSecondaryFailureTests
{
    [Fact]
    public void SuccessfulExport_WithHealthySecondaryBoundaries_IsCompleted()
    {
        var sink = new LogSink();
        var (pipeline, counts) = CreatePipeline(sink);

        var result = pipeline.Execute("healthy");

        Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status);
        Assert.Null(result.Outcome.SecondaryErrors);
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
        Assert.Equal(1, sink.Count("EXPORT_REQUEST_START"));
        Assert.Equal(1, sink.Count("EXPORT_REQUEST_END"));
        Assert.Equal(1, sink.Count("PACKAGE_EXPORT_OK"));
    }

    [Fact]
    public void SuccessfulExport_WithEndLoggingException_RemainsCompleted()
    {
        var sink = new LogSink("EXPORT_REQUEST_END");
        var (pipeline, counts) = CreatePipeline(sink);

        var result = pipeline.Execute("end-log-failure");

        Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status);
        AssertHasSecondary(result, "EXPORT_REQUEST_END", "IOException: logging failed: EXPORT_REQUEST_END");
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
        Assert.Equal(0, sink.Count("EXPORT_REQUEST_END"));
        Assert.Equal(1, sink.Count("PACKAGE_EXPORT_OK"));
        Assert.Equal(1, sink.Count("LOG_WRITE_FAILED"));
    }

    [Fact]
    public void SuccessfulExport_WithPackageOkLoggingException_RemainsCompleted()
    {
        var sink = new LogSink("PACKAGE_EXPORT_OK");
        var (pipeline, counts) = CreatePipeline(sink);

        var result = pipeline.Execute("package-ok-log-failure");

        Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status);
        AssertHasSecondary(result, "PACKAGE_EXPORT_OK", "IOException: logging failed: PACKAGE_EXPORT_OK");
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
        Assert.Equal(0, sink.Count("PACKAGE_EXPORT_OK"));
        Assert.Equal(1, sink.Count("EXPORT_REQUEST_END"));
    }

    [Fact]
    public void SuccessfulExport_WithCleanupException_RemainsCompleted()
    {
        var sink = new LogSink();
        var (pipeline, counts) = CreatePipeline(
            sink,
            cleanupAction: () => throw new IOException("cleanup failed"));

        var result = pipeline.Execute("cleanup-failure");

        Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status);
        AssertHasSecondary(result, "MODEL_CLEANUP", "IOException: cleanup failed");
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
        Assert.Equal(1, sink.Count("SECONDARY_OPERATION_FAILED"));
        Assert.Equal(1, sink.Count("PACKAGE_EXPORT_OK"));
    }

    [Fact]
    public void SuccessfulExport_WithLoggingAndCleanupExceptions_RemainsCompleted()
    {
        var sink = new LogSink("EXPORT_REQUEST_END", "PACKAGE_EXPORT_OK");
        var (pipeline, counts) = CreatePipeline(
            sink,
            cleanupAction: () => throw new IOException("cleanup failed"));

        var result = pipeline.Execute("combined-secondary-failure");

        Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status);
        AssertHasSecondary(result, "EXPORT_REQUEST_END", "IOException: logging failed: EXPORT_REQUEST_END");
        AssertHasSecondary(result, "PACKAGE_EXPORT_OK", "IOException: logging failed: PACKAGE_EXPORT_OK");
        AssertHasSecondary(result, "MODEL_CLEANUP", "IOException: cleanup failed");
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
        Assert.Equal(0, sink.Count("PACKAGE_EXPORT_OK"));
    }

    [Fact]
    public void PrimaryOperationException_BeforeConfirmedSuccess_FailsClosed()
    {
        var sink = new LogSink();
        var (pipeline, counts) = CreatePipeline(
            sink,
            writerAction: () => throw new IOException("writer failed"));

        var first = pipeline.Execute("primary-exception");
        var second = pipeline.Execute("primary-exception");

        Assert.Equal(ExportRequestStatus.Failed, first.Outcome.Status);
        Assert.Contains("writer failed", first.Message, StringComparison.Ordinal);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Outcome.Status);
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
        Assert.Equal(0, sink.Count("PACKAGE_EXPORT_OK"));
    }

    [Fact]
    public void UncertainOutcome_IsTerminalWithoutClaimingSuccess()
    {
        var sink = new LogSink();
        var (pipeline, counts) = CreatePipeline(
            sink,
            writerResult: () => new ExportInvocationResult(
                ExportRequestStatus.Uncertain,
                "verification failed"));

        var first = pipeline.Execute("uncertain");
        var second = pipeline.Execute("uncertain");

        Assert.Equal(ExportRequestStatus.Uncertain, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Outcome.Status);
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
        Assert.Equal(0, sink.Count("PACKAGE_EXPORT_OK"));
    }

    [Fact]
    public void DuplicateAfterSuccessfulExportWithSecondaryFailure_DoesNotRedispatch()
    {
        var sink = new LogSink("EXPORT_REQUEST_END", "PACKAGE_EXPORT_OK");
        var (pipeline, counts) = CreatePipeline(
            sink,
            cleanupAction: () => throw new IOException("cleanup failed"));

        var first = pipeline.Execute("duplicate-after-secondary-failure");
        var afterFirst = counts.Values();
        var second = pipeline.Execute("duplicate-after-secondary-failure");

        Assert.Equal(ExportRequestStatus.Completed, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Outcome.Status);
        Assert.Equal(afterFirst, counts.Values());
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
        Assert.Equal(0, sink.Count("PACKAGE_EXPORT_OK"));
        Assert.Equal(1, sink.Count("EXPORT_DUPLICATE_REJECTED"));
    }

    [Fact]
    public void DuplicateAfterFailedExport_DoesNotRedispatch()
    {
        var sink = new LogSink();
        var (pipeline, counts) = CreatePipeline(
            sink,
            writerAction: () => throw new IOException("writer failed"));

        var first = pipeline.Execute("duplicate-after-failure");
        var afterFirst = counts.Values();
        var second = pipeline.Execute("duplicate-after-failure");

        Assert.Equal(ExportRequestStatus.Failed, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Outcome.Status);
        Assert.Equal(afterFirst, counts.Values());
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
    }

    [Fact]
    public void DuplicateAfterUncertainOutcome_DoesNotRedispatch()
    {
        var sink = new LogSink();
        var (pipeline, counts) = CreatePipeline(
            sink,
            writerResult: () => new ExportInvocationResult(
                ExportRequestStatus.Uncertain,
                "verification failed"));

        var first = pipeline.Execute("duplicate-after-uncertain");
        var afterFirst = counts.Values();
        var second = pipeline.Execute("duplicate-after-uncertain");

        Assert.Equal(ExportRequestStatus.Uncertain, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Outcome.Status);
        Assert.Equal(afterFirst, counts.Values());
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
    }

    [Fact]
    public async Task ParallelDuplicateDuringExecution_RunsOnlyOnePipeline()
    {
        var sink = new LogSink();
        using var snapshotEntered = new ManualResetEventSlim(false);
        using var releaseSnapshot = new ManualResetEventSlim(false);
        var (pipeline, counts) = CreatePipeline(
            sink,
            snapshotAction: () =>
            {
                snapshotEntered.Set();
                Assert.True(releaseSnapshot.Wait(TimeSpan.FromSeconds(5)));
            });

        var firstTask = Task.Run(() => pipeline.Execute("parallel-duplicate"));
        Assert.True(snapshotEntered.Wait(TimeSpan.FromSeconds(5)));
        var second = pipeline.Execute("parallel-duplicate");
        releaseSnapshot.Set();
        var first = await firstTask;

        Assert.Equal(ExportRequestStatus.Completed, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Outcome.Status);
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
        Assert.Equal(1, sink.Count("PACKAGE_EXPORT_OK"));
    }

    [Fact]
    public void IndependentRequestIds_AreProcessedSeparately()
    {
        var sink = new LogSink();
        var (pipeline, counts) = CreatePipeline(sink);

        var first = pipeline.Execute("independent-a");
        var second = pipeline.Execute("independent-b");

        Assert.Equal(ExportRequestStatus.Completed, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.Completed, second.Outcome.Status);
        Assert.Equal((2, 2, 2, 2, 2, 2, 2, 2), counts.Values());
        Assert.NotEqual(first.Outcome.InvocationId, second.Outcome.InvocationId);
        Assert.Equal(2, sink.Count("PACKAGE_EXPORT_OK"));
    }

    [Fact]
    public void SuccessfulExport_WithStartLoggingException_RemainsCompleted()
    {
        var sink = new LogSink("EXPORT_REQUEST_START");
        var (pipeline, counts) = CreatePipeline(sink);

        var result = pipeline.Execute("start-log-failure");

        Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status);
        AssertHasSecondary(result, "EXPORT_REQUEST_START", "IOException: logging failed: EXPORT_REQUEST_START");
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
        Assert.Equal(1, sink.Count("PACKAGE_EXPORT_OK"));
    }

    [Fact]
    public void DuplicateLoggingException_DoesNotChangeEitherOutcome()
    {
        var sink = new LogSink("EXPORT_DUPLICATE_REJECTED");
        var (pipeline, counts) = CreatePipeline(sink);

        var first = pipeline.Execute("duplicate-log-failure");
        var second = pipeline.Execute("duplicate-log-failure");

        Assert.Equal(ExportRequestStatus.Completed, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Outcome.Status);
        AssertHasSecondary(second, "EXPORT_DUPLICATE_REJECTED", "IOException: logging failed: EXPORT_DUPLICATE_REJECTED");
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
        Assert.Equal(1, sink.Count("PACKAGE_EXPORT_OK"));
    }

    private static void AssertHasSecondary(
        ExportPipelineResult result,
        string code,
        string? detail = null)
    {
        var errors = result.Outcome.SecondaryErrors;
        Assert.NotNull(errors);
        Assert.Contains(errors!, item => item.Contains(code, StringComparison.Ordinal));
        var error = errors!.First(item => item.Contains(code, StringComparison.Ordinal));
        if (detail is not null)
            Assert.Contains(detail, error, StringComparison.Ordinal);
    }

    private static (ExportRequestPipeline Pipeline, BoundaryCounts Counts) CreatePipeline(
        LogSink sink,
        Action? snapshotAction = null,
        Action? cleanupAction = null,
        Action? writerAction = null,
        Func<ExportInvocationResult>? writerResult = null,
        Action<ExportInvocation>? writerCallback = null)
    {
        var counts = new BoundaryCounts();
        var coordinator = new ExportRequestCoordinator(sink.Write);
        var pipeline = new ExportRequestPipeline(
            coordinator,
            () => { counts.Gate++; return ExportGateResult.Allow(); },
            () => { counts.PackageRoot++; return ExportPackageRootResult.Allow("package-root"); },
            () => { counts.Workspace++; return "workspace"; },
            _ =>
            {
                counts.Cleanup++;
                cleanupAction?.Invoke();
            },
            () =>
            {
                counts.Snapshot++;
                snapshotAction?.Invoke();
                return Snapshot();
            },
            (_, _) =>
            {
                counts.Model++;
                return Model();
            },
            (_, _) =>
            {
                counts.Board++;
                return Board();
            },
            (_, invocation) =>
            {
                counts.Writer++;
                writerCallback?.Invoke(invocation);
                writerAction?.Invoke();
                var result = writerResult?.Invoke()
                    ?? new ExportInvocationResult(ExportRequestStatus.Completed);
                if (result.Status == ExportRequestStatus.Completed)
                {
                    var successLogError = SecondaryFailureGuard.TryWrite(
                        sink.Write,
                        "INFO",
                        "PACKAGE_EXPORT_OK",
                        "packageId=" + invocation.InvocationId);
                    result = result with
                    {
                        SecondaryErrors = SecondaryFailureGuard.WithError(
                            result.SecondaryErrors,
                            successLogError),
                    };
                }

                return result;
            },
            Array.Empty<string>(),
            sink.Write);
        return (pipeline, counts);
    }

    private static AltiumBoardSnapshotResult Snapshot() => new(
        Snapshot: null!,
        Board: null!,
        ComponentIdentities: Array.Empty<AltiumComponentIdentity>(),
        Warnings: Array.Empty<string>(),
        Regions: Array.Empty<AltiumRegionInfo>(),
        Diagnostics: new AltiumAdapterDiagnostics());

    private static AltiumComponentModelResult Model() => new(
        Request: null!,
        Warnings: Array.Empty<string>(),
        Diagnostics: new AltiumComponentModelDiagnostics());

    private static AltiumBoardModelExportResult Board() => new(
        StepPath: null,
        Sha256: null,
        Size: 0,
        Warnings: Array.Empty<string>(),
        Error: null);

    private sealed class LogSink(params string[] throwOnCodes)
    {
        private readonly object _sync = new();
        private readonly HashSet<string> _throwOnCodes =
            new(throwOnCodes, StringComparer.Ordinal);
        private readonly List<LogEntry> _entries = new();

        public void Write(string level, string code, string message)
        {
            if (_throwOnCodes.Contains(code))
                throw new IOException("logging failed: " + code);

            lock (_sync)
                _entries.Add(new LogEntry(level, code, message));
        }

        public int Count(string code)
        {
            lock (_sync)
                return _entries.Count(entry => entry.Code == code);
        }
    }

    private sealed record LogEntry(string Level, string Code, string Message);

    private sealed class BoundaryCounts
    {
        public int Gate;
        public int PackageRoot;
        public int Workspace;
        public int Snapshot;
        public int Model;
        public int Board;
        public int Writer;
        public int Cleanup;

        public (int Gate, int PackageRoot, int Workspace, int Snapshot, int Model, int Board, int Writer, int Cleanup)
            Values() => (Gate, PackageRoot, Workspace, Snapshot, Model, Board, Writer, Cleanup);
    }
}
