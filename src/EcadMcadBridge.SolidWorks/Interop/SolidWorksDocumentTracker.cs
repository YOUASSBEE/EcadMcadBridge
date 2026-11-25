using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace EcadMcadBridge.SolidWorks.Interop;

public sealed record SolidWorksDocumentLifecycle(
    int BaselineDocuments,
    int CreatedDocuments,
    int ClosedDocuments,
    int RemainingCreatedDocuments,
    IReadOnlyList<string> Warnings,
    SolidWorksCleanupStatus CleanupStatus = SolidWorksCleanupStatus.Incomplete,
    SolidWorksDocumentSnapshotStatus BaselineSnapshotStatus = SolidWorksDocumentSnapshotStatus.Incomplete,
    SolidWorksDocumentSnapshotStatus? FinalSnapshotStatus = null,
    int FinalDocuments = 0,
    string? SnapshotMessage = null);

/// <summary>
/// Captures the user's open documents before Bridge work and closes only documents
/// positively tracked as belonging to this Bridge operation. Titles and paths are
/// re-read at close time because SaveAs changes them.
/// </summary>
public sealed class SolidWorksDocumentTracker
{
    private readonly ISldWorks _solidWorks;
    private readonly Dictionary<IntPtr, DocumentIdentity> _owned = new();
    private readonly HashSet<IntPtr> _protectedIdentities = new();
    private readonly HashSet<string> _protectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _warnings = new();
    private readonly HashSet<string> _reportedUnknown = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DocumentIdentity> _baseline;
    private readonly HashSet<IntPtr> _baselineIdentities = new();
    private readonly HashSet<string> _baselinePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _baselineMessage;
    private int _createdCount;
    private int _closedCount;
    private bool _comUnavailable;
    private SolidWorksCleanupStatus _cleanupStatus = SolidWorksCleanupStatus.Incomplete;
    private SolidWorksDocumentSnapshotStatus? _finalStatus;
    private int _finalCount;
    private string? _snapshotMessage;

    private SolidWorksDocumentTracker(
        ISldWorks solidWorks,
        CapturedDocumentSet baseline)
    {
        _solidWorks = solidWorks;
        BaselineStatus = baseline.Status;
        _baselineMessage = baseline.Message;
        _baseline = baseline.Documents.ToList();
        foreach (var identity in _baseline)
        {
            _baselineIdentities.Add(identity.Identity);
            if (identity.Path is not null)
                _baselinePaths.Add(identity.Path);
        }

        if (baseline.Status != SolidWorksDocumentSnapshotStatus.Available)
        {
            _warnings.Add(
                "Open document baseline is " + baseline.Status +
                "; Bridge will not close documents automatically" +
                (string.IsNullOrWhiteSpace(baseline.Message) ? "." : ": " + baseline.Message));
        }
    }

    public SolidWorksDocumentSnapshotStatus BaselineStatus { get; }

    public bool BaselineAvailable => BaselineStatus == SolidWorksDocumentSnapshotStatus.Available;

    public int BaselineCount => _baseline.Count;

    public int CreatedCount => _createdCount;

    public int ClosedCount => _closedCount;

    public int RemainingCreatedCount => _owned.Count;

    public SolidWorksCleanupStatus CleanupStatus => _cleanupStatus;

    public IReadOnlyList<string> Warnings => _warnings;

    public static SolidWorksDocumentTracker Capture(ISldWorks solidWorks)
    {
        ArgumentNullException.ThrowIfNull(solidWorks);
        return new SolidWorksDocumentTracker(solidWorks, ReadDocuments(solidWorks));
    }

