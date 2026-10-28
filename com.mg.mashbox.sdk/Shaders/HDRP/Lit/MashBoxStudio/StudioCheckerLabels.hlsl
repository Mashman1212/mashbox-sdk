#ifndef MASHBOX_STUDIO_CHECKER_LABELS_INCLUDED
#define MASHBOX_STUDIO_CHECKER_LABELS_INCLUDED
// Original 5x7 glyphs, bottom row first, in two uints. No font texture fetch.
uint2 StudioTileGlyph(int glyph)
{
    switch (glyph)
    {
        case 0: return uint2(2744831534u,3u);
        case 1: return uint2(205656206u,1u);
        case 2: return uint2(2734952543u,3u);
        case 3: return uint2(3775349263u,3u);
        case 4: return uint2(413465864u,2u);
        case 5: return uint2(3256336911u,7u);
        case 6: return uint2(2182596142u,3u);
        case 7: return uint2(3766618178u,7u);
        case 8: return uint2(2736211502u,3u);
        case 9: return uint2(2736734734u,3u);
        case 10: return uint2(1662567982u,4u);
        case 11: return uint2(1662567748u,4u);
        case 12: return uint2(1015808u,0u);
        default: return uint2(0u,0u);
    }
}
float StudioTileBit(uint2 mask, int index)
{
    return (float)(((index < 32 ? mask.x : mask.y) >> (uint)(index % 32)) & 1u);
}
int StudioTileDigits(int value)
{
    return value >= 1000 ? 4 : value >= 100 ? 3 : value >= 10 ? 2 : 1;
}
int StudioTileNumberGlyph(int value, int column, int digits)
{
    if (value < 0) { if (column == 0) return 12; column--; }
    int power = digits - column - 1;
    int divisor = power == 3 ? 1000 : power == 2 ? 100 : power == 1 ? 10 : 1;
    return (abs(value) / divisor) % 10;
}
// Fragment-only. Raw UV0 before tiling: tile [0,1)x[0,1) is U1 V1.
// No separator between letter and number. Minus is used only for negative values.
void StudioCheckerLabels_float(float2 UV, float3 CheckerColor, out float3 Out)
{
    Out = CheckerColor;
    float2 footprint = max(fwidth(UV), 0.000001);
    float2 local = frac(UV);
    [branch] if (abs(local.y - 0.5) > 0.13) return;
    float2 tile = floor(UV) + 1.0;
    if (any(abs(tile) > 9999.0)) return;
    int u = (int)tile.x, v = (int)tile.y;
    int du = StudioTileDigits(abs(u)), dv = StudioTileDigits(abs(v));
    int nu = du + (u < 0 ? 1 : 0), nv = dv + (v < 0 ? 1 : 0);
    const float pitch = 5.6; // 5 glyph pixels plus a tight 0.6-pixel gap.
    const float groupGap = 1.2; // Additional space between complete U/V groups only.
    float vStart = (1 + nu) * pitch + groupGap;
    float width = vStart + (1 + nv) * pitch - 0.6;
    float pixel = min(0.035, 0.9 / width); // Larger text; long coordinates still fit.

    float fade = saturate(pixel / max(footprint.x, footprint.y) - 1.0);
    float2 grid = (local - 0.5) / pixel + float2(width * 0.5, 3.5);
    [branch] if (fade <= 0 || grid.x < 0 || grid.x >= width || grid.y < 0 || grid.y >= 7) return;
    bool isV = grid.x >= vStart;
    float x = grid.x - (isV ? vStart : 0);
    int character = (int)floor(x / pitch);
    int glyph = character == 0 ? (isV ? 11 : 10) : StudioTileNumberGlyph(isV ? v : u, character-1, isV ? dv : du);
    float2 cell = float2(x - character * pitch, grid.y);
    int2 bit = (int2)floor(cell);
    if (bit.x >= 5 || (!isV && character > nu)) return;
    uint2 mask = StudioTileGlyph(glyph);
    int index = bit.y * 5 + bit.x;
    float on = StudioTileBit(mask,index);
    float2 q = frac(cell);
    float left = bit.x > 0 ? StudioTileBit(mask,index-1) : 0;
    float right = bit.x < 4 ? StudioTileBit(mask,index+1) : 0;
    float below = bit.y > 0 ? StudioTileBit(mask,index-5) : 0;
    float above = bit.y < 6 ? StudioTileBit(mask,index+5) : 0;
    float2 edge = min(q + float2(left,below), 1.0-q + float2(right,above));
    float2 aa = max(footprint / pixel,0.001);
    float ink = on * saturate(edge.x / aa.x + 0.5) * saturate(edge.y / aa.y + 0.5) * fade;
    Out = lerp(CheckerColor, float3(1,1,1), ink);
}
void StudioCheckerLabels_half(half2 UV, half3 CheckerColor, out half3 Out)
{
    float3 result; StudioCheckerLabels_float(UV, CheckerColor, result); Out = (half3)result;
}
#endif
