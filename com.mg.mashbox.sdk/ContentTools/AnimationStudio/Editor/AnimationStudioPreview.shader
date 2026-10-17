Shader "Hidden/MashBox/AnimationStudioPreview"
{
    Properties
    {
        _MainTex ("Base texture", 2D) = "white" {}
        _Color ("Tint", Color) = (0.76, 0.84, 0.91, 1)
        _Unlit ("Unlit preview", Float) = 0
        _CameraLighting ("Camera lighting", Float) = 0
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
        HLSLINCLUDE
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Cutoff;
            float _Unlit;
            float _CameraLighting;
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
                if (_CameraLighting > 0.5)
                {
                    // View-space lights follow an orbit without changing model/material data.
                    float3 viewNormal = normalize(mul((float3x3)UNITY_MATRIX_V, normal));
                    key = saturate(dot(viewNormal, normalize(float3(-0.35, 0.45, 1))));
                    fill = saturate(dot(viewNormal, normalize(float3(0.65, -0.25, 1))));
                    shade = 0.25 + key * 0.55 + fill * 0.15;
                }
                float4 albedo = tex2D(_MainTex, input.uv) * _Color;
                clip(albedo.a - _Cutoff);
                return float4(saturate(albedo.rgb) * lerp(shade, 1, _Unlit), 1);
            }
        ENDHLSL
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Tags { "LightMode" = "ForwardOnly" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            // Built-in rendering does not use HDRP's ForwardOnly pass.
            Tags { "LightMode" = "Always" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            ENDHLSL
        }
    }
    Fallback "Unlit/Color"
}
