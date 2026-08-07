Shader "Hidden/MashBox/MGLitTrailArrayPack"
{
    Properties
    {
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _MaskMap ("Mask Map", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "UnityCG.cginc"

        sampler2D _NormalMap;
        sampler2D _MaskMap;

        struct Attributes
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = UnityObjectToClipPos(input.vertex);
            output.uv = input.uv;
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Pack Height"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHeight

            float4 FragHeight(Varyings input) : SV_Target
            {
                float height = tex2D(_MaskMap, input.uv).b;
                return height.xxxx;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Pack Surface"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSurface

            float4 FragSurface(Varyings input) : SV_Target
            {
                float3 normalTS = UnpackNormal(tex2D(_NormalMap, input.uv));
                float4 mask = tex2D(_MaskMap, input.uv);
                return float4(normalTS.xy * 0.5 + 0.5, mask.g, mask.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
