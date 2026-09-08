#ifndef MG_TRAIL_VERTEX_TOP_TWO_HEIGHT_INCLUDED
#define MG_TRAIL_VERTEX_TOP_TWO_HEIGHT_INCLUDED

// Match DualControlMapWeights' stable top-two selection before fetching heights.
// Explicit LOD is required in the tessellation/domain stage (no derivatives).
void MGTrailVertexTopTwoHeight_float(
    UnityTexture2D ControlMap1, UnityTexture2D ControlMap2,
    UnitySamplerState ControlSampler, UnityTexture2DArray HeightMapArray,
    UnitySamplerState HeightSampler, float2 UV0, float2 UV2, float UseUV2,
    float3 AbsolutePosition,
    float4 Tiling0, float4 Tiling1, float4 Tiling2, float4 Tiling3,
    float4 Tiling4, float4 Tiling5, float4 Tiling6, float4 Tiling7,
    float4 Planar0To3, float4 Planar4To7,
    float4 RemapMin0To3, float4 RemapMin4To7,
    float4 RemapMax0To3, float4 RemapMax4To7, out float Height)
{
    Height = 0.0;
    float2 controlUV = lerp(UV0, UV2, UseUV2);
    float4 weights0 = SAMPLE_TEXTURE2D_LOD(ControlMap1.tex, ControlSampler.samplerstate, controlUV, 0);
    float4 weights1 = SAMPLE_TEXTURE2D_LOD(ControlMap2.tex, ControlSampler.samplerstate, controlUV, 0);
    float weights[8] = { weights0.x, weights0.y, weights0.z, weights0.w,
                         weights1.x, weights1.y, weights1.z, weights1.w };
    int bestIndex = 0;
    int secondIndex = 1;
    float bestWeight = weights[0];
    float secondWeight = weights[1];
    if (secondWeight > bestWeight)
    {
        bestIndex = 1;
        secondIndex = 0;
        bestWeight = weights[1];
        secondWeight = weights[0];
    }
    [unroll] for (int i = 2; i < 8; i++)
    {
        float candidate = weights[i];
        if (candidate > bestWeight)
        {
            secondWeight = bestWeight;
            secondIndex = bestIndex;
            bestWeight = candidate;
            bestIndex = i;
        }
        else if (candidate > secondWeight)
        {
            secondWeight = candidate;
            secondIndex = i;
        }
    }
    // Empty control maps have no height contribution, regardless of remaps.
    [branch] if (bestWeight == 0.0 && secondWeight == 0.0)
    {
        Height = 0.0;
        return;
    }
    float selectedTotal = max(bestWeight + secondWeight, 0.00001);
    float4 tilings[8] = { Tiling0, Tiling1, Tiling2, Tiling3, Tiling4, Tiling5, Tiling6, Tiling7 };
    float planar[8] = { Planar0To3.x, Planar0To3.y, Planar0To3.z, Planar0To3.w,
                        Planar4To7.x, Planar4To7.y, Planar4To7.z, Planar4To7.w };
    float minimums[8] = { RemapMin0To3.x, RemapMin0To3.y, RemapMin0To3.z, RemapMin0To3.w,
                          RemapMin4To7.x, RemapMin4To7.y, RemapMin4To7.z, RemapMin4To7.w };
    float maximums[8] = { RemapMax0To3.x, RemapMax0To3.y, RemapMax0To3.z, RemapMax0To3.w,
                          RemapMax4To7.x, RemapMax4To7.y, RemapMax4To7.z, RemapMax4To7.w };
    // Same unshifted layer coordinates as PixelSampler8, without fragment POM.
    float2 planarUV = 1.0 - AbsolutePosition.xz * 0.1;
    // A constant remap does not need a texture fetch. Keep exact comparisons:
    // tiny nonzero painted weights still contribute exactly as before.
    float heightA = minimums[bestIndex];
    [branch] if (bestWeight != 0.0 && minimums[bestIndex] != maximums[bestIndex])
    {
        float2 uvA = lerp(UV0, planarUV, planar[bestIndex]) * tilings[bestIndex].xy;
        float sampleA = SAMPLE_TEXTURE2D_ARRAY_LOD(HeightMapArray.tex, HeightSampler.samplerstate, uvA, bestIndex, 0).r;
        heightA = lerp(minimums[bestIndex], maximums[bestIndex], sampleA);
    }
    float heightB = minimums[secondIndex];
    [branch] if (secondWeight != 0.0 && minimums[secondIndex] != maximums[secondIndex])
    {
        float2 uvB = lerp(UV0, planarUV, planar[secondIndex]) * tilings[secondIndex].xy;
        float sampleB = SAMPLE_TEXTURE2D_ARRAY_LOD(HeightMapArray.tex, HeightSampler.samplerstate, uvB, secondIndex, 0).r;
        heightB = lerp(minimums[secondIndex], maximums[secondIndex], sampleB);
    }
    Height = heightA * (bestWeight / selectedTotal) + heightB * (secondWeight / selectedTotal);
}
#endif