    /// <summary>
    /// Registers a document that the current Bridge operation positively opened or created.
    /// Callers must supply a document returned by LoadFile4, OpenDoc6, NewDocument, or a
    /// component obtained from the STEP assembly.
    /// </summary>
    public bool Track(ModelDoc2? document, int loadWarnings = 0)
    {
        if (document is null)
            return false;

        if (!BaselineAvailable)
        {
            _warnings.Add(
                "A Bridge document could not be tracked because the baseline is " +
                BaselineStatus + "; it will not be closed automatically.");
            return false;
        }

        DocumentIdentity identity;
        try
        {
            identity = Capture(document);
        }
        catch (Exception ex)
        {
            _warnings.Add(
                "A created document could not be identified; it will not be closed automatically: " +
                DescribeFailure("Capture", ex));
            if (SolidWorksComFailure.IsUnavailable(ex))
                _comUnavailable = true;
            return false;
        }

        if ((loadWarnings & (int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen) != 0
            || IsBaseline(identity))
        {
            Protect(identity);
            return false;
        }

        if (!_owned.ContainsKey(identity.Identity))
            _createdCount++;
        _owned[identity.Identity] = identity;
        return true;
    }

    /// <summary>
    /// Closes only documents positively tracked as Bridge-owned. Any post-baseline document
    /// whose ownership cannot be established is left open and recorded as a warning.
    /// </summary>
    public IReadOnlyList<string> CloseCreated(ModelDoc2? preferredFirst = null)
    {
        var warnings = new List<string>();
        if (!BaselineAvailable)
        {
            warnings.Add(
                "Document baseline is " + BaselineStatus + "; no Bridge documents were closed.");
            _cleanupStatus = BaselineStatus == SolidWorksDocumentSnapshotStatus.Unavailable
                ? SolidWorksCleanupStatus.Unavailable
                : SolidWorksCleanupStatus.Incomplete;
            _snapshotMessage = _baselineMessage;
            _warnings.AddRange(warnings);
            return warnings;
        }

        var current = ReadCurrentDocuments();
        if (!SolidWorksCleanupPolicy.CanStartCleanup(current.Status))
            return StopCleanup(current, warnings);

        var unknownDocuments = current.Documents
            .Where(document =>
                !IsBaseline(document)
                && !IsProtected(document)
                && FindOwned(document) is null)
            .ToArray();
        foreach (var unknownDocument in unknownDocuments)
            ReportUnknown(unknownDocument, warnings);

        CapturedDocumentSet? terminalSnapshot = null;
        SolidWorksCleanupStatus? terminalStatus = null;
        if (unknownDocuments.Length > 0)
        {
            // Closing an assembly can unload/close its component documents. Fail closed:
            // while any post-baseline document lacks proven ownership, close nothing.
            warnings.Add(
                "Document cleanup did not close Bridge-owned documents because " +
                unknownDocuments.Length.ToString(CultureInfo.InvariantCulture) +
                " post-baseline document(s) could not be identified.");
        }
        else
        {
            // Close the operation's root document first; STEP import helper documents stay open
            // while their root document is loaded. Then close children before parent assemblies.
            var preferredIdentity = IntPtr.Zero;
            if (preferredFirst is not null)
            {
                try
                {
                    preferredIdentity = IdentityOf(preferredFirst);
                }
                catch
                {
                    preferredIdentity = IntPtr.Zero;
                }
            }

            foreach (var document in current.Documents
                .OrderBy(item => item.Identity == preferredIdentity ? 0 : 1)
                .ThenBy(item => item.DocType)
                .ToArray())
            {
                if (IsBaseline(document) || IsProtected(document))
                    continue;

                var owned = FindOwned(document);
                if (owned is null)
                    continue;

                if (!SolidWorksDocumentOwnershipPolicy.CanClose(
                        isBaseline: IsBaseline(document),
                        isProtected: IsProtected(document),
                        isBridgeOwned: true))
                {
                    continue;
                }

                var outcome = TryCloseIdentity(document, warnings, out var verification);
                if (outcome == SolidWorksDocumentCheckResult.Closed)
                {
                    _closedCount++;
                    _owned.Remove(owned.Identity);
                    continue;
                }

                if (!SolidWorksCleanupPolicy.CanContinueWithNextDocument(outcome))
                {
                    terminalSnapshot = verification;
                    terminalStatus = outcome == SolidWorksDocumentCheckResult.Incomplete
                        ? SolidWorksCleanupStatus.Incomplete
                        : SolidWorksCleanupStatus.Unavailable;
                    if (outcome == SolidWorksDocumentCheckResult.Unavailable)
                        _comUnavailable = true;
                    break;
                }
            }
        }

        var final = terminalSnapshot
            ?? (_comUnavailable ? UnavailableSnapshot() : ReadCurrentDocuments());

        if (final.Status == SolidWorksDocumentSnapshotStatus.Unavailable)
            _comUnavailable = true;

        if (final.Status == SolidWorksDocumentSnapshotStatus.Available)
        {
            foreach (var owned in _owned.Values.ToArray())
            {
                if (!final.Documents.Any(document => SameDocument(document, owned)))
                    _owned.Remove(owned.Identity);
            }
        }

        var missingBaseline = final.Status == SolidWorksDocumentSnapshotStatus.Available
            ? _baseline.Count(item => !final.Documents.Any(document => SameDocument(item, document)))
            : 0;
        var ownedOpen = final.Status == SolidWorksDocumentSnapshotStatus.Available
            ? _owned.Values.Count(item => final.Documents.Any(document => SameDocument(item, document)))
            : _owned.Count;
        var unknown = final.Status == SolidWorksDocumentSnapshotStatus.Available
            ? final.Documents.Count(document =>
                !IsBaseline(document)
                && !IsProtected(document)
                && FindOwned(document) is null)
            : 0;

        _finalStatus = final.Status;
        _finalCount = final.Documents.Count;
        _snapshotMessage = final.Message ?? _baselineMessage;
        _cleanupStatus = terminalStatus ?? SolidWorksCleanupEvaluator.Evaluate(
            new SolidWorksCleanupEvidence(
                BaselineStatus,
                final.Status,
                missingBaseline,
                ownedOpen,
                unknown,
                _snapshotMessage));

        if (_cleanupStatus != SolidWorksCleanupStatus.Verified)
        {
            warnings.Add(
                "Document cleanup is " + _cleanupStatus +
                (ownedOpen > 0 ? "; " + ownedOpen.ToString(CultureInfo.InvariantCulture) + " Bridge-owned document(s) remain tracked/unconfirmed" : string.Empty) +
                (unknown > 0 ? "; " + unknown.ToString(CultureInfo.InvariantCulture) + " unidentified post-baseline document(s) remain open" : string.Empty) +
                (missingBaseline > 0 ? "; " + missingBaseline.ToString(CultureInfo.InvariantCulture) + " baseline document(s) are missing" : string.Empty) +
                (string.IsNullOrWhiteSpace(_snapshotMessage) ? "." : ": " + _snapshotMessage));
        }

        _warnings.AddRange(warnings);
        return warnings;
    }

    private IReadOnlyList<string> StopCleanup(
        CapturedDocumentSet snapshot,
        List<string> warnings)
    {
        if (snapshot.Status == SolidWorksDocumentSnapshotStatus.Unavailable)
            _comUnavailable = true;

        _finalStatus = snapshot.Status;
        _finalCount = snapshot.Documents.Count;
        _snapshotMessage = snapshot.Message ?? _baselineMessage;
        _cleanupStatus = snapshot.Status == SolidWorksDocumentSnapshotStatus.Unavailable
            ? SolidWorksCleanupStatus.Unavailable
            : SolidWorksCleanupStatus.Incomplete;
        warnings.Add(
            "Document cleanup is " + _cleanupStatus +
            "; no Bridge documents were closed because the initial snapshot is " +
            snapshot.Status +
            (string.IsNullOrWhiteSpace(snapshot.Message) ? string.Empty : ": " + snapshot.Message) +
            "; " + _owned.Count.ToString(CultureInfo.InvariantCulture) +
            " known Bridge-owned document(s) remain tracked/unconfirmed.");
        _warnings.AddRange(warnings);
        return warnings;
    }

    public SolidWorksDocumentLifecycle Snapshot() =>
        new(
            BaselineCount,
            CreatedCount,
            ClosedCount,
            RemainingCreatedCount,
            _warnings.ToArray(),
            _cleanupStatus,
            BaselineStatus,
            _finalStatus,
            _finalCount,
            _snapshotMessage ?? _baselineMessage);

    private void ReportUnknown(DocumentIdentity document, List<string> warnings)
    {
        var label = string.IsNullOrWhiteSpace(document.Path)
            ? document.Title
            : document.Path;
        var key = document.Identity != IntPtr.Zero
            ? "identity:" + document.Identity.ToString("X", CultureInfo.InvariantCulture)
            : "name:" + label;
        if (!_reportedUnknown.Add(key))
            return;

        warnings.Add(
            "A post-baseline document (" + label +
            ") was not identified as Bridge-owned; it was left open.");
    }

    private SolidWorksDocumentCheckResult TryCloseIdentity(
        DocumentIdentity current,
        List<string> warnings,
        out CapturedDocumentSet? verification)
    {
        verification = null;
        if (string.IsNullOrWhiteSpace(current.Title))
        {
            warnings.Add("A Bridge document has no current title; it was not closed.");
            return SolidWorksDocumentCheckResult.StillOpen;
        }

        var label = Path.GetFileName(current.Path ?? current.Title);
        var lastError = string.Empty;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var outcome = TryCloseDocument(current, out lastError, out verification);
            if (outcome == SolidWorksDocumentCheckResult.Closed)
                return SolidWorksDocumentCheckResult.Closed;
            if (!SolidWorksCleanupPolicy.CanContinueWithNextDocument(outcome))
            {
                warnings.Add(
                    "A Bridge document (" + label + ") close could not be verified: " +
                    outcome + "; " + lastError + ".");
                return outcome;
            }

            if (attempt < 4)
                Thread.Sleep(50);
        }

        warnings.Add("A Bridge document (" + label + ") could not be closed: " + lastError + ".");
        return SolidWorksDocumentCheckResult.StillOpen;
    }

