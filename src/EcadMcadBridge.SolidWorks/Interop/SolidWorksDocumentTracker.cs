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
    IReadOnlyList<string> Warnings);

/// <summary>
/// Captures the user's open documents before Bridge work and closes only documents that
/// appeared afterwards. Titles and paths are re-read at close time because SaveAs changes them.
/// </summary>
public sealed class SolidWorksDocumentTracker
{
    private readonly ISldWorks _solidWorks;
    private readonly Dictionary<IntPtr, DocumentIdentity> _owned = new();
    private readonly HashSet<IntPtr> _protectedIdentities = new();
    private readonly HashSet<string> _protectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _warnings = new();
    private readonly List<DocumentIdentity> _baseline;
    private readonly HashSet<IntPtr> _baselineIdentities = new();
    private readonly HashSet<string> _baselinePaths = new(StringComparer.OrdinalIgnoreCase);
    private int _createdCount;
    private int _closedCount;

    private SolidWorksDocumentTracker(
        ISldWorks solidWorks,
        IReadOnlyList<ModelDoc2> documents,
        bool baselineAvailable)
    {
        _solidWorks = solidWorks;
        BaselineAvailable = baselineAvailable;
        _baseline = new List<DocumentIdentity>();
        foreach (var document in documents)
        {
            var identity = Capture(document);
            if (identity is null || identity.Identity == IntPtr.Zero)
                continue;
            _baseline.Add(identity);
            _baselineIdentities.Add(identity.Identity);
            if (identity.Path is not null)
                _baselinePaths.Add(identity.Path);
        }
    }

    public bool BaselineAvailable { get; }

    public int BaselineCount => _baseline.Count;

    public int CreatedCount => _createdCount;

    public int ClosedCount => _closedCount;

    public int RemainingCreatedCount => _owned.Count;

    public IReadOnlyList<string> Warnings => _warnings;

    public static SolidWorksDocumentTracker Capture(ISldWorks solidWorks)
    {
        ArgumentNullException.ThrowIfNull(solidWorks);
        try
        {
            var raw = solidWorks.GetDocuments();
            var documents = raw switch
            {
                object[] array => array.OfType<ModelDoc2>().ToArray(),
                ModelDoc2 single => new[] { single },
                _ => Array.Empty<ModelDoc2>(),
            };
            return new SolidWorksDocumentTracker(solidWorks, documents, baselineAvailable: true);
        }
        catch (Exception ex)
        {
            var tracker = new SolidWorksDocumentTracker(
                solidWorks,
                Array.Empty<ModelDoc2>(),
                baselineAvailable: false);
            tracker._warnings.Add(
                "Open document baseline is unavailable; Bridge will not close documents automatically: " +
                ex.GetType().Name + ".");
            return tracker;
        }
    }

    public bool Track(ModelDoc2? document, int loadWarnings = 0)
    {
        if (document is null || !BaselineAvailable)
            return false;

        var identity = Capture(document);
        if (identity is null || identity.Identity == IntPtr.Zero)
        {
            _warnings.Add("A created document could not be identified; it will not be closed automatically.");
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
    /// Closes all documents that appeared after the baseline. Assemblies are closed before
    /// their component parts, using the current title/path of each open document.
    /// </summary>
    public IReadOnlyList<string> CloseCreated(ModelDoc2? preferredFirst = null)
    {
        var warnings = new List<string>();
        if (!BaselineAvailable)
        {
            warnings.Add("Document baseline is unavailable; no Bridge documents were closed.");
            _warnings.AddRange(warnings);
            return warnings;
        }

        if (!TryReadCurrentDocuments(out var documents))
        {
            warnings.Add("Open documents could not be enumerated; no Bridge documents were closed.");
            _warnings.AddRange(warnings);
            return warnings;
        }

        // Close the operation's root document first; STEP import helper documents stay open
        // while their root document is loaded. Then close children before parent assemblies.
        var preferredIdentity = preferredFirst is null ? IntPtr.Zero : IdentityOf(preferredFirst);
        foreach (var document in documents
            .OrderBy(item => item.Identity == preferredIdentity ? 0 : 1)
            .ThenBy(item => item.DocType)
            .ToArray())
        {
            if (IsBaseline(document) || IsProtected(document))
                continue;

            try
            {
                var owned = FindOwned(document);
                if (owned is null)
                    _createdCount++;

                if (!TryCloseIdentity(document, warnings))
                    continue;

                _closedCount++;
                if (owned is not null)
                    _owned.Remove(owned.Identity);
            }
            catch (Exception ex)
            {
                warnings.Add(DescribeCloseFailure("CloseCreated", document.Path ?? document.Title, ex));
            }
        }

        _warnings.AddRange(warnings);
        return warnings;
    }

    public SolidWorksDocumentLifecycle Snapshot() =>
        new(BaselineCount, CreatedCount, ClosedCount, RemainingCreatedCount, _warnings.ToArray());

    private bool TryCloseIdentity(DocumentIdentity current, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(current.Title))
        {
            warnings.Add("A Bridge document has no current title; it was not closed.");
            return false;
        }

        var label = Path.GetFileName(current.Path ?? current.Title);
        var closed = false;
        var lastError = string.Empty;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (TryCloseDocument(current, out lastError))
            {
                closed = true;
                break;
            }

            if (attempt < 4)
                Thread.Sleep(50);
        }

        if (!closed)
            warnings.Add("A Bridge document (" + label + ") could not be closed: " + lastError + ".");

        return closed;
    }

