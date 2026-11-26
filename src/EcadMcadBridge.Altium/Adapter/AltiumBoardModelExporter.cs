using System.IO;
using System.Text;
using EcadMcadBridge.Core.Packaging;
using PCB;

namespace EcadMcadBridge.Altium.Adapter;

internal sealed record AltiumBoardModelExportResult(
    string? StepPath,
    string? Sha256,
    long Size,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public bool IsSuccess => StepPath is not null && Sha256 is not null && Error is null;
}

/// <summary>
/// Exports the PCB substrate only. Altium's exporter has no "no components" option, so the
/// board-only contract is expressed by SelectedComponents plus a verified empty selection.
/// The original user selection is restored exactly after the export; all exporter switches
/// are set explicitly and saved user preferences are not read.
/// </summary>
internal static class AltiumBoardModelExporter
{
    private const string StepFileName = "board.step";

    public static AltiumBoardModelExportResult Export(IPCB_Board board, string exportDirectory)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(exportDirectory))
            return Failure("Board model export directory is required.", warnings);
        if (board is null)
            return Failure("The PCB board is unavailable for board model export.", warnings);

        AltiumBoardModelExportResult? core = null;
        var preservation = AltiumSelectionPreserver.Execute(
            new PcbBoardSelectionStore(board),
            () => core = ExportCore(board, exportDirectory, warnings));

        var mergedWarnings = core is null
            ? new List<string>(warnings)
            : new List<string>(core.Warnings);
        mergedWarnings.AddRange(preservation.Warnings);

        if (!preservation.Success)
            return Failure(preservation.Error ?? "PCB selection preservation failed.", mergedWarnings);
        if (core is null)
            return Failure("Board model export did not return a result.", mergedWarnings);

        return core with { Warnings = mergedWarnings };
    }

    private static AltiumBoardModelExportResult ExportCore(
        IPCB_Board board,
        string exportDirectory,
        List<string> warnings)
    {
        try
        {
            Directory.CreateDirectory(exportDirectory);
            var stepPath = Path.Combine(exportDirectory, StepFileName);
            AltiumStepExportFile.TryDelete(stepPath);

            var server = PCB.GlobalVars.PCBServer;
            if (server is null)
                return Failure("The PCB server is not running.", warnings);

            var exporter = server.Board3DModelExporter();
            if (exporter is null)
                return Failure("IPCB_ServerInterface.Board3DModelExporter() returned null.", warnings);

            Configure(exporter);

            // SelectedComponents is the only SDK option that can exclude component bodies.
            // AltiumSelectionPreserver guarantees an empty selection for the whole export.
            if (board.SelectedObjectsCount() != 0)
                return Failure("PCB selection is not empty; board-only export is not provable.", warnings);

            exporter.ExportBoard3DModel(
                stepPath,
                TBoard3DModelExportFormat.eBoard3DModelExportFormat_Step,
                board,
                null!);

            if (!AltiumStepExportFile.IsValid(stepPath))
                return Failure("Board STEP export produced no file.", warnings);

            if (!HasAp214Header(stepPath))
                return Failure("Board STEP file does not declare STEP AP214.", warnings);

            var hash = PackageFileHasher.ComputeStableSha256(stepPath);
            if (!hash.IsSuccess || hash.Sha256 is null)
                return Failure("Board STEP SHA-256 could not be computed: " + hash.Message, warnings);
            if (hash.Sha256.Length != 64 || hash.Sha256.Any(character =>
                    character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            {
                return Failure("Board STEP SHA-256 is not a lowercase hexadecimal digest.", warnings);
            }

            return new AltiumBoardModelExportResult(stepPath, hash.Sha256, hash.Size, warnings, null);
        }
        catch (Exception ex)
        {
            return Failure(
                "Board model export failed: " + ex.GetType().Name + " " +
                ex.Message.Replace("\r", " ", StringComparison.Ordinal)
                    .Replace("\n", " ", StringComparison.Ordinal),
                warnings);
        }
    }

    private static void Configure(IPCB_Board3DModelExporter exporter)
    {
        exporter.SetState_ExportComponentOptions(
            TBoard3DModelExportComponentOptions.eBoard3DModelExportComponentOptions_SelectedComponents);
        exporter.SetState_ExportModelsOption(
            TBoard3DModelExportModelsOptions.eBoard3DModelExportModelsOptions_AsStep);
        exporter.SetState_ExportHolesOption(
            TBoard3DModelExportHolesOptions.eBoard3DModelExportHolesOptions_AllHoles);
        exporter.SetState_ExportCopperOption(
            TBoard3DModelExportCopperOptions.eBoard3DModelExportCopperOptions_CopperNone);
        exporter.SetState_ExportFoldedBoard(false);
        exporter.SetState_ComponentSuffixType(
            TBoard3DModelExportComponentSuffix.eBoard3DModelExportComponentSuffix_None);
        // Pad/via drill holes are not part of the substrate outline contract; excluding them
        // keeps the exported body volume consistent with the snapshot Outer/Hole contours.
        exporter.SetState_IncludeMechanicalPadHoles(false);
        exporter.SetState_IncludeElectricalPadHoles(false);
        exporter.SetState_IncludeFreePadHoles(false);
        exporter.SetState_IncludeCoverLayer(false);
        exporter.SetState_ExportPadAndViaBarrelsOnly(false);
    }

    internal static bool HasAp214Header(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = (int)Math.Min(stream.Length, 8192);
        var buffer = new byte[length];
        var read = stream.Read(buffer, 0, length);
        var text = Encoding.ASCII.GetString(buffer, 0, read);
        if (!text.StartsWith("ISO-10303-21;", StringComparison.Ordinal))
            return false;

        var schemaStart = text.IndexOf("FILE_SCHEMA", StringComparison.Ordinal);
        if (schemaStart < 0)
            return false;

        var schemaEnd = text.IndexOf("ENDSEC;", schemaStart, StringComparison.Ordinal);
        if (schemaEnd < 0)
            return false;

        return text[schemaStart..schemaEnd].Contains("AUTOMOTIVE_DESIGN", StringComparison.Ordinal)
            && text[schemaStart..schemaEnd].Contains("10303 214", StringComparison.Ordinal);
    }

    private static AltiumBoardModelExportResult Failure(
        string message,
        IReadOnlyList<string> warnings) =>
        new(null, null, 0, warnings, message);
}
