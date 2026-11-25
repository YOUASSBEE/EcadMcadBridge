using System.Globalization;
using SolidWorks.Interop.swconst;

namespace EcadMcadBridge.SolidWorks.Interop;

public sealed record SolidWorksWarningFlag(int Value, string Name);

/// <summary>Decodes SOLIDWORKS bitmask warnings without treating known bits as errors.</summary>
public static class SolidWorksWarningDecoder
{
    private static readonly (int Value, string Name)[] SaveFlags =
    {
        ((int)swFileSaveWarning_e.swFileSaveWarning_RebuildError, nameof(swFileSaveWarning_e.swFileSaveWarning_RebuildError)),
        ((int)swFileSaveWarning_e.swFileSaveWarning_NeedsRebuild, nameof(swFileSaveWarning_e.swFileSaveWarning_NeedsRebuild)),
        ((int)swFileSaveWarning_e.swFileSaveWarning_ViewsNeedUpdate, nameof(swFileSaveWarning_e.swFileSaveWarning_ViewsNeedUpdate)),
        ((int)swFileSaveWarning_e.swFileSaveWarning_AnimatorNeedToSolve, nameof(swFileSaveWarning_e.swFileSaveWarning_AnimatorNeedToSolve)),
        ((int)swFileSaveWarning_e.swFileSaveWarning_AnimatorFeatureEdits, nameof(swFileSaveWarning_e.swFileSaveWarning_AnimatorFeatureEdits)),
        ((int)swFileSaveWarning_e.swFileSaveWarning_EdrwingsBadSelection, nameof(swFileSaveWarning_e.swFileSaveWarning_EdrwingsBadSelection)),
        ((int)swFileSaveWarning_e.swFileSaveWarning_AnimatorLightEdits, nameof(swFileSaveWarning_e.swFileSaveWarning_AnimatorLightEdits)),
        ((int)swFileSaveWarning_e.swFileSaveWarning_AnimatorCameraViews, nameof(swFileSaveWarning_e.swFileSaveWarning_AnimatorCameraViews)),
        ((int)swFileSaveWarning_e.swFileSaveWarning_AnimatorSectionViews, nameof(swFileSaveWarning_e.swFileSaveWarning_AnimatorSectionViews)),
        ((int)swFileSaveWarning_e.swFileSaveWarning_MissingOLEObjects, nameof(swFileSaveWarning_e.swFileSaveWarning_MissingOLEObjects)),
        ((int)swFileSaveWarning_e.swFileSaveWarning_OpenedViewOnly, nameof(swFileSaveWarning_e.swFileSaveWarning_OpenedViewOnly)),
        ((int)swFileSaveWarning_e.swFileSaveWarning_XmlInvalid, nameof(swFileSaveWarning_e.swFileSaveWarning_XmlInvalid)),
    };

    private static readonly (int Value, string Name)[] LoadFlags =
    {
        ((int)swFileLoadWarning_e.swFileLoadWarning_IdMismatch, nameof(swFileLoadWarning_e.swFileLoadWarning_IdMismatch)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_ReadOnly, nameof(swFileLoadWarning_e.swFileLoadWarning_ReadOnly)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_SharingViolation, nameof(swFileLoadWarning_e.swFileLoadWarning_SharingViolation)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_DrawingANSIUpdate, nameof(swFileLoadWarning_e.swFileLoadWarning_DrawingANSIUpdate)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_SheetScaleUpdate, nameof(swFileLoadWarning_e.swFileLoadWarning_SheetScaleUpdate)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_NeedsRegen, nameof(swFileLoadWarning_e.swFileLoadWarning_NeedsRegen)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_BasePartNotLoaded, nameof(swFileLoadWarning_e.swFileLoadWarning_BasePartNotLoaded)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen, nameof(swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_DrawingsOnlyRapidDraft, nameof(swFileLoadWarning_e.swFileLoadWarning_DrawingsOnlyRapidDraft)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_ViewOnlyRestrictions, nameof(swFileLoadWarning_e.swFileLoadWarning_ViewOnlyRestrictions)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_ViewMissingReferencedConfig, nameof(swFileLoadWarning_e.swFileLoadWarning_ViewMissingReferencedConfig)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_DrawingSFSymbolConvert, nameof(swFileLoadWarning_e.swFileLoadWarning_DrawingSFSymbolConvert)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_RevolveDimTolerance, nameof(swFileLoadWarning_e.swFileLoadWarning_RevolveDimTolerance)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_ModelOutOfDate, nameof(swFileLoadWarning_e.swFileLoadWarning_ModelOutOfDate)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_DimensionsReferencedIncorrectlyToModels, nameof(swFileLoadWarning_e.swFileLoadWarning_DimensionsReferencedIncorrectlyToModels)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_ComponentMissingReferencedConfig, nameof(swFileLoadWarning_e.swFileLoadWarning_ComponentMissingReferencedConfig)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_InvisibleDoc_LinkedDesignTableUpdateFail, nameof(swFileLoadWarning_e.swFileLoadWarning_InvisibleDoc_LinkedDesignTableUpdateFail)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_MissingDesignTable, nameof(swFileLoadWarning_e.swFileLoadWarning_MissingDesignTable)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_AutomaticRepair, nameof(swFileLoadWarning_e.swFileLoadWarning_AutomaticRepair)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_CriticalDataRepair, nameof(swFileLoadWarning_e.swFileLoadWarning_CriticalDataRepair)),
        ((int)swFileLoadWarning_e.swFileLoadWarning_MissingExternalReferences, nameof(swFileLoadWarning_e.swFileLoadWarning_MissingExternalReferences)),
    };

    public static IReadOnlyList<SolidWorksWarningFlag> DecodeSaveWarnings(int warnings) =>
        Decode(warnings, SaveFlags);

    public static IReadOnlyList<SolidWorksWarningFlag> DecodeLoadWarnings(int warnings) =>
        Decode(warnings, LoadFlags);

    public static string Describe(IReadOnlyList<SolidWorksWarningFlag> flags, int rawValue) =>
        rawValue == 0
            ? "none"
            : string.Join(
                ", ",
                flags.Select(flag => flag.Name + "(" + flag.Value.ToString(CultureInfo.InvariantCulture) + ")")
                    .Append("raw=" + rawValue.ToString(CultureInfo.InvariantCulture)));

    private static IReadOnlyList<SolidWorksWarningFlag> Decode(
        int warnings,
        IReadOnlyList<(int Value, string Name)> known)
    {
        if (warnings == 0)
            return Array.Empty<SolidWorksWarningFlag>();

        var result = new List<SolidWorksWarningFlag>();
        var remaining = warnings;
        foreach (var (value, name) in known)
        {
            if ((warnings & value) != 0)
            {
                result.Add(new SolidWorksWarningFlag(value, name));
                remaining &= ~value;
            }
        }

        for (var bit = 1; bit != 0 && remaining != 0; bit <<= 1)
        {
            if ((remaining & bit) == 0)
                continue;
            result.Add(new SolidWorksWarningFlag(bit, "unknown"));
            remaining &= ~bit;
        }

        return result;
    }
}
