Shader "Hidden/MashBox/AnimationStudioPreview"
{
    Properties
    {
        _MainTex ("Base texture", 2D) = "white" {}
        _BaseArray ("Base texture array", 2DArray) = "" {}
        _NormalArray ("Normal texture array", 2DArray) = "" {}
        _NormalTex ("Normal texture", 2D) = "bump" {}
        _UseArray ("Use array", Float) = 0
        _ArraySlice ("Array slice", Float) = 0
        _UseNormal ("Normal source", Float) = 0
        _NormalStrength ("Normal strength", Float) = 1
        _Brightness ("Brightness", Float) = 1
        _Contrast ("Contrast", Float) = 1
        _Saturation ("Saturation", Float) = 1
        _Color ("Tint", Color) = (0.76, 0.84, 0.91, 1)
        _VertexAOStrength ("Vertex AO Strength", Range(0,1)) = 0
        _Unlit ("Unlit preview", Float) = 0
        _CameraLighting ("Camera lighting", Float) = 0
        _Opacity ("Preview opacity", Range(0, 1)) = 1
        [HideInInspector] _BlackBackfaces ("Black back faces", Float) = 0
        [HideInInspector] _ZWrite ("Depth write", Float) = 1
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 0
        [HideInInspector] _ColorMask ("Color write mask", Float) = 15
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
        HLSLINCLUDE
            #include "UnityCG.cginc"
            sampler2D _MainTex, _NormalTex;
            UNITY_DECLARE_TEX2DARRAY(_BaseArray);
            UNITY_DECLARE_TEX2DARRAY(_NormalArray);
            float _UseArray, _ArraySlice, _UseNormal, _NormalStrength;
            float _Brightness, _Contrast, _Saturation;
            float4 _MainTex_ST;
            float4 _Color;
            float _Cutoff;
            float _Unlit, _VertexAOStrength;
            float _CameraLighting;
            float _Opacity, _BlackBackfaces;
            struct Attributes { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; float3 normal : TEXCOORD0; float2 uv : TEXCOORD1; float4 tangent : TEXCOORD2; float ao : TEXCOORD3; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.tangent = float4(UnityObjectToWorldDir(input.tangent.xyz), input.tangent.w * unity_WorldTransformParams.w);
                output.ao = input.color.r;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }
            float4 Frag(Varyings input, float facing : VFACE) : SV_Target
            {
                float3 normal = normalize(input.normal);
                if (_UseNormal > 0.5 && dot(input.tangent.xyz,input.tangent.xyz) > 0.01)
                {
                    float3 detail;
                    if (_UseNormal > 1.5) detail = UNITY_SAMPLE_TEX2DARRAY(_NormalArray,float3(input.uv,_ArraySlice)).xyz * 2 - 1;
                    else detail = UnpackNormal(tex2D(_NormalTex,input.uv));
                    detail.xy *= _NormalStrength;
                    float3 tangent = normalize(input.tangent.xyz-normal*dot(input.tangent.xyz,normal));
                    float3 bitangent = cross(normal,tangent)*input.tangent.w;
                    normal = normalize(tangent*detail.x+bitangent*detail.y+normal*detail.z);
                }
                normal *= (facing >= 0 ? 1 : -1);
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
                float4 albedo;
                if (_UseArray > 0.5) albedo = UNITY_SAMPLE_TEX2DARRAY(_BaseArray,float3(input.uv,_ArraySlice));
                else albedo = tex2D(_MainTex, input.uv);
                albedo *= _Color;
                float luminance = dot(albedo.rgb,float3(.2126,.7152,.0722));
                albedo.rgb = (lerp(luminance.xxx,albedo.rgb,_Saturation)-.5)*_Contrast+.5;
                albedo.rgb *= _Brightness;
                clip(albedo.a - _Cutoff);
                if (_BlackBackfaces > .5 && facing < 0) return float4(0, 0, 0, _Opacity);
                return float4(saturate(albedo.rgb) * lerp(shade, 1, _Unlit) * saturate(1-(1-input.ao)*_VertexAOStrength), _Opacity);
            }
        ENDHLSL
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Tags { "LightMode" = "ForwardOnly" }
            ZWrite [_ZWrite]
            ColorMask [_ColorMask]
            Blend [_SrcBlend] [_DstBlend]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
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
            ZWrite [_ZWrite]
            ColorMask [_ColorMask]
            Blend [_SrcBlend] [_DstBlend]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            ENDHLSL
        }
    }
    Fallback "Unlit/Color"
}