    private DocumentIdentity? FindOwned(DocumentIdentity document) =>
        _owned.Values.FirstOrDefault(item => SameDocument(item, document));

    private bool IsBaseline(DocumentIdentity identity) =>
        _baselineIdentities.Contains(identity.Identity)
        || (identity.Path is not null && _baselinePaths.Contains(identity.Path));

    private bool IsProtected(DocumentIdentity identity) =>
        _protectedIdentities.Contains(identity.Identity)
        || (identity.Path is not null && _protectedPaths.Contains(identity.Path));

    private void Protect(DocumentIdentity identity)
    {
        _protectedIdentities.Add(identity.Identity);
        if (identity.Path is not null)
            _protectedPaths.Add(identity.Path);
    }

    private CapturedDocumentSet ReadCurrentDocuments()
    {
        if (_comUnavailable)
        {
            return new CapturedDocumentSet(
                SolidWorksDocumentSnapshotStatus.Unavailable,
                Array.Empty<DocumentIdentity>(),
                "SOLIDWORKS COM/RPC is unavailable.");
        }

        var current = ReadDocuments(_solidWorks);
        if (current.Status == SolidWorksDocumentSnapshotStatus.Unavailable)
            _comUnavailable = true;
        return current;
    }

    private SolidWorksDocumentCheckResult TryCloseDocument(
        DocumentIdentity identity,
        out string lastError,
        out CapturedDocumentSet? verification)
    {
        lastError = string.Empty;
        verification = null;
        var candidates = new[] { identity.Path, identity.Title }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var candidate in candidates)
        {
            try
            {
                var activateErrors = 0;
                if (_solidWorks.ActivateDoc3(candidate!, false, 0, ref activateErrors) is not null
                    && activateErrors == 0)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                if (SolidWorksComFailure.IsUnavailable(ex))
                {
                    AppendError(ref lastError, DescribeCloseFailure("ActivateDoc3", candidate!, ex));
                    return SolidWorksDocumentCheckResult.Unavailable;
                }
                // Closing can still work without activation.
            }
        }

