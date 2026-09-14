Shader "Hidden/MashBox/AnimationStudioPreview"
{
    Properties
    {
        _MainTex ("Base texture", 2D) = "white" {}
        _Color ("Tint", Color) = (0.76, 0.84, 0.91, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "ForwardOnly" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Cutoff;
            struct Attributes { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float3 normal : TEXCOORD0; float2 uv : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }
            float4 Frag(Varyings input, float facing : VFACE) : SV_Target
            {
                float3 normal = normalize(input.normal) * (facing >= 0 ? 1 : -1);
                float key = saturate(dot(normal, normalize(float3(-0.5, 0.8, -0.7))));
                float fill = saturate(dot(normal, normalize(float3(0.7, 0.3, 0.5))));
                float shade = 0.16 + key * 0.60 + fill * 0.16;
                float4 albedo = tex2D(_MainTex, input.uv) * _Color;
                clip(albedo.a - _Cutoff);
                return float4(saturate(albedo.rgb) * shade, 1);
            }
            ENDHLSL
        }
    }
    Fallback "Unlit/Color"
}