    private DocumentIdentity? FindOwned(DocumentIdentity document) =>
        _owned.Values.FirstOrDefault(item => item.Identity == document.Identity)
        ?? _owned.Values.FirstOrDefault(item =>
            item.Path is not null
            && document.Path is not null
            && SolidWorksPath.Same(item.Path, document.Path))
        ?? _owned.Values.FirstOrDefault(item =>
            string.Equals(item.Title, document.Title, StringComparison.Ordinal));

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

    private bool TryReadCurrentDocuments(out IReadOnlyList<DocumentIdentity> documents)
    {
        try
        {
            var raw = _solidWorks.GetDocuments();
            var values = raw switch
            {
                object[] array => array.OfType<ModelDoc2>(),
                ModelDoc2 single => new[] { single },
                _ => Array.Empty<ModelDoc2>(),
            };
            var result = new List<DocumentIdentity>();
            foreach (var document in values)
            {
                var identity = Capture(document);
                if (identity is not null && identity.Identity != IntPtr.Zero)
                    result.Add(identity);
            }

            documents = result;
            return true;
        }
        catch
        {
            documents = Array.Empty<DocumentIdentity>();
            return false;
        }
    }

    private bool TryCloseDocument(DocumentIdentity identity, out string lastError)
    {
        lastError = string.Empty;
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
            catch
            {
                // Closing can still work without activation.
            }
        }

        foreach (var candidate in candidates)
        {
            try
            {
                _solidWorks.CloseDoc(candidate!);
                if (IsClosed(identity))
                    return true;
                AppendError(ref lastError, "CloseDoc returned but document is still open.");
            }
            catch (Exception ex)
            {
                if (IsDisconnected(ex) && IsClosed(identity))
                    return true;
                AppendError(ref lastError, DescribeCloseFailure("CloseDoc", candidate!, ex));
            }
        }

        foreach (var candidate in candidates)
        {
            try
            {
                _solidWorks.QuitDoc(candidate!);
                if (IsClosed(identity))
                    return true;
                AppendError(ref lastError, "QuitDoc returned but document is still open.");
            }
            catch (Exception ex)
            {
                if (IsDisconnected(ex) && IsClosed(identity))
                    return true;
                AppendError(ref lastError, DescribeCloseFailure("QuitDoc", candidate!, ex));
            }
        }

        return false;
    }

    private bool IsClosed(DocumentIdentity identity)
    {
        if (!TryReadCurrentDocuments(out var documents))
            return false;
        return !documents.Any(item =>
            item.Identity == identity.Identity
            || (identity.Path is not null && SolidWorksPath.Same(item.Path, identity.Path)));
    }

    private static bool IsDisconnected(Exception ex) =>
        ex.HResult == unchecked((int)0x80010108);

    private static void AppendError(ref string lastError, string message) =>
        lastError = string.IsNullOrWhiteSpace(lastError) ? message : lastError + "; " + message;

    private static string DescribeCloseFailure(string operation, string candidate, Exception ex) =>
        operation + "(" + Path.GetFileName(candidate) + ") failed " +
        ex.GetType().Name + " 0x" +
        ex.HResult.ToString("X8", CultureInfo.InvariantCulture) + " " + ex.Message;

    private static DocumentIdentity? Capture(ModelDoc2 document)
    {
        try
        {
            var identity = IdentityOf(document);
            if (identity == IntPtr.Zero)
                return null;
            return new DocumentIdentity(
                document,
                identity,
                SolidWorksPath.Normalize(document.GetPathName()),
                document.GetTitle() ?? string.Empty,
                document.GetType());
        }
        catch
        {
            return null;
        }
    }

    private static IntPtr IdentityOf(ModelDoc2 document)
    {
        try
        {
            var identity = Marshal.GetIUnknownForObject(document);
            if (identity != IntPtr.Zero)
                Marshal.Release(identity);
            return identity;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private sealed record DocumentIdentity(
        ModelDoc2 Document,
        IntPtr Identity,
        string? Path,
        string Title,
        int DocType);
}
