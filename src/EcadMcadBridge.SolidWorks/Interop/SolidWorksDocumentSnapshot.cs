namespace EcadMcadBridge.SolidWorks.Interop;

public enum SolidWorksDocumentSnapshotStatus
{
    Available,
    Unavailable,
    Incomplete,
}

/// <summary>
/// Result of enumerating SOLIDWORKS documents. An empty identity list is meaningful only
/// when <see cref="Status"/> is <see cref="SolidWorksDocumentSnapshotStatus.Available"/>.
/// </summary>
public sealed record SolidWorksDocumentSnapshot(
    SolidWorksDocumentSnapshotStatus Status,
    IReadOnlyList<string> Identities,
    string? Message)
{
    public int Count => Identities.Count;

    public bool IsAvailable => Status == SolidWorksDocumentSnapshotStatus.Available;

    public static SolidWorksDocumentSnapshot Available(IReadOnlyList<string> identities) =>
        new(SolidWorksDocumentSnapshotStatus.Available, identities, null);

    public static SolidWorksDocumentSnapshot Unavailable(string message) =>
        new(SolidWorksDocumentSnapshotStatus.Unavailable, Array.Empty<string>(), message);

    public static SolidWorksDocumentSnapshot Incomplete(
        IReadOnlyList<string> identities,
        string message) =>
        new(SolidWorksDocumentSnapshotStatus.Incomplete, identities, message);
}

public enum SolidWorksCleanupStatus
{
    Verified,
    Incomplete,
    Failed,
    Unavailable,
}

public sealed record SolidWorksCleanupEvidence(
    SolidWorksDocumentSnapshotStatus BaselineStatus,
    SolidWorksDocumentSnapshotStatus FinalStatus,
    int MissingBaselineCount,
    int BridgeOwnedOpenCount,
    int UnknownPostBaselineCount,
    string? Diagnostic = null);

/// <summary>
/// Pure cleanup evaluation. Only a complete baseline and final snapshot with no missing
/// baseline, bridge-owned, or unidentified post-baseline documents can be Verified.
/// </summary>
public static class SolidWorksCleanupEvaluator
{
    public static SolidWorksCleanupStatus Evaluate(SolidWorksCleanupEvidence evidence)
    {
        if (evidence.BaselineStatus == SolidWorksDocumentSnapshotStatus.Unavailable
            || evidence.FinalStatus == SolidWorksDocumentSnapshotStatus.Unavailable)
        {
            return SolidWorksCleanupStatus.Unavailable;
        }

        if (evidence.BaselineStatus == SolidWorksDocumentSnapshotStatus.Incomplete
            || evidence.FinalStatus == SolidWorksDocumentSnapshotStatus.Incomplete)
        {
            return SolidWorksCleanupStatus.Incomplete;
        }

        if (evidence.MissingBaselineCount > 0 || evidence.BridgeOwnedOpenCount > 0)
            return SolidWorksCleanupStatus.Failed;

        if (evidence.UnknownPostBaselineCount > 0)
            return SolidWorksCleanupStatus.Incomplete;

        return SolidWorksCleanupStatus.Verified;
    }
}

public enum SolidWorksDocumentCheckResult
{
    Closed,
    StillOpen,
    Incomplete,
    Unavailable,
}

public static class SolidWorksDocumentOwnershipPolicy
{
    /// <summary>
    /// A document may be closed only when it is not baseline, not AlreadyOpen/protected,
    /// and positively tracked as belonging to the current Bridge operation.
    /// </summary>
    public static bool CanClose(bool isBaseline, bool isProtected, bool isBridgeOwned) =>
        !isBaseline && !isProtected && isBridgeOwned;
}

public static class SolidWorksCleanupPolicy
{
    public static bool CanStartCleanup(SolidWorksDocumentSnapshotStatus snapshotStatus) =>
        snapshotStatus == SolidWorksDocumentSnapshotStatus.Available;

    public static bool CanFallbackToQuit(SolidWorksDocumentCheckResult closeResult) =>
        closeResult == SolidWorksDocumentCheckResult.StillOpen;

    public static bool CanContinueWithNextDocument(SolidWorksDocumentCheckResult closeResult) =>
        closeResult is SolidWorksDocumentCheckResult.Closed
            or SolidWorksDocumentCheckResult.StillOpen;
}

public enum SolidWorksCloseExceptionAction
{
    Closed,
    RetryOrFallback,
    Stop,
}

public static class SolidWorksCloseExceptionPolicy
{
    /// <summary>
    /// RPC/COM-unavailable exceptions already prove the server is gone; do not call
    /// GetDocuments again. Any other exception still requires a state check.
    /// </summary>
    public static bool RequiresStateCheck(Exception exception) =>
        !SolidWorksComFailure.IsUnavailable(exception);

    public static SolidWorksCloseExceptionAction Decide(
        SolidWorksDocumentCheckResult checkedState) =>
        checkedState switch
        {
            SolidWorksDocumentCheckResult.Closed => SolidWorksCloseExceptionAction.Closed,
            SolidWorksDocumentCheckResult.StillOpen => SolidWorksCloseExceptionAction.RetryOrFallback,
            _ => SolidWorksCloseExceptionAction.Stop,
        };
}

public static class SolidWorksComFailure
{
    public static bool IsUnavailable(Exception exception) =>
        exception.HResult == unchecked((int)0x80010108)
        || exception.HResult == unchecked((int)0x800706BA)
        || exception.HResult == unchecked((int)0x800706BE)
        || exception.HResult == unchecked((int)0x80010007);
}
