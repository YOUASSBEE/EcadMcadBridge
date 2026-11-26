using PCB;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>Minimal typed bridge between a PCB board and its current selection.</summary>
internal interface IAltiumSelectionStore
{
    int SnapshotCount { get; }

    int ActiveCount { get; }

    IReadOnlyList<object> Capture();

    string Describe(object item);

    void BeginUpdate();

    void Clear();

    void Add(object item);

    void EndUpdate();
}

internal sealed class PcbBoardSelectionStore(IPCB_Board board) : IAltiumSelectionStore
{
    public int SnapshotCount => board.GetState_SelectecObjectCount();

    public int ActiveCount => board.SelectedObjectsCount();

    public IReadOnlyList<object> Capture()
    {
        var count = SnapshotCount;
        var items = new List<object>(count);
        for (var i = 0; i < count; i++)
        {
            if (board.GetState_SelectecObject(i) is not { } item)
                throw new InvalidOperationException("A selected PCB object could not be read.");
            items.Add(item);
        }

        return items;
    }

    public string Describe(object item) =>
        item is IPCB_Primitive primitive ? primitive.GetState_Identifier() : string.Empty;

    public void BeginUpdate() => board.SelectedObjects_BeginUpdate();

    public void Clear() => board.SelectedObjects_Clear();

    public void Add(object item)
    {
        if (item is not IPCB_Primitive primitive)
            throw new InvalidOperationException("A captured PCB object is not IPCB_Primitive.");
        board.SelectedObjects_Add(primitive);
    }

    public void EndUpdate() => board.SelectedObjects_EndUpdate();
}

/// <summary>Runs an operation with an empty PCB selection and restores the exact original set.</summary>
internal static class AltiumSelectionPreserver
{
    internal sealed record Result(bool Success, string? Error, IReadOnlyList<string> Warnings);

    public static Result Execute(IAltiumSelectionStore store, Action action)
    {
        var warnings = new List<string>();
        try
        {
            var selected = store.Capture();
            var snapshotCount = store.SnapshotCount;
            var activeCount = store.ActiveCount;
            if (selected.Count != snapshotCount || snapshotCount != activeCount)
                return Fail("PCB selection counts are inconsistent; board-only export is not provable.", warnings);

            var identifiers = selected.Select(store.Describe).ToArray();
            if (identifiers.Any(string.IsNullOrWhiteSpace)
                || identifiers.Distinct(StringComparer.Ordinal).Count() != identifiers.Length)
            {
                return Fail("PCB selection objects have no unique identifier; selection cannot be preserved.", warnings);
            }

            if (selected.Count == 0)
            {
                action();
                if (store.ActiveCount != 0 || store.SnapshotCount != 0)
                {
                    TryClearEmptySelection(store, warnings);
                    return Fail("PCB selection changed during board model export.", warnings);
                }

                return new Result(true, null, warnings);
            }

            var clearAttempted = false;
            string? error = null;
            try
            {
                WithUpdate(store, () =>
                {
                    clearAttempted = true;
                    store.Clear();
                });

                if (store.ActiveCount != 0 || store.SnapshotCount != 0)
                    error = "PCB selection could not be cleared; board-only export is not provable.";
                else
                    action();
            }
            finally
            {
                if (clearAttempted)
                {
                    var restoreError = TryRestore(store, selected, identifiers);
                    if (restoreError is not null)
                    {
                        warnings.Add(restoreError);
                        error ??= "PCB selection could not be restored exactly after board model export.";
                    }
                }
            }

            return error is null ? new Result(true, null, warnings) : Fail(error, warnings);
        }
        catch (Exception ex)
        {
            return Fail(
                "PCB selection preservation failed: " + ex.GetType().Name + " " +
                ex.Message.Replace("\r", " ", StringComparison.Ordinal)
                    .Replace("\n", " ", StringComparison.Ordinal),
                warnings);
        }
    }

    private static void WithUpdate(IAltiumSelectionStore store, Action action)
    {
        store.BeginUpdate();
        try
        {
            action();
        }
        finally
        {
            store.EndUpdate();
        }
    }

    private static string? TryRestore(
        IAltiumSelectionStore store,
        IReadOnlyList<object> selected,
        IReadOnlyList<string> expected)
    {
        try
        {
            WithUpdate(store, () =>
            {
                store.Clear();
                foreach (var item in selected)
                    store.Add(item);
            });

            var actual = store.Capture().Select(store.Describe)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var orderedExpected = expected.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            if (store.ActiveCount != store.SnapshotCount
                || !orderedExpected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                return "PCB selection was not restored to the exact original set.";
            }

            return null;
        }
        catch (Exception ex)
        {
            return "PCB selection restore failed: " + ex.GetType().Name + " " + ex.Message;
        }
    }

    private static void TryClearEmptySelection(IAltiumSelectionStore store, List<string> warnings)
    {
        try
        {
            WithUpdate(store, store.Clear);
        }
        catch (Exception ex)
        {
            warnings.Add("PCB selection cleanup failed: " + ex.GetType().Name + " " + ex.Message);
        }
    }

    private static Result Fail(string error, List<string> warnings) => new(false, error, warnings);
}
