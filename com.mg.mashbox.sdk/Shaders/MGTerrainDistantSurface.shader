Shader "MashBox/Terrain Distant Surface"
{
    Properties
    {
        _BaseMap("Baked Appearance", 2D) = "white" {}
        _FadeStart("Fade In Start (Metres)", Float) = 300
        _FadeEnd("Fade In End (Metres)", Float) = 400
        _Brightness("Brightness", Range(0, 4)) = 1
    }
    HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float _FadeStart, _FadeEnd, _Brightness;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionRWS : TEXCOORD0; float2 uv : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionRWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionRWS);
                output.uv = input.uv;
                return output;
            }
            void ApplyFade(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float d = distance(GetAbsolutePositionWS(input.positionRWS), _WorldSpaceCameraPos);
                float coverage = saturate((d - _FadeStart) / max(1, _FadeEnd - _FadeStart));
                float noise = frac(52.9829189 * frac(dot(floor(input.positionCS.xy), float2(0.06711056, 0.00583715))));
                clip(coverage - (noise * 254 + 0.5) / 255);
            }
            float4 FragDepth(Varyings input) : SV_Target
            {
                ApplyFade(input);
                return 0;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ApplyFade(input);
                // Colour already contains capture lighting and exposure. Do not light it a second time.
                return float4(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _Brightness, 1);
            }
    ENDHLSL
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "AlphaTest+10" }
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Cull Back ZWrite On ZTest LEqual ColorMask 0
            Stencil { Ref 0 WriteMask 8 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Back ZWrite On ZTest LEqual
            Stencil { Ref 0 WriteMask 3 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    Fallback Off
}
