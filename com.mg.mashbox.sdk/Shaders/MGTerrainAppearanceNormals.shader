Shader "Hidden/MashBox/TerrainCaptureNormals"
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
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"
            TEXTURE2D_X(_MGCaptureNormalBuffer);
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
            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                uint2 pixel = uint2(input.positionCS.xy);
                NormalData data;
                DecodeFromNormalBuffer(LOAD_TEXTURE2D_X(_MGCaptureNormalBuffer, pixel), data);
                float3 normalWS = normalize(data.normalWS);
                // No surface: encode world up, not an invalid black normal.
                if (LoadCameraDepth(pixel) == UNITY_RAW_FAR_CLIP_VALUE) normalWS = float3(0, 1, 0);
                return float4(normalWS * 0.5 + 0.5, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
