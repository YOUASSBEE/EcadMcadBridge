using EcadMcadBridge.Altium.Application;
using EcadMcadBridge.Core.Diagnostics;
using EcadMcadBridge.Core.Model;
using PCB;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>
/// Reads the operator's PCB selection into CAD-independent Top-only candidates.
/// Every SDK call here is read-only; the original selection is never changed.
/// </summary>
internal static class AltiumTopOnlySelectionReader
{
    public static IReadOnlyList<TopOnlySelectionCandidate> Read(IPCB_Board board)
    {
        ArgumentNullException.ThrowIfNull(board);

        var store = new PcbBoardSelectionStore(board);
        var selected = store.Capture();
        var candidates = new List<TopOnlySelectionCandidate>(selected.Count);
        foreach (var item in selected)
        {
            var reference = store.Describe(item);
            var component = ResolveComponent(item);
            if (component is null)
            {
                candidates.Add(new TopOnlySelectionCandidate(
                    string.IsNullOrWhiteSpace(reference) ? "unknown" : reference,
                    new AltiumComponentKey(null, null, string.Empty, 0, 0, 0, BoardSide.Top),
                    false,
                    false,
                    false,
                    false));
                continue;
            }

            var designator = component.GetState_Name()?.GetState_Text() ?? string.Empty;
            var key = new AltiumComponentKey(
                component.GetState_UniqueId(),
                component.GetState_SourceUniqueId(),
                designator,
                component.GetState_XLocation(),
                component.GetState_YLocation(),
                AngleNormalizer.NormalizeDegrees(component.GetState_Rotation()),
                AltiumBoardSnapshotAdapter.ResolveComponentSide(component));
            var (readable, topOnly, modelAvailable) = InspectBodies(component);
            candidates.Add(new TopOnlySelectionCandidate(
                string.IsNullOrWhiteSpace(designator) ? reference : designator,
                key,
                true,
                readable,
                topOnly,
                modelAvailable));
        }

        return candidates;
    }

    private static IPCB_Component? ResolveComponent(object item)
    {
        if (item is IPCB_Component component)
            return component;

        return item is IPCB_Primitive primitive ? primitive.GetState_Component() : null;
    }

    private static (bool Readable, bool TopOnly, bool ModelAvailable) InspectBodies(IPCB_Component component)
    {
        var iterator = component.GroupIterator_Create();
        if (iterator is null)
            return (false, false, false);

        var readable = true;
        var topOnly = true;
        var modelAvailable = false;
        try
        {
            iterator.AddFilter_ObjectSet(new TObjectSet { TObjectId.eComponentBodyObject });
            for (var primitive = iterator.FirstPCBObject(); primitive is not null; primitive = iterator.NextPCBObject())
            {
                if (primitive is not IPCB_ComponentBody body)
                    continue;

                topOnly &= body.GetBodyProjection() == TBoardSide.eBoardSide_Top;
                IPCB_Model? model = null;
                try
                {
                    model = body.GetModel();
                }
                catch
                {
                    readable = false;
                }

                modelAvailable |= model is not null;
            }
        }
        catch
        {
            readable = false;
        }
        finally
        {
            component.GroupIterator_Destroy(ref iterator);
        }

        return (readable && topOnly, topOnly, modelAvailable);
    }
}
