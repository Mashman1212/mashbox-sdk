#ifndef MG_SURFACE_ARRAYS_INCLUDED
#define MG_SURFACE_ARRAYS_INCLUDED
float4 MGSurfaceRead(UnityTexture2D Parameters, float SurfaceID, float Column)
{
    uint width, height; Parameters.tex.GetDimensions(width, height);
    int row = clamp((int)round(SurfaceID), 0, max((int)height - 1, 0));
    return Parameters.tex.Load(int3(clamp((int)Column, 0, (int)width - 1), row, 0));
}
void MGSurfaceFloat_float(UnityTexture2D Parameters, float SurfaceID, float Column, out float Out)
{ Out = MGSurfaceRead(Parameters, SurfaceID, Column).x; }
void MGSurfaceColor_float(UnityTexture2D Parameters, float SurfaceID, float Column, out float4 Out)
{ Out = MGSurfaceRead(Parameters, SurfaceID, Column); }
void MGSurfaceBool_float(UnityTexture2D Parameters, float SurfaceID, float Column, out bool Out)
{ Out = MGSurfaceRead(Parameters, SurfaceID, Column).x > .5; }
void MGSurfaceSample_float(UnityTexture2DArray Layers, float2 UV, UnityTexture2D Parameters,
    float SurfaceID, float Channel, float STColumn, float IsNormal,
    out float4 RGBA, out float R, out float G, out float B, out float A)
{
    uint channel = (uint)Channel;
    float slice = MGSurfaceRead(Parameters, SurfaceID, channel < 4 ? 0 : 1)[channel % 4];
    float4 st = MGSurfaceRead(Parameters, SurfaceID, STColumn);
    RGBA = SAMPLE_TEXTURE2D_ARRAY(Layers.tex, Layers.samplerstate, UV * st.xy + st.zw, slice);
    if (IsNormal > .5) RGBA.xyz = normalize(RGBA.xyz * 2 - 1);
    R = RGBA.r; G = RGBA.g; B = RGBA.b; A = RGBA.a;
}
// Match God Grass: zero strength preserves texture AO exactly.
void MGSurfaceVertexAO_float(float TextureAO, float4 VertexColor, float Strength, out float CombinedAO)
{
    CombinedAO = saturate(TextureAO - (1 - saturate(VertexColor.r)) * saturate(Strength));
}
float4 MGGeometryLoad(UnityTexture2D Atlas, uint index)
{
    uint width, height; Atlas.tex.GetDimensions(width,height);
    // Geometry baker uses power-of-two widths. Avoid integer division for every vertex fetch.
    return Atlas.tex.Load(int3(index & (width - 1), index >> firstbithigh(width), 0));
}
void MGSurfaceGeometry_float(UnityTexture2D Vertices, UnityTexture2D Indices, UnityTexture2D Chunks, float GeometryID,
    float3 SourcePosition, float3 SourceNormal, float3 SourceTangent,
    float2 SourceUV0, float2 SourceUV1, float2 SourceUV2, float2 SourceUV3, float4 SourceColor,
    out float3 Position, out float3 Normal, out float3 Tangent, out float4 UV01, out float4 UV23, out float2 AOSign)
{
    Position=SourcePosition; Normal=SourceNormal; Tangent=SourceTangent;
    UV01=float4(SourceUV0,SourceUV1); UV23=float4(SourceUV2,SourceUV3); AOSign=float2(SourceColor.r,1);
    [branch] if (GeometryID < 0) return;
    float4 piece=MGGeometryLoad(Chunks,(uint)round(GeometryID));
    uint corner=(uint)round(SourceUV0.x);
    // Padding needs no index or attribute fetches. All its triangles collapse to zero.
    [branch] if (corner >= (uint)piece.y)
    { Position=0; Normal=float3(0,1,0); Tangent=float3(1,0,0); UV01=0; UV23=0; AOSign=float2(1,1); return; }
    uint vertex=(uint)round(MGGeometryLoad(Indices,(uint)piece.x+corner).r)*5;
    float4 p=MGGeometryLoad(Vertices,vertex), t=MGGeometryLoad(Vertices,vertex+2);
    Position=p.xyz; Normal=MGGeometryLoad(Vertices,vertex+1).xyz; Tangent=t.xyz;
    UV01=MGGeometryLoad(Vertices,vertex+3); UV23=MGGeometryLoad(Vertices,vertex+4); AOSign=float2(p.w,t.w);
}
void MGSurfaceUV_float(float4 UV01,float4 UV23,float Channel,out float2 Out)
{ Out=Channel<.5?UV01.xy:Channel<1.5?UV01.zw:Channel<2.5?UV23.xy:UV23.zw; }
void MGSurfaceAOColor_float(float2 AOSign,out float4 Color) { Color=float4(AOSign.x,1,1,1); }
void MGSurfaceTangentSign_float(float3 Normal,float2 AOSign,out float3 Out)
{ Out=Normal*float3(1,AOSign.y<0?-1:1,1); }
#endif
