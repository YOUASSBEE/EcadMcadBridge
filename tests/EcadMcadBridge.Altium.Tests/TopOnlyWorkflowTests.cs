using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Altium.Application;
using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using EcadMcadBridge.Core.Packaging;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

/// <summary>
/// BRIDGE-000053: Top-only selected-package workflow orchestration.
/// All CAD boundaries are delegate seams; the real SDK adapters stay outside these tests.
/// </summary>
public sealed class TopOnlyWorkflowTests
{
    private static readonly ModelTestSnapshot.ComponentSeed Top1 =
        ModelTestSnapshot.Seed("component-1", "R1", side: BoardSide.Top);

    private static readonly ModelTestSnapshot.ComponentSeed Top2 =
        ModelTestSnapshot.Seed("component-2", "R2", side: BoardSide.Top);

    private static readonly ModelTestSnapshot.ComponentSeed Bottom1 =
        ModelTestSnapshot.Seed("component-3", "C1", side: BoardSide.Bottom);

    [Fact]
    public void SuccessfulExport_SelectsExportsAndVerifiesOnlyTopComponents()
    {
        var counts = new BoundaryCounts();
        var requests = new List<ExportPackageWriteRequest>();
        var pipeline = CreatePipeline(
            new[] { Candidate(Top1), Candidate(Bottom1) },
            new[] { Top1, Bottom1 },
            counts,
            writerCallback: (request, _) => requests.Add(request));

        var result = pipeline.Execute("top-success");

        Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status);
        Assert.Equal((1, 1, 1, 1, 1, 1, 1, 1), counts.Values());
        var request = Assert.Single(requests);
        AssertTopOnly(request, Top1.Id);
        Assert.NotNull(result.Selection);
        Assert.Equal(2, result.Selection!.Selected.Count);
        Assert.Equal(new[] { "R1" }, result.Selection.Exported.Select(item => item.Reference));
        Assert.Contains(result.Selection.Skipped, item =>
            item.Reference == "C1" && item.Reason.Contains("Top", StringComparison.Ordinal));
        Assert.NotNull(result.Outcome.InvocationId);
    }

    [Fact]
    public void EmptySelection_RejectsWithoutRunningExportBoundaries()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(
            Array.Empty<TopOnlySelectionCandidate>(),
            new[] { Top1, Bottom1 },
            counts);

        var result = pipeline.Execute("empty-selection");

        Assert.Equal(ExportRequestStatus.GateRejected, result.Outcome.Status);
        Assert.Contains("Top-only contract", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, counts.Gate);
        Assert.Equal(1, counts.Snapshot);
        Assert.Equal(0, counts.PackageRoot);
        Assert.Equal(0, counts.Workspace);
        Assert.Equal(0, counts.Model);
        Assert.Equal(0, counts.Board);
        Assert.Equal(0, counts.Writer);
    }

    [Fact]
    public void BottomOnlySelection_RejectsWithoutExport()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(
            new[] { Candidate(Bottom1) },
            new[] { Top1, Bottom1 },
            counts);

        var result = pipeline.Execute("bottom-only");

        Assert.Equal(ExportRequestStatus.GateRejected, result.Outcome.Status);
        Assert.Equal(0, counts.Writer);
        Assert.Contains(result.Selection!.Skipped, item =>
            item.Reference == "C1" && item.Reason == "Only Top component side is supported.");
    }

    [Fact]
    public void MixedTopAndBottom_ExportsTopAndSkipsBottomWithReason()
    {
        var counts = new BoundaryCounts();
        var requests = new List<ExportPackageWriteRequest>();
        var pipeline = CreatePipeline(
            new[] { Candidate(Top1), Candidate(Bottom1), Candidate(Top2) },
            new[] { Top1, Bottom1, Top2 },
            counts,
            writerCallback: (request, _) => requests.Add(request));

        var result = pipeline.Execute("mixed");

        Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status);
        Assert.Equal(new[] { "R1", "R2" }, result.Selection!.Exported.Select(item => item.Reference));
        Assert.Equal(new[] { "C1" }, result.Selection.Skipped.Select(item => item.Reference));
        AssertTopOnly(Assert.Single(requests), Top1.Id, Top2.Id);
    }

    [Fact]
    public void OwnershipUnavailable_RejectsWithoutExport()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(
            new[] { Candidate(Top1, ownershipAvailable: false) },
            new[] { Top1, Bottom1 },
            counts);

        var result = pipeline.Execute("ownership-unavailable");

        Assert.Equal(ExportRequestStatus.GateRejected, result.Outcome.Status);
        Assert.Equal(0, counts.Writer);
        Assert.Contains(result.Selection!.Skipped, item =>
            item.Reason.Contains("ownership", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OwnershipUnproven_RejectsWithoutExport()
    {
        var counts = new BoundaryCounts();
        var unmatched = Candidate(Top1) with
        {
            Key = new AltiumComponentKey(null, "different-source", "R1", 42, 42, 0, BoardSide.Top),
        };
        var pipeline = CreatePipeline(
            new[] { unmatched },
            new[] { Top1, Bottom1 },
            counts);

        var result = pipeline.Execute("ownership-unproven");

        Assert.Equal(ExportRequestStatus.GateRejected, result.Outcome.Status);
        Assert.Equal(0, counts.Writer);
        Assert.Contains(result.Selection!.Skipped, item =>
            item.Reason.Contains("proven", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingModel_IsSkippedWhileOtherTopComponentIsExported()
    {
        var counts = new BoundaryCounts();
        var requests = new List<ExportPackageWriteRequest>();
        var pipeline = CreatePipeline(
            new[] { Candidate(Top1, modelAvailable: false), Candidate(Top2) },
            new[] { Top1, Top2, Bottom1 },
            counts,
            writerCallback: (request, _) => requests.Add(request));

        var result = pipeline.Execute("missing-model");

        Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status);
        Assert.Equal(new[] { "R2" }, result.Selection!.Exported.Select(item => item.Reference));
        Assert.Contains(result.Selection.Skipped, item =>
            item.Reference == "R1" && item.Reason.Contains("model", StringComparison.OrdinalIgnoreCase));
        AssertTopOnly(Assert.Single(requests), Top2.Id);
    }

    [Fact]
    public void MultipleTopComponents_AreExportedTogether()
    {
        var counts = new BoundaryCounts();
        var requests = new List<ExportPackageWriteRequest>();
        var pipeline = CreatePipeline(
            new[] { Candidate(Top1), Candidate(Top2) },
            new[] { Top1, Top2, Bottom1 },
            counts,
            writerCallback: (request, _) => requests.Add(request));

        var result = pipeline.Execute("multiple-top");

        Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status);
        Assert.Empty(result.Selection!.Skipped);
        AssertTopOnly(Assert.Single(requests), Top1.Id, Top2.Id);
    }

    [Fact]
    public void TwoIndependentOperations_HaveSeparateIdsInvocationAndPackageContents()
    {
        var counts = new BoundaryCounts();
        var requests = new List<ExportPackageWriteRequest>();
        var invocations = new List<ExportInvocation>();
        var pipeline = CreatePipeline(
            new[] { Candidate(Top1), Candidate(Bottom1) },
            new[] { Top1, Bottom1 },
            counts,
            writerCallback: (request, invocation) =>
            {
                invocations.Add(invocation);
                requests.Add(request);
            });

        var first = pipeline.Execute("operation-a");
        var second = pipeline.Execute("operation-b");

        Assert.Equal(ExportRequestStatus.Completed, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.Completed, second.Outcome.Status);
        Assert.NotEqual(first.Outcome.CorrelationId, second.Outcome.CorrelationId);
        Assert.NotEqual(first.Outcome.InvocationId, second.Outcome.InvocationId);
        Assert.Equal(2, requests.Count);
        AssertTopOnly(requests[0], Top1.Id);
        AssertTopOnly(requests[1], Top1.Id);
        Assert.Equal(2, invocations.Select(item => item.InvocationId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void RepeatFirstFixtureAsNewOperation_SucceedsIndependently()
    {
        var counts = new BoundaryCounts();
        var invocations = new List<ExportInvocation>();
        var pipeline = CreatePipeline(
            new[] { Candidate(Top1), Candidate(Bottom1) },
            new[] { Top1, Bottom1 },
            counts,
            writerCallback: (_, invocation) => invocations.Add(invocation));

        var first = pipeline.Execute("fixture-a-1");
        var second = pipeline.Execute("fixture-b-1");
        var repeated = pipeline.Execute("fixture-a-2");

        Assert.All(new[] { first, second, repeated }, result =>
            Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status));
        Assert.Equal(3, counts.Writer);
        Assert.Equal(3, invocations.Select(item => item.InvocationId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("fixture-a-1", first.Outcome.RequestId);
        Assert.Equal("fixture-a-2", repeated.Outcome.RequestId);
    }

    [Fact]
    public void FirstOperationFails_SecondOperationSucceeds()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(
            new[] { Candidate(Top1) },
            new[] { Top1, Bottom1 },
            counts,
            writerResult: (_, invocation) =>
                invocation.RequestId == "fails"
                    ? throw new IOException("writer failed")
                    : new ExportInvocationResult(ExportRequestStatus.Completed));

        var failed = pipeline.Execute("fails");
        var succeeded = pipeline.Execute("succeeds");

        Assert.Equal(ExportRequestStatus.Failed, failed.Outcome.Status);
        Assert.Contains("writer failed", failed.Message, StringComparison.Ordinal);
        Assert.Equal(ExportRequestStatus.Completed, succeeded.Outcome.Status);
        Assert.Equal(2, counts.Writer);
    }

    [Fact]
    public void DuplicateRequestId_IsRejectedWithoutSecondDispatch()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(
            new[] { Candidate(Top1) },
            new[] { Top1, Bottom1 },
            counts);

        var first = pipeline.Execute("same-id");
        var before = counts.Values();
        var duplicate = pipeline.Execute("same-id");

        Assert.Equal(ExportRequestStatus.Completed, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, duplicate.Outcome.Status);
        Assert.Equal(before, counts.Values());
        Assert.Equal(first.Outcome.CorrelationId, duplicate.Outcome.CorrelationId);
    }

    [Fact]
    public async Task ParallelDuplicateRequestId_RunsOnlyOnePipeline()
    {
        var counts = new BoundaryCounts();
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var pipeline = CreatePipeline(
            new[] { Candidate(Top1) },
            new[] { Top1, Bottom1 },
            counts,
            writerCallback: (_, _) =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            });

        var first = Task.Run(() => pipeline.Execute("parallel"));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var duplicate = pipeline.Execute("parallel");
        release.Set();
        var firstResult = await first;

        Assert.Equal(ExportRequestStatus.Completed, firstResult.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, duplicate.Outcome.Status);
        Assert.Equal(1, counts.Writer);
    }

    [Fact]
    public void VerificationFailure_ReturnsUncertainWithoutSuccessLogAndRejectsRetry()
    {
        var counts = new BoundaryCounts();
        var sink = new LogSink();
        var pipeline = CreatePipeline(
            new[] { Candidate(Top1) },
            new[] { Top1, Bottom1 },
            counts,
            sink,
            writerResult: (_, _) => new ExportInvocationResult(
                ExportRequestStatus.Uncertain,
                "Exchange Package verification failed; cleanup was requested."));

        var first = pipeline.Execute("uncertain");
        var retry = pipeline.Execute("uncertain");

        Assert.Equal(ExportRequestStatus.Uncertain, first.Outcome.Status);
        Assert.Equal(ExportRequestStatus.DuplicateRejected, retry.Outcome.Status);
        Assert.Contains("verification failed", first.Message, StringComparison.Ordinal);
        Assert.Equal(0, sink.Count("PACKAGE_EXPORT_OK"));
        Assert.Equal(1, counts.Writer);
    }

    [Fact]
    public void CleanupFailure_IsReportedAsSecondaryAndKeepsCompleted()
    {
        var counts = new BoundaryCounts();
        var pipeline = CreatePipeline(
            new[] { Candidate(Top1) },
            new[] { Top1, Bottom1 },
            counts,
            cleanupAction: () => throw new IOException("cleanup failed"));

        var result = pipeline.Execute("cleanup-failure");

        Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status);
        Assert.Equal(1, counts.Cleanup);
        Assert.Contains(
            result.Outcome.SecondaryErrors ?? Array.Empty<string>(),
            item => item.Contains("MODEL_CLEANUP", StringComparison.Ordinal));
    }

    [Fact]
    public void SecondaryLoggingFailure_IsReportedAndKeepsCompleted()
    {
        var counts = new BoundaryCounts();
        var sink = new LogSink("PACKAGE_EXPORT_OK", "EXPORT_REQUEST_END");
        var pipeline = CreatePipeline(
            new[] { Candidate(Top1) },
            new[] { Top1, Bottom1 },
            counts,
            sink);

        var result = pipeline.Execute("logging-failure");

        Assert.Equal(ExportRequestStatus.Completed, result.Outcome.Status);
        Assert.Contains(
            result.Outcome.SecondaryErrors ?? Array.Empty<string>(),
            item => item.Contains("PACKAGE_EXPORT_OK", StringComparison.Ordinal));
        Assert.Contains(
            result.Outcome.SecondaryErrors ?? Array.Empty<string>(),
            item => item.Contains("EXPORT_REQUEST_END", StringComparison.Ordinal));
    }

    [Fact]
    public void PackageVerifier_AcceptsVerifiedTopOnlyReadback()
    {
        using var temp = new TestTempDirectory();
        var source = Snapshot(new[] { Top1, Bottom1 });
        var filtered = TopOnlySnapshotBuilder.Filter(source, new HashSet<string> { Top1.Id });
        var written = ExchangePackageWriter.Write(
            temp.Path,
            filtered.Snapshot,
            WriteOptions(),
            Model(filtered).Request);

        Assert.True(written.IsSuccess, string.Join("; ", written.Errors.Select(item => item.Message)));
        var read = ExchangePackageReader.Read(written.PackageDirectory!);

        Assert.True(read.IsSuccess, string.Join("; ", read.Errors.Select(item => item.Message)));
        Assert.Null(TopOnlyPackageVerifier.Verify(read, filtered.Snapshot));
        Assert.All(read.Snapshot!.Components, component =>
            Assert.Equal(BoardSide.Top, component.Placement.Side));
        Assert.DoesNotContain(
            read.Models!.Components.SelectMany(component => component.Bodies),
            body => body.BodyProjection != ComponentBodyProjection.Top);
    }

    [Fact]
    public void PackageVerifier_RejectsReadbackContainingBottomComponent()
    {
        using var temp = new TestTempDirectory();
        var source = Snapshot(new[] { Top1, Bottom1 });
        var written = ExchangePackageWriter.Write(
            temp.Path,
            source.Snapshot,
            WriteOptions(),
            Model(source).Request);

        Assert.True(written.IsSuccess, string.Join("; ", written.Errors.Select(item => item.Message)));
        var read = ExchangePackageReader.Read(written.PackageDirectory!);
        var expected = TopOnlySnapshotBuilder.Filter(source, new HashSet<string> { Top1.Id }).Snapshot;

        Assert.True(read.IsSuccess, string.Join("; ", read.Errors.Select(item => item.Message)));
        var error = TopOnlyPackageVerifier.Verify(read, expected);

        Assert.NotNull(error);
        Assert.Contains("differ", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandRequestId_IsNewForEachInvocationAndExplicitIdIsPreserved()
    {
        var first = ExportBoardSnapshotHandler.ResolveWorkflowRequestId(string.Empty);
        var second = ExportBoardSnapshotHandler.ResolveWorkflowRequestId(string.Empty);
        const string explicitId = "11111111-2222-3333-4444-555555555555";

        Assert.NotEqual(first, second);
        Assert.False(first.StartsWith("doc-", StringComparison.Ordinal));
        Assert.Equal(explicitId, ExportBoardSnapshotHandler.ResolveWorkflowRequestId(explicitId));
    }

    private static PackageWriteOptions WriteOptions() => new()
    {
        PackageId = Guid.NewGuid(),
        CreatedAtUtc = DateTimeOffset.UtcNow,
        Producer = new PackageProducer
        {
            Component = "EcadMcadBridge.Altium.Tests",
            Version = "0.4.0",
        },
        Source = new PackageSource
        {
            Cad = "altium",
            DocumentSha256 = new string('a', 64),
        },
        Capabilities = Array.Empty<string>(),
    };

    private static TopOnlySelectionCandidate Candidate(
        ModelTestSnapshot.ComponentSeed seed,
        bool ownershipAvailable = true,
        bool bodyInventoryReadable = true,
        bool bodyTopOnly = true,
        bool modelAvailable = true) =>
        new(
            seed.Designator,
            new AltiumComponentKey(
                null,
                seed.SourceUniqueId,
                seed.Designator,
                seed.XIu,
                seed.YIu,
                seed.RotationDeg,
                seed.Side),
            ownershipAvailable,
            bodyInventoryReadable,
            bodyTopOnly,
            modelAvailable);

    private static void AssertTopOnly(ExportPackageWriteRequest request, params string[] expectedIds)
    {
        var expected = expectedIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, request.Snapshot.Snapshot.Components
            .Select(component => component.Id)
            .OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(expected, request.Model.Request.Components
            .Select(component => component.ComponentId)
            .OrderBy(id => id, StringComparer.Ordinal));
        Assert.DoesNotContain(request.Snapshot.Snapshot.Components, component =>
            component.Placement.Side != BoardSide.Top);
        Assert.DoesNotContain(request.Model.Request.Components.SelectMany(component => component.Bodies), body =>
            body.BodyProjection != ComponentBodyProjection.Top);
    }

    private static ExportRequestPipeline CreatePipeline(
        IReadOnlyList<TopOnlySelectionCandidate> candidates,
        IReadOnlyList<ModelTestSnapshot.ComponentSeed> seeds,
        BoundaryCounts counts,
        LogSink? sink = null,
        Action? cleanupAction = null,
        Action<ExportPackageWriteRequest, ExportInvocation>? writerCallback = null,
        Func<ExportPackageWriteRequest, ExportInvocation, ExportInvocationResult>? writerResult = null)
    {
        sink ??= new LogSink();
        var pipeline = new ExportRequestPipeline(
            new ExportRequestCoordinator(sink.Write),
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
                return Snapshot(seeds);
            },
            (snapshot, _) =>
            {
                counts.Model++;
                return Model(snapshot);
            },
            (_, _) =>
            {
                counts.Board++;
                return Board();
            },
            (request, invocation) =>
            {
                counts.Writer++;
                writerCallback?.Invoke(request, invocation);
                var result = writerResult?.Invoke(request, invocation)
                    ?? new ExportInvocationResult(ExportRequestStatus.Completed);
                if (result.Status == ExportRequestStatus.Completed)
                {
                    var logError = SecondaryFailureGuard.TryWrite(
                        sink.Write,
                        "INFO",
                        "PACKAGE_EXPORT_OK",
                        "packageId=" + invocation.InvocationId);
                    result = result with
                    {
                        SecondaryErrors = SecondaryFailureGuard.WithError(result.SecondaryErrors, logError),
                    };
                }

                return result;
            },
            Array.Empty<string>(),
            sink.Write,
            snapshot => TopOnlySelectionFilter.Apply(snapshot, candidates));
        return pipeline;
    }

    private static AltiumBoardSnapshotResult Snapshot(IReadOnlyList<ModelTestSnapshot.ComponentSeed> seeds)
    {
        var identities = seeds
            .Select(seed => new AltiumComponentIdentity(
                seed.Id,
                new AltiumComponentKey(
                    null,
                    seed.SourceUniqueId,
                    seed.Designator,
                    seed.XIu,
                    seed.YIu,
                    seed.RotationDeg,
                    seed.Side)))
            .ToArray();
        return new AltiumBoardSnapshotResult(
            ModelTestSnapshot.Create(seeds.ToArray()),
            null!,
            identities,
            Array.Empty<string>(),
            Array.Empty<AltiumRegionInfo>(),
            new AltiumAdapterDiagnostics());
    }

    private static AltiumComponentModelResult Model(AltiumBoardSnapshotResult snapshot)
    {
        var components = snapshot.Snapshot.Components
            .Select(component => new ComponentModelWriteEntry
            {
                ComponentId = component.Id,
                Designator = component.Designator,
                Bodies = new[]
                {
                    new ComponentBodyWriteEntry
                    {
                        BodyOrdinal = 0,
                        BodyProjection = component.Placement.Side == BoardSide.Top
                            ? ComponentBodyProjection.Top
                            : ComponentBodyProjection.Bottom,
                    },
                },
            })
            .ToArray();
        return new AltiumComponentModelResult(
            new ComponentModelsWriteRequest { Components = components },
            Array.Empty<string>(),
            new AltiumComponentModelDiagnostics());
    }

    private static AltiumBoardModelExportResult Board() => new(
        StepPath: null,
        Sha256: null,
        Size: 0,
        Warnings: Array.Empty<string>(),
        Error: null);

    private sealed class LogSink(params string[] throwOnCodes)
    {
        private readonly object _sync = new();
        private readonly HashSet<string> _throwOnCodes = new(throwOnCodes, StringComparer.Ordinal);
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

        public void Write(string level, string code, string message)
        {
            if (_throwOnCodes.Contains(code))
                throw new IOException("logging failed: " + code);

            lock (_sync)
            {
                _counts.TryGetValue(code, out var count);
                _counts[code] = count + 1;
            }
        }

        public int Count(string code)
        {
            lock (_sync)
                return _counts.GetValueOrDefault(code);
        }
    }

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
