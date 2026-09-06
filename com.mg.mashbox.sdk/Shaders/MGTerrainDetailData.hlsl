#ifndef MG_TERRAIN_DETAIL_DATA_INCLUDED
#define MG_TERRAIN_DETAIL_DATA_INCLUDED

// Shader Graph Custom Function (File): MGTerrainDetailData, float precision.
// Do NOT also declare these reserved metadata names as Blackboard properties.
#if defined(UNITY_DOTS_INSTANCING_ENABLED) && !defined(SHADERGRAPH_PREVIEW)
UNITY_DOTS_INSTANCING_START(MGTerrainDetailMetadata)
    UNITY_DOTS_INSTANCED_PROP_OVERRIDE_SUPPORTED(float4, _MGDetailInstance)
    UNITY_DOTS_INSTANCED_PROP_OVERRIDE_SUPPORTED(float4, _MGDetailTable)
UNITY_DOTS_INSTANCING_END(MGTerrainDetailMetadata)
#endif

void MGTerrainDetailData_float(out float DefinitionIndex, out float TextureSlice,
    out float4 Tint, out float WindMultiplier, out float RandomValue, out float HasDetailData)
{
    DefinitionIndex = 0;
    TextureSlice = 0;
    Tint = float4(1, 1, 1, 1);
    WindMultiplier = 1;
    RandomValue = 0;
    HasDetailData = 0;
#if defined(UNITY_DOTS_INSTANCING_ENABLED) && !defined(SHADERGRAPH_PREVIEW)
    uint tableAddress = UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _MGDetailTable) & 0x7fffffffu;
    uint instanceAddress = UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _MGDetailInstance);
    if (tableAddress != 0 && instanceAddress != 0)
    {
        float4 instance = UNITY_ACCESS_DOTS_INSTANCED_PROP(float4, _MGDetailInstance);
        uint count = (uint)asfloat(unity_DOTSInstanceData.Load(tableAddress));
        uint index = (uint)max(instance.x, 0);
        if (index < count)
        {
            uint address = tableAddress + 16 + index * 32;
            Tint = asfloat(unity_DOTSInstanceData.Load4(address));
            float4 definition = asfloat(unity_DOTSInstanceData.Load4(address + 16));
            DefinitionIndex = index;
            TextureSlice = definition.x;
            WindMultiplier = definition.y;
            RandomValue = instance.y;
            HasDetailData = 1;
        }
    }
#endif
}
#endif
