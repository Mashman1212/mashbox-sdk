#ifndef MG_TERRAIN_DETAIL_DATA_INCLUDED
#define MG_TERRAIN_DETAIL_DATA_INCLUDED

// Shader Graph Custom Function (File): MGTerrainDetailData, float precision.
// Do NOT also declare these reserved metadata names as Blackboard properties.
#if defined(UNITY_DOTS_INSTANCING_ENABLED) && !defined(SHADERGRAPH_PREVIEW)
// BRG binds only BuiltinPropertyMetadata, MaterialPropertyMetadata, or UserPropertyMetadata.
UNITY_DOTS_INSTANCING_START(UserPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP_OVERRIDE_SUPPORTED(float4, _MGDetailInstance)
    UNITY_DOTS_INSTANCED_PROP_OVERRIDE_SUPPORTED(float4, _MGDetailTable)
UNITY_DOTS_INSTANCING_END(UserPropertyMetadata)
#elif !defined(SHADERGRAPH_PREVIEW)
UNITY_INSTANCING_BUFFER_START(MGTerrainClassicDetailMetadata)
    UNITY_DEFINE_INSTANCED_PROP(float4, _MGDetailInstance)
UNITY_INSTANCING_BUFFER_END(MGTerrainClassicDetailMetadata)
float4 _MGDetailFadeRanges;
float4 _MGDetailFadeDensities;
#endif

// Connected after GodGrass's authored alpha logic, fragment stage only.
// z = stable population rank, w = overall density + 1 (zero = no fade data).
// Coverage is evaluated in the shader each frame, avoiding per-frame transform
// rebuilds. A stable UV-space pattern also works in shadow/depth passes.
void MGTerrainDetailFade_float(float InAlpha, float2 UV, out float OutAlpha, out float InstanceFade)
{
    OutAlpha = InAlpha;
    InstanceFade = 1;
#if !defined(SHADERGRAPH_PREVIEW)
    float4 instance = 0;
    float4 ranges = 0;
    float2 densities = 0;
#if defined(UNITY_DOTS_INSTANCING_ENABLED)
    uint tableAddress = UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _MGDetailTable) & 0x7fffffffu;
    uint instanceAddress = UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _MGDetailInstance);
    if (tableAddress != 0 && instanceAddress != 0)
    {
        instance = UNITY_ACCESS_DOTS_INSTANCED_PROP(float4, _MGDetailInstance);
        float4 header = asfloat(unity_DOTSInstanceData.Load4(tableAddress));
        uint index = (uint)max(instance.x, 0);
        if (index < (uint)header.x)
        {
            ranges = float4(header.yzw, 0);
            densities = asfloat(unity_DOTSInstanceData.Load4(tableAddress + 32 + index * 32)).zw;
        }
    }
#else
    instance = UNITY_ACCESS_INSTANCED_PROP(MGTerrainClassicDetailMetadata, _MGDetailInstance);
    ranges = _MGDetailFadeRanges;
    densities = _MGDetailFadeDensities.xy;
#endif
    if (instance.w > 0 && ranges.z > 0)
    {
        float3 originWS = GetAbsolutePositionWS(TransformObjectToWorld(float3(0, 0, 0)));
        float distanceToCamera = distance(originWS, _WorldSpaceCameraPos);
        float overall = saturate(instance.w - 1);
        float nearCoverage = step(instance.z, overall);
        float midCoverage = step(instance.z, overall * saturate(densities.x));
        float farCoverage = step(instance.z, overall * saturate(densities.y));
        float halfWidth = ranges.z * 0.5;
        float nearToMid = smoothstep(ranges.x - halfWidth, ranges.x + halfWidth, distanceToCamera);
        float midToFar = smoothstep(ranges.y - halfWidth, ranges.y + halfWidth, distanceToCamera);
        InstanceFade = lerp(lerp(nearCoverage, midCoverage, nearToMid), farCoverage, midToFar);
        // Only the transition band needs per-pixel dithering.
        [branch] if (InstanceFade >= 1) return;
        [branch] if (InstanceFade <= 0) { clip(-1); return; }
        float2 cell = floor(UV * 1024);
        float noise = frac(sin(dot(cell, float2(12.9898, 78.233)) + dot(originWS.xz, float2(3.17, 7.13))) * 43758.5453);
        // Strictly inside (0,1): fully hidden/visible instances are exact.
        noise = (noise * 254 + 0.5) / 255;
        clip(InstanceFade - noise);
    }
#endif
}

void MGTerrainDetailFade_half(half InAlpha, half2 UV, out half OutAlpha, out half InstanceFade)
{
    float alpha, fade;
    MGTerrainDetailFade_float(InAlpha, UV, alpha, fade);
    OutAlpha = alpha;
    InstanceFade = fade;
}

// Texture-array sampling needs only the instance slice, not the tint/wind table.
void MGTerrainDetailSlice_float(float DefaultSlice, out float Slice)
{
    Slice = DefaultSlice;
#if defined(UNITY_DOTS_INSTANCING_ENABLED) && !defined(SHADERGRAPH_PREVIEW)
    if (UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _MGDetailInstance) != 0)
    {
        float encoded = UNITY_ACCESS_DOTS_INSTANCED_PROP(float4, _MGDetailInstance).y;
        if (encoded >= 1) Slice = floor(encoded) - 1;
    }
#elif !defined(SHADERGRAPH_PREVIEW)
    float encoded = UNITY_ACCESS_INSTANCED_PROP(MGTerrainClassicDetailMetadata, _MGDetailInstance).y;
    if (encoded >= 1) Slice = floor(encoded) - 1;
#endif
}
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
            TextureSlice = instance.y >= 1 ? floor(instance.y) - 1 : definition.x;
            WindMultiplier = definition.y;
            RandomValue = frac(instance.y);
            HasDetailData = 1;
        }
    }
#elif !defined(SHADERGRAPH_PREVIEW)
    float4 instance = UNITY_ACCESS_INSTANCED_PROP(MGTerrainClassicDetailMetadata, _MGDetailInstance);
    if (instance.y > 0)
    {
        DefinitionIndex = instance.x;
        TextureSlice = instance.y - 1;
        HasDetailData = 1;
    }
#endif
}
#endif
