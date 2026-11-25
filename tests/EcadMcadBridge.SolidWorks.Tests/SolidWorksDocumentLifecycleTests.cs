using System.Runtime.InteropServices;
using EcadMcadBridge.SolidWorks.Interop;
using Xunit;

namespace EcadMcadBridge.SolidWorks.Tests;

public sealed class SolidWorksDocumentLifecycleTests
{
    [Fact]
    public void VerifiedWhenBaselineAndFinalSnapshotsAreAvailableAndClean()
    {
        var status = SolidWorksCleanupEvaluator.Evaluate(new SolidWorksCleanupEvidence(
            SolidWorksDocumentSnapshotStatus.Available,
            SolidWorksDocumentSnapshotStatus.Available,
            MissingBaselineCount: 0,
            BridgeOwnedOpenCount: 0,
            UnknownPostBaselineCount: 0));

        Assert.Equal(SolidWorksCleanupStatus.Verified, status);
    }

    [Fact]
    public void FinalSnapshotUnavailableIsNotAnEmptyList()
    {
        var snapshot = SolidWorksDocumentSnapshot.Unavailable("RPC server unavailable.");

        Assert.Equal(SolidWorksDocumentSnapshotStatus.Unavailable, snapshot.Status);
        Assert.False(snapshot.IsAvailable);
        Assert.NotEmpty(snapshot.Message!);
        Assert.Equal(
            SolidWorksCleanupStatus.Unavailable,
            SolidWorksCleanupEvaluator.Evaluate(new SolidWorksCleanupEvidence(
                SolidWorksDocumentSnapshotStatus.Available,
                snapshot.Status,
                0,
                0,
                0)));
    }

    [Fact]
    public void IncompleteSnapshotCannotBeVerifiedCleanup()
    {
        var snapshot = SolidWorksDocumentSnapshot.Incomplete(
            new[] { "title:known" },
            "One document identity could not be read.");

        Assert.Equal(SolidWorksDocumentSnapshotStatus.Incomplete, snapshot.Status);
        Assert.Equal(
            SolidWorksCleanupStatus.Incomplete,
            SolidWorksCleanupEvaluator.Evaluate(new SolidWorksCleanupEvidence(
                SolidWorksDocumentSnapshotStatus.Available,
                snapshot.Status,
                0,
                0,
                0)));
    }

    [Fact]
    public void KnownBridgeOwnedDocumentLeftOpenIsFailure()
    {
        var status = SolidWorksCleanupEvaluator.Evaluate(new SolidWorksCleanupEvidence(
            SolidWorksDocumentSnapshotStatus.Available,
            SolidWorksDocumentSnapshotStatus.Available,
            0,
            BridgeOwnedOpenCount: 1,
            0));

        Assert.Equal(SolidWorksCleanupStatus.Failed, status);
    }

    [Fact]
    public void UnknownPostBaselineDocumentIsNotClosedAutomatically()
    {
        Assert.False(SolidWorksDocumentOwnershipPolicy.CanClose(
            isBaseline: false,
            isProtected: false,
            isBridgeOwned: false));

        Assert.Equal(
            SolidWorksCleanupStatus.Incomplete,
            SolidWorksCleanupEvaluator.Evaluate(new SolidWorksCleanupEvidence(
                SolidWorksDocumentSnapshotStatus.Available,
                SolidWorksDocumentSnapshotStatus.Available,
                0,
                0,
                UnknownPostBaselineCount: 1)));
    }

    [Fact]
    public void BaselineDocumentIsNeverClosed()
    {
        Assert.False(SolidWorksDocumentOwnershipPolicy.CanClose(
            isBaseline: true,
            isProtected: false,
            isBridgeOwned: true));
    }

    [Fact]
    public void AlreadyOpenOrProtectedDocumentIsNeverClosed()
    {
        Assert.False(SolidWorksDocumentOwnershipPolicy.CanClose(
            isBaseline: false,
            isProtected: true,
            isBridgeOwned: true));
    }

    [Fact]
    public void AvailableInitialSnapshotAllowsCleanup()
    {
        Assert.True(SolidWorksCleanupPolicy.CanStartCleanup(
            SolidWorksDocumentSnapshotStatus.Available));
    }

    [Fact]
    public void IncompleteInitialSnapshotForbidsCleanup()
    {
        Assert.False(SolidWorksCleanupPolicy.CanStartCleanup(
            SolidWorksDocumentSnapshotStatus.Incomplete));
    }

    [Fact]
    public void UnavailableInitialSnapshotForbidsCleanup()
    {
        Assert.False(SolidWorksCleanupPolicy.CanStartCleanup(
            SolidWorksDocumentSnapshotStatus.Unavailable));
    }

    [Fact]
    public void IncompleteCloseDocVerificationForbidsQuitFallback()
    {
        Assert.False(SolidWorksCleanupPolicy.CanFallbackToQuit(
            SolidWorksDocumentCheckResult.Incomplete));
        Assert.False(SolidWorksCleanupPolicy.CanContinueWithNextDocument(
            SolidWorksDocumentCheckResult.Incomplete));
    }

