using System.IO;
using DXP;
using EcadMcadBridge.Core.Diagnostics;
using PCB;

namespace EcadMcadBridge.Altium.Adapter;

internal sealed record PcbSnapshotData(
    string DocumentFileName,
    int ComponentCount,
    int OutlinePointCount,
    int SampleXIu,
    int SampleYIu,
    double SampleXMm,
    double SampleYMm,
    IReadOnlyList<string> Warnings);

/// <summary>
/// The only layer that touches PCB SDK objects. Typed SDK calls only; no reflection, no writes.
/// </summary>
internal static class PcbSnapshotReader
{
    private const int ControlXIu = 1_000_000;
    private const int ControlYIu = 2_000_000;

    public static PcbSnapshotData Read(IServerDocumentView? view)
    {
        var pcbServer = PCB.GlobalVars.PCBServer;
        if (pcbServer is null)
            throw new InvalidOperationException("The PCB server is not available.");

        var board = pcbServer.GetCurrentPCBBoard();
        if (board is null)
            throw new InvalidOperationException("No active PCB document. Open a .PcbDoc and try again.");

        var document = view?.GetOwnerDocument();
        if (document is not null &&
            !string.Equals(document.GetKind(), "PCB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The active document is not a PCB document.");

        var warnings = new List<string>();
        var fileName = Path.GetFileName(board.GetState_FileName() ?? string.Empty);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = "(unnamed)";
            warnings.Add("The board file name is unavailable.");
        }

        var componentCount = CountComponents(board);
        var (outlinePointCount, firstX, firstY) = ReadOutline(board, warnings);

        var xIu = firstX ?? ControlXIu;
        var yIu = firstY ?? ControlYIu;
        if (firstX is null)
            warnings.Add("Board outline points are unavailable; the sample coordinate uses control IU values.");

        return new PcbSnapshotData(
            fileName,
            componentCount,
            outlinePointCount,
            xIu,
            yIu,
            EDP.Utils.CoordToMMs(xIu),
            EDP.Utils.CoordToMMs(yIu),
            warnings);
    }

    private static int CountComponents(IPCB_Board board)
    {
        var iterator = board.BoardIterator_Create();
        try
        {
            var objectSet = new TObjectSet();
            objectSet.Add(TObjectId.eComponentObject);
            iterator.AddFilter_ObjectSet(objectSet);
            iterator.AddFilter_Method(TIterationMethod.eProcessAll);

            var count = 0;
            var primitive = iterator.FirstPCBObject();
            while (primitive is not null)
            {
                count++;
                primitive = iterator.NextPCBObject();
            }

            return count;
        }
        finally
        {
            board.BoardIterator_Destroy(ref iterator);
        }
    }

    private static (int PointCount, int? FirstX, int? FirstY) ReadOutline(
        IPCB_Board board,
        List<string> warnings)
    {
        try
        {
            var outline = board.GetState_BoardOutline();
            if (outline is null)
            {
                warnings.Add("Board outline API returned null.");
                return (0, null, null);
            }

            var polygon = outline.BoardOutline_GeometricPolygon();
            if (polygon is null)
            {
                warnings.Add("Board outline geometric polygon is unavailable.");
                return (0, null, null);
            }

            var contourCount = polygon.GetState_Count();
            if (contourCount <= 0)
            {
                warnings.Add("Board outline contains no contours.");
                return (0, null, null);
            }

            var total = 0;
            int? firstX = null;
            int? firstY = null;
            for (var i = 0; i < contourCount; i++)
            {
                var contour = polygon.GetState_Contour(i);
                if (contour is null)
                    continue;

                var pointCount = contour.GetState_Count();
                total += pointCount;
                if (firstX is null && pointCount > 0)
                {
                    firstX = contour.GetState_PointX(0);
                    firstY = contour.GetState_PointY(0);
                }
            }

            if (total == 0)
                warnings.Add("Board outline contours contain no points.");

            return (total, firstX, firstY);
        }
        catch (Exception ex)
        {
            warnings.Add("Board outline read failed: " + ex.GetType().Name);
            return (0, null, null);
        }
    }
}
