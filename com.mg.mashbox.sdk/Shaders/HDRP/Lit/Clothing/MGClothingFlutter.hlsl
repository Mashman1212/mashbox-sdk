#ifndef MG_CLOTHING_FLUTTER_INCLUDED
#define MG_CLOTHING_FLUTTER_INCLUDED
float4 _MGWindVelocity;
float4 _MGWindGust;
float3 MGFlutterOffset(float3 positionWS, float3 normalWS, float mask, float amplitude, float4 previewWind, float4 previewParams)
{
    float3 velocity = previewWind.w > .5 ? previewWind.xyz : _MGWindVelocity.xyz;
    float2 gust = previewWind.w > .5 ? previewParams.xy : _MGWindGust.xy;
    float time = previewWind.w > .5 ? previewParams.z : _Time.y;
    float speed = length(velocity);
    float strength = saturate(mask) * max(0, amplitude) * min(speed / 4.0, 3.0);
    float envelope = max(0, 1 + saturate(gust.x) * sin(time * max(0,gust.y) * 6.2831853));
    float3 direction = velocity / max(speed, .0001);
    float phase = dot(positionWS, float3(6.7,9.1,5.3));
    float wave = sin(phase + time * (8 + speed)) * .65 + sin(phase * 1.73 - time * 13.1) * .35;
    return (normalWS * wave + direction * (.25 + .25 * sin(phase*.7 + time*4))) * strength * envelope;
}
#ifndef MG_FLUTTER_BUILTIN
void MGClothingFlutter_float(float3 Position, float3 Normal, float4 Color, float Amplitude, float4 PreviewWind, float4 PreviewParams, out float3 Out)
{
#ifdef SHADERGRAPH_PREVIEW
    Out=Position;
#else
    float3 world = GetAbsolutePositionWS(TransformObjectToWorld(Position));
    float3 delta = MGFlutterOffset(world,TransformObjectToWorldNormal(Normal),Color.r,Amplitude,PreviewWind,PreviewParams);
    Out=Position+mul((float3x3)UNITY_MATRIX_I_M,delta);
#endif
}
#endif
#endif
