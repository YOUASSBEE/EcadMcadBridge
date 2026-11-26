using EcadMcadBridge.Altium.Adapter;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class AltiumSelectionPreserverTests
{
    [Fact]
    public void Execute_ClearsForOperationAndRestoresExactSet()
    {
        var store = new FakeSelectionStore("C1", "R2", "U3");
        var observed = -1;

        var result = AltiumSelectionPreserver.Execute(store, () => observed = store.ActiveCount);

        Assert.True(result.Success, result.Error);
        Assert.Equal(0, observed);
        Assert.Equal(new[] { "C1", "R2", "U3" }, store.Identifiers);
        Assert.Equal(2, store.BeginCount);
        Assert.Equal(2, store.EndCount);
    }

    [Fact]
    public void Execute_RestoresAfterOperationThrows()
    {
        var store = new FakeSelectionStore("C1", "R2");

        var result = AltiumSelectionPreserver.Execute(
            store,
            () => throw new InvalidOperationException("export failed"));

        Assert.False(result.Success);
        Assert.Contains("export failed", result.Error, StringComparison.Ordinal);
        Assert.Equal(new[] { "C1", "R2" }, store.Identifiers);
    }

    [Fact]
    public void Execute_FailsClosedWhenIdentifierIsMissing()
    {
        var store = new FakeSelectionStore(string.Empty);
        var invoked = false;

        var result = AltiumSelectionPreserver.Execute(store, () => invoked = true);

        Assert.False(result.Success);
        Assert.Contains("identifier", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(invoked);
        Assert.Equal(0, store.BeginCount);
        Assert.Equal(new[] { string.Empty }, store.Identifiers);
    }

    [Fact]
    public void Execute_FailsWhenRestoredSetChanges()
    {
        var store = new FakeSelectionStore("C1", "R2") { DropOnAdd = "R2" };

        var result = AltiumSelectionPreserver.Execute(store, () => { });

        Assert.False(result.Success);
        Assert.Contains("restored", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "C1" }, store.Identifiers);
    }

    [Fact]
    public void Execute_LeavesEmptySelectionUnchanged()
    {
        var store = new FakeSelectionStore();

        var result = AltiumSelectionPreserver.Execute(store, () => { });

        Assert.True(result.Success, result.Error);
        Assert.Empty(store.Identifiers);
        Assert.Equal(0, store.BeginCount);
        Assert.Equal(0, store.EndCount);
    }

    [Fact]
    public void Execute_EndsUpdateWhenClearThrows()
    {
        var store = new FakeSelectionStore("C1") { ThrowOnClear = true };

        var result = AltiumSelectionPreserver.Execute(store, () => { });

        Assert.False(result.Success);
        Assert.Equal(2, store.BeginCount);
        Assert.Equal(2, store.EndCount);
    }

    private sealed class FakeSelectionStore(params string[] initial) : IAltiumSelectionStore
    {
        private readonly List<string> _items = initial.ToList();

        public int SnapshotCount => _items.Count;

        public int ActiveCount => _items.Count;

        public IEnumerable<string> Identifiers => _items;

        public string? DropOnAdd { get; init; }

        public bool ThrowOnClear { get; init; }

        public int BeginCount { get; private set; }

        public int EndCount { get; private set; }

        public IReadOnlyList<object> Capture() => _items.Cast<object>().ToArray();

        public string Describe(object item) => (string)item;

        public void BeginUpdate() => BeginCount++;

        public void Clear()
        {
            if (ThrowOnClear)
                throw new InvalidOperationException("clear failed");
            _items.Clear();
        }

        public void Add(object item)
        {
            if ((string)item != DropOnAdd)
                _items.Add((string)item);
        }

        public void EndUpdate() => EndCount++;
    }
}
