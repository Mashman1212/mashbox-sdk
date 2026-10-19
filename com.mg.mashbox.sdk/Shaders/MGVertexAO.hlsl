#ifndef MG_VERTEX_AO_INCLUDED
#define MG_VERTEX_AO_INCLUDED
#include "MGTerrainDetailData.hlsl"
// Vertex-stage lookup, then interpolate AO itself across the rendered triangle.
void MGGrassShapeAO_float(float4 VertexColor, float4 Lookup, UnityTexture2DArray AO,
    float UseAO, float UseShapes, float DefaultLayer, out float VertexAO)
{
    VertexAO=saturate(VertexColor.r);
    [branch] if(UseAO>.5 && UseShapes>.5 && Lookup.z>.5)
    {
        float slice; MGTerrainDetailSlice_float(DefaultLayer,slice);
        slice=clamp(floor(slice+.5),0,7);
        VertexAO=saturate(SAMPLE_TEXTURE2D_ARRAY_LOD(AO.tex,AO.samplerstate,Lookup.xy,slice,0).r);
    }
}
// Add occlusion, not brightness. Zero strength is exactly the original texture AO.
void MGVertexAO_float(float TextureAO,float VertexAO,float Strength,out float CombinedAO)
{
    CombinedAO=saturate(TextureAO-(1-saturate(VertexAO))*saturate(Strength));
}
#endif