        foreach (var candidate in candidates)
        {
            try
            {
                _solidWorks.CloseDoc(candidate!);
                var result = CheckClosed(identity, out verification);
                if (result == SolidWorksDocumentCheckResult.Closed)
                    return SolidWorksDocumentCheckResult.Closed;
                if (!SolidWorksCleanupPolicy.CanFallbackToQuit(result))
                {
                    AppendError(
                        ref lastError,
                        "CloseDoc verification is " + result + "; QuitDoc fallback was not attempted.");
                    return result;
                }
                AppendError(ref lastError, "CloseDoc returned but document is still open.");
            }
            catch (Exception ex)
            {
                AppendError(ref lastError, DescribeCloseFailure("CloseDoc", candidate!, ex));
                if (SolidWorksComFailure.IsUnavailable(ex))
                    return SolidWorksDocumentCheckResult.Unavailable;
            }
        }

        foreach (var candidate in candidates)
        {
            try
            {
                _solidWorks.QuitDoc(candidate!);
                var result = CheckClosed(identity, out verification);
                if (result == SolidWorksDocumentCheckResult.Closed)
                    return SolidWorksDocumentCheckResult.Closed;
                if (!SolidWorksCleanupPolicy.CanFallbackToQuit(result))
                {
                    AppendError(
                        ref lastError,
                        "QuitDoc verification is " + result + ".");
                    return result;
                }
                AppendError(ref lastError, "QuitDoc returned but document is still open.");
            }
            catch (Exception ex)
            {
                AppendError(ref lastError, DescribeCloseFailure("QuitDoc", candidate!, ex));
                if (SolidWorksComFailure.IsUnavailable(ex))
                    return SolidWorksDocumentCheckResult.Unavailable;
            }
        }

