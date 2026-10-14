#ifndef MG_GRASS_SHAPES_INCLUDED
#define MG_GRASS_SHAPES_INCLUDED
#include "MGTerrainDetailData.hlsl"

// Lookup is baked into UV2; z protects ordinary meshes sharing the shader.
// Both textures contain uncompressed, linear, point-filtered data without mips.
void MGGrassShape_float(float3 PositionOS, float2 UV, float4 Lookup,
    UnityTexture2DArray Positions, UnityTexture2DArray UVs,
    float UseShapes, float DefaultLayer, out float3 ShapePositionOS, out float2 ShapeUV)
{
    ShapePositionOS = PositionOS;
    ShapeUV = UV;
    [branch] if (UseShapes > 0.5 && Lookup.z > 0.5)
    {
        float slice;
        MGTerrainDetailSlice_float(DefaultLayer, slice);
        slice = clamp(floor(slice + 0.5), 0, 7);
        float4 position = SAMPLE_TEXTURE2D_ARRAY_LOD(Positions.tex, Positions.samplerstate, Lookup.xy, slice, 0);
        float4 uv = SAMPLE_TEXTURE2D_ARRAY_LOD(UVs.tex, UVs.samplerstate, Lookup.xy, slice, 0);
        if (position.w > 0.5 && uv.w > 0.5)
        {
            ShapePositionOS = position.xyz;
            ShapeUV = uv.xy;
        }
    }
}
#endif
