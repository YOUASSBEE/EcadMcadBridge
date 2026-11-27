using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Altium.Application;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class ExportRequestPipelineTests
{
    [Fact]
    public void SingleRequest_InvokesEveryBoundaryExactlyOnce()
    {
        var counts = new BoundaryCounts();
        ExportInvocation? writerInvocation = null;
        var pipeline = CreatePipeline(counts, writerCallback: invocation => writerInvocation = invocation);

        var result = pipeline.Execute("request-1");

        Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status);
        Assert.Equal(1, counts.Gate);
        Assert.Equal(1, counts.PackageRoot);
        Assert.Equal(1, counts.Workspace);
        Assert.Equal(1, counts.Snapshot);
        Assert.Equal(1, counts.Model);
        Assert.Equal(1, counts.Board);
        Assert.Equal(1, counts.Writer);
        Assert.NotNull(writerInvocation);
        Assert.Equal("request-1", writerInvocation!.RequestId);
        Assert.False(string.IsNullOrWhiteSpace(writerInvocation.CorrelationId));
        Assert.False(string.IsNullOrWhiteSpace(writerInvocation.InvocationId));
        Assert.Equal(writerInvocation.CorrelationId, result.Outcome.CorrelationId);
        Assert.Equal(writerInvocation.InvocationId, result.Outcome.InvocationId);
    }

    [Fact]
    public void SameRequestIdRetry_HasZeroSecondInvocationCounts()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(counts);

        var first = pipeline.Execute("duplicate");
        var beforeRetry = counts.Values();
        var second = pipeline.Execute("duplicate");

        Assert.Equal(ExportRequestStatus.Completed, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Outcome.Status);
        Assert.Equal(beforeRetry, counts.Values());
        Assert.Equal(1, counts.Snapshot);
        Assert.Equal(1, counts.Model);
        Assert.Equal(1, counts.Board);
        Assert.Equal(1, counts.Writer);
        Assert.Equal(first.Outcome.CorrelationId, second.Outcome.CorrelationId);
    }

    [Fact]
    public void DifferentRequestIds_AreProcessedIndependently()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(counts);
        var invocationIds = new List<string>();

        var first = pipeline.Execute("request-a");
        var second = pipeline.Execute("request-b");

        Assert.Equal(ExportRequestStatus.Completed, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.Completed, second.Outcome.Status);
        Assert.NotEqual(first.Outcome.CorrelationId, second.Outcome.CorrelationId);
        Assert.NotEqual(first.Outcome.InvocationId, second.Outcome.InvocationId);
        Assert.Equal(2, counts.Snapshot);
        Assert.Equal(2, counts.Model);
        Assert.Equal(2, counts.Board);
        Assert.Equal(2, counts.Writer);
    }

    [Fact]
    public async Task ConcurrentSameRequestId_RunsOnlyOnePipeline()
    {
        var counts = new BoundaryCounts();
        using var snapshotEntered = new ManualResetEventSlim(false);
        using var releaseSnapshot = new ManualResetEventSlim(false);
        var pipeline = CreatePipeline(
            counts,
            snapshotAction: () =>
            {
                snapshotEntered.Set();
                Assert.True(releaseSnapshot.Wait(TimeSpan.FromSeconds(5)));
            });

        var first = Task.Run(() => pipeline.Execute("concurrent"));
        Assert.True(snapshotEntered.Wait(TimeSpan.FromSeconds(5)));
        var second = pipeline.Execute("concurrent");
        releaseSnapshot.Set();
        await first;

        Assert.Equal(ExportRequestStatus.Completed, (await first).Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Outcome.Status);
        Assert.Equal(1, counts.Snapshot);
        Assert.Equal(1, counts.Model);
        Assert.Equal(1, counts.Board);
        Assert.Equal(1, counts.Writer);
    }

    [Fact]
    public void SnapshotReaderException_StopsBeforeModelBoardAndWriter()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(
            counts,
            snapshotAction: () => throw new IOException("snapshot failed"));

        var result = pipeline.Execute("snapshot-exception");

        Assert.Equal(ExportRequestStatus.Failed, result.Outcome.Status);
        Assert.Contains("snapshot failed", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, counts.Snapshot);
        Assert.Equal(0, counts.Model);
        Assert.Equal(0, counts.Board);
        Assert.Equal(0, counts.Writer);
    }

    [Fact]
    public void ModelReaderException_StopsBeforeBoardAndWriter()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(
            counts,
            modelAction: () => throw new IOException("model failed"));

        var result = pipeline.Execute("model-exception");

        Assert.Equal(ExportRequestStatus.Failed, result.Outcome.Status);
        Assert.Contains("model failed", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, counts.Snapshot);
        Assert.Equal(1, counts.Model);
        Assert.Equal(0, counts.Board);
        Assert.Equal(0, counts.Writer);
    }

    [Fact]
    public void BoardExporterException_StopsBeforeWriter()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(
            counts,
            boardAction: () => throw new IOException("board export failed"));

        var result = pipeline.Execute("board-exception");

        Assert.Equal(ExportRequestStatus.Failed, result.Outcome.Status);
        Assert.Contains("board export failed", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, counts.Snapshot);
        Assert.Equal(1, counts.Model);
        Assert.Equal(1, counts.Board);
        Assert.Equal(0, counts.Writer);
    }

    [Fact]
    public void PackageWriterException_FailsClosedAndRejectsRetry()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(
            counts,
            writerAction: () => throw new IOException("writer failed"));

        var first = pipeline.Execute("writer-exception");
        var second = pipeline.Execute("writer-exception");

        Assert.Equal(ExportRequestStatus.Failed, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Outcome.Status);
        Assert.Contains("writer failed", first.Message, StringComparison.Ordinal);
        Assert.Equal(1, counts.Snapshot);
        Assert.Equal(1, counts.Model);
        Assert.Equal(1, counts.Board);
        Assert.Equal(1, counts.Writer);
    }

    [Fact]
    public void UncertainOutcome_IsTerminalAndRejectsRetry()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(
            counts,
            writerResult: () => new ExportInvocationResult(
                ExportRequestStatus.Uncertain,
                "verification failed"));

        var first = pipeline.Execute("uncertain");
        var second = pipeline.Execute("uncertain");

        Assert.Equal(ExportRequestStatus.Uncertain, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Outcome.Status);
        Assert.Equal(1, counts.Snapshot);
        Assert.Equal(1, counts.Model);
        Assert.Equal(1, counts.Board);
        Assert.Equal(1, counts.Writer);
    }

    [Fact]
    public void OwnershipGateFailure_RunsNoExportBoundary()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(
            counts,
            gateResult: ExportGateResult.Reject("document ownership failed"));

        var result = pipeline.Execute("gate-rejected");
        var retry = pipeline.Execute("gate-rejected");

        Assert.Equal(ExportRequestStatus.GateRejected, result.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, retry.Outcome.Status);
        Assert.Equal(1, counts.Gate);
        Assert.Equal(0, counts.PackageRoot);
        Assert.Equal(0, counts.Workspace);
        Assert.Equal(0, counts.Snapshot);
        Assert.Equal(0, counts.Model);
        Assert.Equal(0, counts.Board);
        Assert.Equal(0, counts.Writer);
    }

    [Fact]
    public void RepeatedCallback_HasNoAdditionalSideEffects()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(counts);

        var first = pipeline.Execute("callback");
        var afterFirst = counts.Values();
        var second = pipeline.Execute("callback");
        var third = pipeline.Execute("callback");

        Assert.Equal(ExportRequestStatus.Completed, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, second.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, third.Outcome.Status);
        Assert.Equal(afterFirst, counts.Values());
        Assert.Equal(1, counts.Snapshot);
        Assert.Equal(1, counts.Model);
        Assert.Equal(1, counts.Board);
        Assert.Equal(1, counts.Writer);
    }

    [Fact]
    public void RejectedGate_DoesNotInvokeExportBoundaries()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(
            counts,
            gateResult: ExportGateResult.Reject("export gate rejected"));

        var result = pipeline.Execute("rejected-gate");

        Assert.Equal(ExportRequestStatus.GateRejected, result.Outcome.Status);
        Assert.Contains("export gate rejected", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, counts.PackageRoot);
        Assert.Equal(0, counts.Workspace);
        Assert.Equal(0, counts.Snapshot);
        Assert.Equal(0, counts.Model);
        Assert.Equal(0, counts.Board);
        Assert.Equal(0, counts.Writer);
    }

    private static ExportRequestPipeline CreatePipeline(
        BoundaryCounts counts,
        ExportGateResult? gateResult = null,
        Action? snapshotAction = null,
        Action? modelAction = null,
        Action? boardAction = null,
        Action? writerAction = null,
        Func<ExportInvocationResult>? writerResult = null,
        Action<ExportInvocation>? writerCallback = null)
    {
        var coordinator = new ExportRequestCoordinator((_, _, _) => { });
        return new ExportRequestPipeline(
            coordinator,
            () => { counts.Gate++; return gateResult ?? ExportGateResult.Allow(); },
            () => { counts.PackageRoot++; return ExportPackageRootResult.Allow("package-root"); },
            () => { counts.Workspace++; return "workspace"; },
            _ => { },
            () =>
            {
                counts.Snapshot++;
                snapshotAction?.Invoke();
                return Snapshot();
            },
            (_, _) =>
            {
                counts.Model++;
                modelAction?.Invoke();
                return Model();
            },
            (_, _) =>
            {
                counts.Board++;
                boardAction?.Invoke();
                return Board();
            },
            (_, invocation) =>
            {
                counts.Writer++;
                writerCallback?.Invoke(invocation);
                writerAction?.Invoke();
                return writerResult?.Invoke()
                    ?? new ExportInvocationResult(ExportRequestStatus.Completed);
            },
            Array.Empty<string>(),
            (_, _, _) => { });
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

    private sealed class BoundaryCounts
    {
        public int Gate;
        public int PackageRoot;
        public int Workspace;
        public int Snapshot;
        public int Model;
        public int Board;
        public int Writer;

        public (int Gate, int PackageRoot, int Workspace, int Snapshot, int Model, int Board, int Writer)
            Values() => (Gate, PackageRoot, Workspace, Snapshot, Model, Board, Writer);
    }
}