        return SolidWorksDocumentCheckResult.StillOpen;
    }

    private SolidWorksDocumentCheckResult CheckClosed(
        DocumentIdentity identity,
        out CapturedDocumentSet snapshot)
    {
        snapshot = ReadCurrentDocuments();
        if (snapshot.Status == SolidWorksDocumentSnapshotStatus.Unavailable)
            return SolidWorksDocumentCheckResult.Unavailable;
        if (snapshot.Status == SolidWorksDocumentSnapshotStatus.Incomplete)
            return SolidWorksDocumentCheckResult.Incomplete;

        return snapshot.Documents.Any(item => SameDocument(item, identity))
            ? SolidWorksDocumentCheckResult.StillOpen
            : SolidWorksDocumentCheckResult.Closed;
    }

    private static CapturedDocumentSet ReadDocuments(ISldWorks solidWorks)
    {
        object? raw;
        try
        {
            raw = solidWorks.GetDocuments();
        }
        catch (Exception ex)
        {
            return new CapturedDocumentSet(
                SolidWorksDocumentSnapshotStatus.Unavailable,
                Array.Empty<DocumentIdentity>(),
                DescribeFailure("GetDocuments", ex));
        }

        ModelDoc2[] documents;
        var unexpectedEntry = false;
        switch (raw)
        {
            case null:
                documents = Array.Empty<ModelDoc2>();
                break;
            case object[] array:
                documents = array.OfType<ModelDoc2>().ToArray();
                unexpectedEntry = documents.Length != array.Length;
                break;
            case ModelDoc2 single:
                documents = new[] { single };
                break;
            default:
                return new CapturedDocumentSet(
                    SolidWorksDocumentSnapshotStatus.Incomplete,
                    Array.Empty<DocumentIdentity>(),
                    "GetDocuments returned an unsupported value.");
        }

        var result = new List<DocumentIdentity>();
        var incomplete = unexpectedEntry;
        string? message = unexpectedEntry
            ? "GetDocuments returned at least one unsupported entry."
            : null;
        foreach (var document in documents)
        {
            try
            {
                result.Add(Capture(document));
            }
            catch (Exception ex)
            {
                if (SolidWorksComFailure.IsUnavailable(ex))
                {
                    return new CapturedDocumentSet(
                        SolidWorksDocumentSnapshotStatus.Unavailable,
                        result,
                        DescribeFailure("GetDocuments identity", ex));
                }

                incomplete = true;
                message = DescribeFailure("GetDocuments identity", ex);
            }
        }

        return incomplete
            ? new CapturedDocumentSet(
                SolidWorksDocumentSnapshotStatus.Incomplete,
                result,
                message ?? "At least one open document could not be identified.")
            : new CapturedDocumentSet(
                SolidWorksDocumentSnapshotStatus.Available,
                result,
                null);
    }

    private static DocumentIdentity Capture(ModelDoc2 document)
    {
        var identity = IdentityOf(document);
        if (identity == IntPtr.Zero)
            throw new InvalidOperationException("The document has no COM identity.");

        return new DocumentIdentity(
            document,
            identity,
            SolidWorksPath.Normalize(document.GetPathName()),
            document.GetTitle() ?? string.Empty,
            document.GetType());
    }

    private static IntPtr IdentityOf(ModelDoc2 document)
    {
        var identity = Marshal.GetIUnknownForObject(document);
        if (identity != IntPtr.Zero)
            Marshal.Release(identity);
        return identity;
    }

    private static bool SameDocument(DocumentIdentity left, DocumentIdentity right) =>
        left.Identity == right.Identity
        || (left.Path is not null
            && right.Path is not null
            && SolidWorksPath.Same(left.Path, right.Path));

    private static string DescribeFailure(string operation, Exception ex) =>
        operation + " failed " + ex.GetType().Name + " 0x" +
        ex.HResult.ToString("X8", CultureInfo.InvariantCulture) + " " + ex.Message;

    private static string DescribeCloseFailure(string operation, string candidate, Exception ex) =>
        operation + "(" + Path.GetFileName(candidate) + ") failed " +
        ex.GetType().Name + " 0x" +
        ex.HResult.ToString("X8", CultureInfo.InvariantCulture) + " " + ex.Message;

    private static void AppendError(ref string lastError, string message) =>
        lastError = string.IsNullOrWhiteSpace(lastError) ? message : lastError + "; " + message;

    private static CapturedDocumentSet UnavailableSnapshot() =>
        new(
            SolidWorksDocumentSnapshotStatus.Unavailable,
            Array.Empty<DocumentIdentity>(),
            "SOLIDWORKS COM/RPC is unavailable.");

    private sealed record CapturedDocumentSet(
        SolidWorksDocumentSnapshotStatus Status,
        IReadOnlyList<DocumentIdentity> Documents,
        string? Message);

    private sealed record DocumentIdentity(
        ModelDoc2 Document,
        IntPtr Identity,
        string? Path,
        string Title,
        int DocType);
}
