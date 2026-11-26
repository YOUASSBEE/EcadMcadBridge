using System.IO;
using DXP;
using PCB;

namespace EcadMcadBridge.Altium.Application;

/// <summary>Read-only typed SDK bridge for the current PCB selection. No document or selection mutation.</summary>
internal static class SelectedObjectDiagnosticsReader
{
    private const string SchemaVersion = "1.0";

    // SDK 4.0.1 cannot prove that the board from GetCurrentPCBBoard() belongs to the command's view.
    private const bool BoardOwnershipProven = false;

    public static SelectedObjectDiagnosticsReport Read(IServerDocumentView? view)
    {
        var measuredAt = DateTimeOffset.UtcNow;
        var documentType = ReadDocumentType(view);
        if (!TryValidateContext(documentType, BoardOwnershipProven, out var contextError))
        {
            return new SelectedObjectDiagnosticsReport(
                SchemaVersion,
                measuredAt,
                documentType,
                0,
                0,
                0,
                DiagnosticValue.Unavailable(contextError!),
                Array.Empty<SelectedObjectDiagnostic>());
        }

        var noObjects = Array.Empty<SelectedObjectDiagnostic>();
        var boardResult = TryGetBoard();

        if (boardResult.Error is not null || boardResult.Board is null)
        {
            return new SelectedObjectDiagnosticsReport(
                SchemaVersion,
                measuredAt,
                documentType,
                0,
                0,
                0,
                DiagnosticValue.Unavailable(boardResult.Error ?? "The PCB board is unavailable."),
                noObjects);
        }

        var board = boardResult.Board;
        var before = TryReadCount(board, out var countError);
        if (countError is not null)
        {
            return new SelectedObjectDiagnosticsReport(
                SchemaVersion,
                measuredAt,
                documentType,
                0,
                0,
                0,
                DiagnosticValue.Unavailable(countError),
                noObjects);
        }

        var objects = new List<SelectedObjectDiagnostic>(before);
        for (var index = 0; index < before; index++)
        {
            IPCB_Primitive? primitive;
            try
            {
                primitive = board.GetState_SelectecObject(index);
            }
            catch (Exception ex)
            {
                objects.Add(UnavailablePrimitive("Selected object " + index + " read failed: " + ex.GetType().Name + "."));
                continue;
            }

            objects.Add(primitive is null
                ? UnavailablePrimitive("Selected object " + index + " was null.")
                : ReadPrimitive(primitive));
        }

        var after = TryReadCount(board, out countError);
        var readStatus = countError is not null
            ? DiagnosticValue.Unavailable(countError)
            : before == after
                ? DiagnosticValue.Available("Selection count unchanged.")
                : DiagnosticValue.Unavailable("Selection count changed during the read.");

        return new SelectedObjectDiagnosticsReport(
            SchemaVersion,
            measuredAt,
            documentType,
            objects.Count,
            before,
            after,
            readStatus,
            objects);
    }

    internal static bool TryValidateContext(
        DiagnosticValue documentType,
        bool boardOwnershipProven,
        out string? reason)
    {
        if (documentType.Status != "available")
        {
            reason = "Document context is unavailable: " + documentType.Reason;
            return false;
        }

        if (!string.Equals(documentType.Value, "PCB", StringComparison.OrdinalIgnoreCase))
        {
            reason = "The supplied document is not a PCB document.";
            return false;
        }

        if (!boardOwnershipProven)
        {
            reason = "Document/board ownership cannot be proven with documented SDK 4.0.1 APIs; " +
                "refusing to read the global current board.";
            return false;
        }

        reason = null;
        return true;
    }

    private static DiagnosticValue ReadDocumentType(IServerDocumentView? view)
    {
        try
        {
            var document = view?.GetOwnerDocument();
            return document is null
                ? DiagnosticValue.Unavailable("No owner document was supplied by the host.")
                : ReadString(() => document.GetKind());
        }
        catch (Exception ex)
        {
            return DiagnosticValue.Unavailable("Document kind read failed: " + ex.GetType().Name + ".");
        }
    }

    private static (IPCB_Board? Board, string? Error) TryGetBoard()
    {
        try
        {
            var server = PCB.GlobalVars.PCBServer;
            if (server is null)
                return (null, "The PCB server is unavailable.");

            var board = server.GetCurrentPCBBoard();
            return board is null ? (null, "No active PCB board.") : (board, null);
        }
        catch (Exception ex)
        {
            return (null, "PCB board read failed: " + ex.GetType().Name + ".");
        }
    }

    private static int TryReadCount(IPCB_Board board, out string? error)
    {
        try
        {
            error = null;
            return board.GetState_SelectecObjectCount();
        }
        catch (Exception ex)
        {
            error = "Selection count read failed: " + ex.GetType().Name + ".";
            return 0;
        }
    }