    [Fact]
    public void UnavailableCloseDocVerificationForbidsQuitFallback()
    {
        Assert.False(SolidWorksCleanupPolicy.CanFallbackToQuit(
            SolidWorksDocumentCheckResult.Unavailable));
        Assert.False(SolidWorksCleanupPolicy.CanContinueWithNextDocument(
            SolidWorksDocumentCheckResult.Unavailable));
    }

    [Fact]
    public void UnknownCloseResultStopsRemainingDocumentCleanup()
    {
        Assert.False(SolidWorksCleanupPolicy.CanContinueWithNextDocument(
            SolidWorksDocumentCheckResult.Incomplete));
        Assert.False(SolidWorksCleanupPolicy.CanContinueWithNextDocument(
            SolidWorksDocumentCheckResult.Unavailable));
        Assert.True(SolidWorksCleanupPolicy.CanContinueWithNextDocument(
            SolidWorksDocumentCheckResult.Closed));
    }

    [Fact]
    public void UnknownSnapshotCannotBecomeVerifiedWithKnownOwnedDocuments()
    {
        var status = SolidWorksCleanupEvaluator.Evaluate(new SolidWorksCleanupEvidence(
            SolidWorksDocumentSnapshotStatus.Available,
            SolidWorksDocumentSnapshotStatus.Incomplete,
            MissingBaselineCount: 0,
            BridgeOwnedOpenCount: 2,
            UnknownPostBaselineCount: 1));

        Assert.Equal(SolidWorksCleanupStatus.Incomplete, status);
        Assert.NotEqual(SolidWorksCleanupStatus.Verified, status);
    }

    [Fact]
    public void CloseDocExceptionWithClosedSnapshotIsTreatedAsClosed()
    {
        var exception = new InvalidOperationException("CloseDoc failed.");

        Assert.True(SolidWorksCloseExceptionPolicy.RequiresStateCheck(exception));
        Assert.Equal(
            SolidWorksCloseExceptionAction.Closed,
            SolidWorksCloseExceptionPolicy.Decide(SolidWorksDocumentCheckResult.Closed));
    }

    [Fact]
    public void CloseDocExceptionWithStillOpenSnapshotAllowsQuitFallback()
    {
        Assert.Equal(
            SolidWorksCloseExceptionAction.RetryOrFallback,
            SolidWorksCloseExceptionPolicy.Decide(SolidWorksDocumentCheckResult.StillOpen));
        Assert.True(SolidWorksCleanupPolicy.CanFallbackToQuit(
            SolidWorksDocumentCheckResult.StillOpen));
    }

    [Fact]
    public void CloseDocExceptionWithIncompleteSnapshotStopsQuitFallback()
    {
        Assert.Equal(
            SolidWorksCloseExceptionAction.Stop,
            SolidWorksCloseExceptionPolicy.Decide(SolidWorksDocumentCheckResult.Incomplete));
        Assert.False(SolidWorksCleanupPolicy.CanFallbackToQuit(
            SolidWorksDocumentCheckResult.Incomplete));
    }

    [Fact]
    public void CloseDocExceptionWithUnavailableSnapshotStopsQuitFallback()
    {
        Assert.False(SolidWorksCloseExceptionPolicy.RequiresStateCheck(
            new COMException("RPC unavailable.", unchecked((int)0x800706BA))));
        Assert.Equal(
            SolidWorksCloseExceptionAction.Stop,
            SolidWorksCloseExceptionPolicy.Decide(SolidWorksDocumentCheckResult.Unavailable));
        Assert.False(SolidWorksCleanupPolicy.CanFallbackToQuit(
            SolidWorksDocumentCheckResult.Unavailable));
    }

    [Fact]
    public void QuitDocExceptionWithUnknownSnapshotStopsFollowingDestructiveCalls()
    {
        Assert.Equal(
            SolidWorksCloseExceptionAction.Stop,
            SolidWorksCloseExceptionPolicy.Decide(SolidWorksDocumentCheckResult.Incomplete));
        Assert.Equal(
            SolidWorksCloseExceptionAction.Stop,
            SolidWorksCloseExceptionPolicy.Decide(SolidWorksDocumentCheckResult.Unavailable));
        Assert.False(SolidWorksCleanupPolicy.CanContinueWithNextDocument(
            SolidWorksDocumentCheckResult.Incomplete));
        Assert.False(SolidWorksCleanupPolicy.CanContinueWithNextDocument(
            SolidWorksDocumentCheckResult.Unavailable));
    }

    [Fact]
    public void IndeterminateCloseStateKeepsOwnedDocumentsUnverified()
    {
        var status = SolidWorksCleanupEvaluator.Evaluate(new SolidWorksCleanupEvidence(
            SolidWorksDocumentSnapshotStatus.Available,
            SolidWorksDocumentSnapshotStatus.Incomplete,
            MissingBaselineCount: 0,
            BridgeOwnedOpenCount: 2,
            UnknownPostBaselineCount: 0));

        Assert.Equal(SolidWorksCleanupStatus.Incomplete, status);
        Assert.NotEqual(SolidWorksCleanupStatus.Verified, status);
    }
}
