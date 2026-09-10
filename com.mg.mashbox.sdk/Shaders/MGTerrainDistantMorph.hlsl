#ifndef MG_TERRAIN_DISTANT_MORPH_INCLUDED
#define MG_TERRAIN_DISTANT_MORPH_INCLUDED

// Encoding 2: R16 normalized upward delta, including valid zero (no lift).
// Encoding 1: legacy R16 uses 0 for missing surface, 1..65535 for local Y.
// Decode = (minimum, range, encoding: 2=delta R16/1=absolute R16/0=absolute RFloat, reserved).
// Filter valid samples manually so an empty pixel cannot pull a canopy downwards.
float2 MGDistantHeight(UnityTexture2D map, float2 uv, float4 decode)
{
    int2 size = int2(map.texelSize.zw);
    float2 pixel = clamp(uv * size - 0.5, 0, size - 1);
    int2 basePixel = int2(floor(pixel));
    float2 f = frac(pixel);
    float sum = 0, weight = 0;
    [unroll] for (int y = 0; y < 2; y++)
    [unroll] for (int x = 0; x < 2; x++)
    {
        float h = LOAD_TEXTURE2D_LOD(map.tex, min(basePixel + int2(x,y), size - 1), 0).r;
        float w = (x == 0 ? 1-f.x : f.x) * (y == 0 ? 1-f.y : f.y);
        bool valid = decode.z > 1.5 ? true : decode.z > .5 ? h > .5 / 65535.0 : h > -1e19 && h < 1e19;
        if (valid)
        {
            if (decode.z > 1.5) h *= decode.y;
            else if (decode.z > .5) h = decode.x + saturate((h * 65535.0 - 1.0) / 65534.0) * decode.y;
            sum += h*w; weight += w;
        }
    }
    return float2(sum / max(weight, 1e-8), weight > 0 ? 1 : 0);
}

void MGDistantSurfaceMorph_float(float3 PositionWS, float3 DisplacementWS, UnityTexture2D HeightMap,
    float4 BoundsXZ, float Strength, float Start, float End, float4 HeightDecode, out float3 Displacement, out float Blend)
{
    Displacement = DisplacementWS;
    Blend = 0;
#if !defined(SHADERGRAPH_PREVIEW)
    if (Strength <= 0 || any(BoundsXZ.zw <= 0)) return;
    float3 positionOS = TransformWorldToObject(GetCameraRelativePositionWS(PositionWS));
    float2 uv = (positionOS.xz - BoundsXZ.xy) / BoundsXZ.zw;
    if (any(uv < 0) || any(uv > 1)) return;
    // Horizontal distance stays stable as the terrain rises; all passes use the view camera.
    Blend = saturate(Strength) * smoothstep(Start, max(Start + 1, End), distance(PositionWS.xz, _WorldSpaceCameraPos.xz));
    if (Blend <= 0) return;
    float2 sample = MGDistantHeight(HeightMap, uv, HeightDecode);
    Blend *= sample.y;
    if (sample.y == 0) return;
    // HDRP's Tessellation Displacement block expects a WORLD-space offset.
    float3 displacementOS = mul((float3x3)GetWorldToObjectMatrix(), DisplacementWS);
    float targetOffset = HeightDecode.z > 1.5 ? sample.x : sample.x - positionOS.y;
    float raise = max(0, targetOffset - displacementOS.y);
    Displacement += mul((float3x3)GetObjectToWorldMatrix(), float3(0, raise * Blend, 0));
#endif
}
// Vertex Position is object-space; keep the morph off the tessellation displacement path.
void MGDistantSurfaceVertexMorph_float(float3 PositionWS, float3 DisplacementWS, UnityTexture2D HeightMap,
    float4 BoundsXZ, float Strength, float Start, float End, float4 HeightDecode, out float3 Displacement, out float Blend)
{
    MGDistantSurfaceMorph_float(PositionWS, DisplacementWS, HeightMap, BoundsXZ, Strength, Start, End,
        HeightDecode, Displacement, Blend);
#if !defined(SHADERGRAPH_PREVIEW)
    Displacement = mul((float3x3)GetWorldToObjectMatrix(), Displacement);
#endif
}
#endif
