Shader "Hidden/MashBox/SkinWeightPreview"
{
    HLSLINCLUDE
    #include "UnityCG.cginc"
    struct Input { float4 vertex:POSITION; float3 normal:NORMAL; float4 color:COLOR; };
    struct Output { float4 vertex:SV_POSITION; float3 normal:TEXCOORD0; float4 color:COLOR; };
    Output Vert(Input i) { Output o; o.vertex=UnityObjectToClipPos(i.vertex); o.normal=UnityObjectToWorldNormal(i.normal); o.color=i.color; return o; }
    float4 Frag(Output i):SV_Target { return float4(i.color.rgb*(.5+.5*abs(dot(normalize(i.normal),normalize(float3(.3,.7,.5))))),1); }
    ENDHLSL
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="ForwardOnly" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
