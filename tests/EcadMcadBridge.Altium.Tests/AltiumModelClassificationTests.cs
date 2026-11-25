using EcadMcadBridge.Altium.Adapter;
using EcadMcadBridge.Core.Model;
using PCB;
using Xunit;

namespace EcadMcadBridge.Altium.Tests;

public sealed class AltiumModelClassificationTests
{
    [Theory]
    [InlineData(T3DModelType.e3DModelType_Generic, ComponentModelRepresentation.Generic)]
    [InlineData(T3DModelType.e3DModelType_Extruded, ComponentModelRepresentation.Native)]
    [InlineData(T3DModelType.e3DModelType_Cylinder, ComponentModelRepresentation.Native)]
    [InlineData(T3DModelType.e3DModelType_Sphere, ComponentModelRepresentation.Native)]
    public void MapsRepresentation(T3DModelType modelType, ComponentModelRepresentation expected)
    {
        Assert.Equal(expected, AltiumModelClassification.MapRepresentation(modelType));
    }

    [Fact]
    public void UnknownModelTypeMapsToUnknown()
    {
        Assert.Equal(
            ComponentModelRepresentation.Unknown,
            AltiumModelClassification.MapRepresentation((T3DModelType)999));
        Assert.Equal(
            ComponentModelRepresentation.Unknown,
            AltiumModelClassification.MapRepresentation(null));
    }

    [Theory]
    [InlineData(T3DModelType.e3DModelType_Generic, true, null, null, ComponentModelSourceOrigin.Embedded)]
    [InlineData(T3DModelType.e3DModelType_Extruded, true, null, null, ComponentModelSourceOrigin.Embedded)]
    [InlineData(T3DModelType.e3DModelType_Generic, false, null, null, ComponentModelSourceOrigin.Unknown)]
    [InlineData(T3DModelType.e3DModelType_Extruded, false, null, null, ComponentModelSourceOrigin.Native)]
    [InlineData(T3DModelType.e3DModelType_Cylinder, false, null, null, ComponentModelSourceOrigin.Native)]
    [InlineData(T3DModelType.e3DModelType_Sphere, null, null, null, ComponentModelSourceOrigin.Native)]
    [InlineData(T3DModelType.e3DModelType_Extruded, false, TModelSource.eModelSource_File, "x.step", ComponentModelSourceOrigin.Unknown)]
    [InlineData(T3DModelType.e3DModelType_Extruded, false, null, "x.step", ComponentModelSourceOrigin.Unknown)]
    [InlineData(T3DModelType.e3DModelType_Generic, false, TModelSource.eModelsource_Vault, null, ComponentModelSourceOrigin.Unknown)]
    [InlineData((T3DModelType)999, false, null, null, ComponentModelSourceOrigin.Unknown)]
    public void ClassifiesSourceOrigin(
        T3DModelType modelType,
        bool? embed,
        TModelSource? modelSource,
        string? fileName,
        ComponentModelSourceOrigin expected)
    {
        Assert.Equal(
            expected,
            AltiumModelClassification.ClassifySourceOrigin(modelType, embed, modelSource, fileName));
    }

    [Fact]
    public void EmbedFalseIsNeverLinked()
    {
        foreach (var source in new TModelSource?[]
                 {
                     null,
                     TModelSource.eModelSource_Undefined,
                     TModelSource.eModelSource_File,
                     TModelSource.eModelsource_Vault,
                 })
        {
            Assert.NotEqual(
                ComponentModelSourceOrigin.Linked,
                AltiumModelClassification.ClassifySourceOrigin(
                    T3DModelType.e3DModelType_Generic,
                    false,
                    source,
                    "external.step"));
        }
    }
}
