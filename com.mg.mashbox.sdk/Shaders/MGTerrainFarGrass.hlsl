#ifndef MG_TERRAIN_FAR_GRASS_INCLUDED
#define MG_TERRAIN_FAR_GRASS_INCLUDED
TEXTURE2D(_MGFarGrassMap);
SAMPLER(sampler_MGFarGrass_linear_clamp);
float4 _MGFarGrassBounds;
float4 _MGFarGrassSettings;
float4x4 _MGFarGrassWorldToLocal;
void MGTerrainFarGrass_float(float3 BaseColor, float3 PositionOS, out float3 Out)
{
    Out = BaseColor;
#ifndef SHADERGRAPH_PREVIEW
    if (_MGFarGrassSettings.w > .5)
    {
        float3 world = GetAbsolutePositionWS(TransformObjectToWorld(PositionOS));
        float3 local = mul(_MGFarGrassWorldToLocal, float4(world, 1)).xyz;
        float2 uv = (local.xz - _MGFarGrassBounds.xy) * _MGFarGrassBounds.zw;
        float inside = all(uv >= 0) && all(uv <= 1) ? 1 : 0;
        float4 grass = SAMPLE_TEXTURE2D(_MGFarGrassMap, sampler_MGFarGrass_linear_clamp, uv);
        float fade = smoothstep(_MGFarGrassSettings.x, _MGFarGrassSettings.y, distance(world, _WorldSpaceCameraPos));
        Out = lerp(BaseColor, grass.rgb, saturate(grass.a * fade * _MGFarGrassSettings.z * inside));
    }
#endif
}
void MGTerrainFarGrass_half(half3 BaseColor, half3 PositionOS, out half3 Out)
{
    float3 result;
    MGTerrainFarGrass_float(BaseColor, PositionOS, result);
    Out = result;
}
#endif
