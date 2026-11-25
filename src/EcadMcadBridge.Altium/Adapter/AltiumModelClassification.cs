using System.IO;
using EcadMcadBridge.Core.Model;
using PCB;

namespace EcadMcadBridge.Altium.Adapter;

/// <summary>
/// Pure mapping of Altium model type/embed/source evidence to the Core catalog values.
/// It never promotes GetEmbed=false to linked.
/// </summary>
internal static class AltiumModelClassification
{
    public static ComponentModelRepresentation MapRepresentation(T3DModelType? modelType) => modelType switch
    {
        T3DModelType.e3DModelType_Generic => ComponentModelRepresentation.Generic,
        T3DModelType.e3DModelType_Extruded => ComponentModelRepresentation.Native,
        T3DModelType.e3DModelType_Cylinder => ComponentModelRepresentation.Native,
        T3DModelType.e3DModelType_Sphere => ComponentModelRepresentation.Native,
        _ => ComponentModelRepresentation.Unknown,
    };

    /// <summary>
    /// A basename from an embedded generic model is not external evidence. File/Vault
    /// sources, rooted paths and any filename on a procedural model are external evidence.
    /// </summary>
    public static bool IsExternalFileName(
        string? fileName,
        TModelSource? modelSource,
        ComponentModelRepresentation representation)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        if (modelSource is TModelSource.eModelSource_File or TModelSource.eModelsource_Vault)
            return true;
        if (representation == ComponentModelRepresentation.Native)
            return true;
        return Path.IsPathRooted(fileName);
    }

    public static ComponentModelSourceOrigin ClassifySourceOrigin(
        T3DModelType? modelType,
        bool? embed,
        TModelSource? modelSource,
        string? fileName)
    {
        var representation = MapRepresentation(modelType);
        var hasExternalEvidence = modelSource is TModelSource.eModelSource_File
                or TModelSource.eModelsource_Vault
            || !string.IsNullOrWhiteSpace(fileName);

        if (embed == true)
            return hasExternalEvidence
                ? ComponentModelSourceOrigin.Unknown
                : ComponentModelSourceOrigin.Embedded;

        if (representation == ComponentModelRepresentation.Native && !hasExternalEvidence)
            return ComponentModelSourceOrigin.Native;

        return ComponentModelSourceOrigin.Unknown;
    }
}
