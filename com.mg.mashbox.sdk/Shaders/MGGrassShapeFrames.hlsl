#ifndef MG_GRASS_SHAPE_FRAMES_INCLUDED
#define MG_GRASS_SHAPE_FRAMES_INCLUDED
#include "MGTerrainDetailData.hlsl"

// Opt-in: existing materials keep their original vertex frame without extra samples.
void MGGrassShapeFrame_float(float3 NormalOS, float3 TangentOS, float4 Lookup,
    UnityTexture2DArray Normals, UnityTexture2DArray Tangents,
    float UseFrames, float UseShapes, float DefaultLayer,
    out float3 ShapeNormalOS, out float3 ShapeTangentOS, out float TangentSign)
{
    TangentSign = 1;
    ShapeNormalOS = NormalOS;
    ShapeTangentOS = TangentOS;
    [branch] if (UseFrames > .5 && UseShapes > .5 && Lookup.z > .5)
    {
        float slice;
        MGTerrainDetailSlice_float(DefaultLayer, slice);
        slice = clamp(floor(slice + .5), 0, 7);
        float4 n = SAMPLE_TEXTURE2D_ARRAY_LOD(Normals.tex, Normals.samplerstate, Lookup.xy, slice, 0);
        float4 t = SAMPLE_TEXTURE2D_ARRAY_LOD(Tangents.tex, Tangents.samplerstate, Lookup.xy, slice, 0);
        if (n.w > .5 && dot(n.xyz,n.xyz) > 1e-8 && dot(t.xyz,t.xyz) > 1e-8)
        {
            ShapeNormalOS = normalize(n.xyz);
            ShapeTangentOS = normalize(t.xyz);
            // Lookup.w stores the base mesh tangent sign; fragment normal Y
            // compensates when this shape has the opposite UV handedness.
            TangentSign = t.w * Lookup.w < 0 ? -1 : 1;
        }
    }
}
#endif
