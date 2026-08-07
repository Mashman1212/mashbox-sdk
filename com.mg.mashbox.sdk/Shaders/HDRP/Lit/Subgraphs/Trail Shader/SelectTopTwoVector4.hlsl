#ifndef SELECT_TOP_TWO_VECTOR4_INCLUDED
#define SELECT_TOP_TWO_VECTOR4_INCLUDED

void SelectTopTwoVector4_float(
    float IndexA,
    float IndexB,
    float4 Value0,
    float4 Value1,
    float4 Value2,
    float4 Value3,
    float4 Value4,
    float4 Value5,
    float4 Value6,
    float4 Value7,
    out float4 SelectedA,
    out float4 SelectedB)
{
    float4 values[8];

    values[0] = Value0;
    values[1] = Value1;
    values[2] = Value2;
    values[3] = Value3;
    values[4] = Value4;
    values[5] = Value5;
    values[6] = Value6;
    values[7] = Value7;

    int indexA = clamp((int)round(IndexA), 0, 7);
    int indexB = clamp((int)round(IndexB), 0, 7);

    SelectedA = values[indexA];
    SelectedB = values[indexB];
}

#endif