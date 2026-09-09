Shader "Hidden/MashBox/TerrainCaptureHeight"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            float4x4 _MGHeightWorldToLocal;
            struct Attributes { uint vertexID : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }
            float Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float depth = LoadCameraDepth(uint2(input.positionCS.xy));
                if (depth == UNITY_RAW_FAR_CLIP_VALUE) return -1e20;
                float3 positionWS = ComputeWorldSpacePosition(input.positionCS.xy * _ScreenSize.zw, depth, UNITY_MATRIX_I_VP);
                return mul(_MGHeightWorldToLocal, float4(GetAbsolutePositionWS(positionWS), 1)).y;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