    private static SelectedObjectDiagnostic ReadPrimitive(IPCB_Primitive primitive)
    {
        var objectType = ReadString(() => primitive.GetState_ObjectIDString());
        if (objectType.Status == "unavailable")
            objectType = ReadString(() => primitive.GetState_ObjectID().ToString());

        IPCB_Component? component = null;
        try
        {
            component = primitive.GetState_Component();
        }
        catch
        {
            // Ownership is optional for non-component primitives.
        }

        var notApplicable = DiagnosticValue.Unavailable("Not applicable to this object type.");
        var bodyModels = primitive is IPCB_ComponentBody body
            ? DiagnosticCollection<BodyModelDiagnostic>.Available(new[] { ReadBody(body, component) })
            : component is not null
                ? ReadBodies(component)
                : DiagnosticCollection<BodyModelDiagnostic>.Unavailable("Not applicable to this object type.");

        return new SelectedObjectDiagnostic(
            objectType,
            ReadString(() => primitive.GetState_UniqueId()),
            ReadString(() => primitive.GetState_Identifier()),
            component is null ? notApplicable : ReadString(() => component.GetState_Pattern()),
            component is null ? notApplicable : ReadString(() => component.GetState_SourceUniqueId()),
            component is null ? notApplicable : ReadString(() => component.GetState_SourceFootprintLibrary()),
            component is null ? notApplicable : ReadString(() => component.GetState_SourceComponentLibrary()),
            component is null ? notApplicable : ReadString(() => component.GetState_VaultGUID()),
            component is null ? notApplicable : ReadString(() => component.GetState_ItemGUID()),
            component is null ? notApplicable : ReadString(() => component.GetState_ItemRevisionGUID()),
            bodyModels);
    }

    private static DiagnosticCollection<BodyModelDiagnostic> ReadBodies(IPCB_Component component)
    {
        IPCB_GroupIterator? iterator = null;
        try
        {
            iterator = component.GroupIterator_Create();
            if (iterator is null)
                return DiagnosticCollection<BodyModelDiagnostic>.Unavailable("The component group iterator was unavailable.");

            var objectSet = new TObjectSet();
            objectSet.Add(TObjectId.eComponentBodyObject);
            iterator.AddFilter_ObjectSet(objectSet);

            var bodies = new List<BodyModelDiagnostic>();
            var primitive = iterator.FirstPCBObject();
            while (primitive is not null)
            {
                if (primitive is IPCB_ComponentBody body)
                    bodies.Add(ReadBody(body, component));

                primitive = iterator.NextPCBObject();
            }

            return bodies.Count == 0
                ? DiagnosticCollection<BodyModelDiagnostic>.Unavailable("No component bodies were enumerated.")
                : DiagnosticCollection<BodyModelDiagnostic>.Available(bodies);
        }
        catch (Exception ex)
        {
            return DiagnosticCollection<BodyModelDiagnostic>.Unavailable(
                "Component body enumeration failed: " + ex.GetType().Name + ".");
        }
        finally
        {
            if (iterator is not null)
            {
                try
                {
                    component.GroupIterator_Destroy(ref iterator);
                }
                catch
                {
                    // Iterator cleanup must not change the selection or hide the read result.
                }
            }
        }
    }

    private static BodyModelDiagnostic ReadBody(IPCB_ComponentBody body, IPCB_Component? fallbackOwner)
    {
        IPCB_Component? owner = fallbackOwner;
        try
        {
            owner = body.GetState_Component() ?? fallbackOwner;
        }
        catch
        {
            // The primitive owner remains the fallback.
        }

        IPCB_Model? model = null;
        try
        {
            model = body.GetModel();
        }
        catch
        {
            // Model fields are reported as unavailable below.
        }

        var notApplicable = DiagnosticValue.Unavailable("Not applicable to this body.");
        return new BodyModelDiagnostic(
            ReadString(() => body.GetState_UniqueId()),
            ReadString(() => body.GetState_Identifier()),
            owner is null ? notApplicable : ReadString(() => owner.GetState_UniqueId()),
            owner is null ? notApplicable : ReadString(() => owner.GetState_Identifier()),
            model is null
                ? notApplicable
                : ReadString(() =>
                {
                    var path = model.GetFileName();
                    return string.IsNullOrEmpty(path) ? null : Path.GetFileName(path);
                }),
            model is null
                ? notApplicable
                : ReadString(() => model.GetState_ModelType().ToString()));
    }

    private static SelectedObjectDiagnostic UnavailablePrimitive(string reason)
    {
        var value = DiagnosticValue.Unavailable(reason);
        return new SelectedObjectDiagnostic(
            value,
            value,
            value,
            value,
            value,
            value,
            value,
            value,
            value,
            value,
            DiagnosticCollection<BodyModelDiagnostic>.Unavailable(reason));
    }

    private static DiagnosticValue ReadString(Func<string?> getter)
    {
        try
        {
            var value = getter();
            return string.IsNullOrEmpty(value)
                ? DiagnosticValue.Unavailable("SDK returned no value.")
                : DiagnosticValue.Available(value);
        }
        catch (Exception ex)
        {
            return DiagnosticValue.Unavailable("SDK getter threw " + ex.GetType().Name + ".");
        }
    }
}
