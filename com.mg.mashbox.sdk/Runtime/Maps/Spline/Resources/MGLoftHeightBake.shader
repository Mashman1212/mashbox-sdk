Shader "Hidden/MashBox/LoftHeightBake"
{
    Properties
    {
        _ControlMap1 ("Control Map 1", 2D) = "white" {}
        _ControlMap2 ("Control Map 2", 2D) = "black" {}

        _MaskMap00 ("Mask Map 00", 2D) = "gray" {}
        _MaskMap01 ("Mask Map 01", 2D) = "gray" {}
        _MaskMap02 ("Mask Map 02", 2D) = "gray" {}
        _MaskMap03 ("Mask Map 03", 2D) = "gray" {}
        _MaskMap04 ("Mask Map 04", 2D) = "gray" {}
        _MaskMap05 ("Mask Map 05", 2D) = "gray" {}
        _MaskMap06 ("Mask Map 06", 2D) = "gray" {}
        _MaskMap07 ("Mask Map 07", 2D) = "gray" {}

        // MG_Lit_Trail drives all three textures in a layer from this explicit
        // mapping value. It does not use the individual texture _ST values.
        _Tiling00 ("Tiling 00", Vector) = (1, 1, 0, 0)
        _Tiling01 ("Tiling 01", Vector) = (1, 1, 0, 0)
        _Tiling02 ("Tiling 02", Vector) = (1, 1, 0, 0)
        _Tiling03 ("Tiling 03", Vector) = (1, 1, 0, 0)
        _Tiling04 ("Tiling 04", Vector) = (1, 1, 0, 0)
        _Tiling05 ("Tiling 05", Vector) = (1, 1, 0, 0)
        _Tiling06 ("Tiling 06", Vector) = (1, 1, 0, 0)
        _Tiling07 ("Tiling 07", Vector) = (1, 1, 0, 0)

        _HeightBlend00 ("Height Blend 00", Float) = 0
        _HeightBlend01 ("Height Blend 01", Float) = 0
        _HeightBlend02 ("Height Blend 02", Float) = 0
        _HeightBlend03 ("Height Blend 03", Float) = 0
        _HeightBlend04 ("Height Blend 04", Float) = 0
        _HeightBlend05 ("Height Blend 05", Float) = 0
        _HeightBlend06 ("Height Blend 06", Float) = 0
        _HeightBlend07 ("Height Blend 07", Float) = 0

        _HeightOffset00 ("Height Offset 00", Float) = 0
        _HeightOffset01 ("Height Offset 01", Float) = 0
        _HeightOffset02 ("Height Offset 02", Float) = 0
        _HeightOffset03 ("Height Offset 03", Float) = 0
        _HeightOffset04 ("Height Offset 04", Float) = 0
        _HeightOffset05 ("Height Offset 05", Float) = 0
        _HeightOffset06 ("Height Offset 06", Float) = 0
        _HeightOffset07 ("Height Offset 07", Float) = 0

        _HeightContrast00 ("Height Contrast 00", Float) = 1
        _HeightContrast01 ("Height Contrast 01", Float) = 1
        _HeightContrast02 ("Height Contrast 02", Float) = 1
        _HeightContrast03 ("Height Contrast 03", Float) = 1
        _HeightContrast04 ("Height Contrast 04", Float) = 1
        _HeightContrast05 ("Height Contrast 05", Float) = 1
        _HeightContrast06 ("Height Contrast 06", Float) = 1
        _HeightContrast07 ("Height Contrast 07", Float) = 1

        _HeightInfluence00 ("Height Influence 00", Float) = 1
        _HeightInfluence01 ("Height Influence 01", Float) = 1
        _HeightInfluence02 ("Height Influence 02", Float) = 1
        _HeightInfluence03 ("Height Influence 03", Float) = 1
        _HeightInfluence04 ("Height Influence 04", Float) = 1
        _HeightInfluence05 ("Height Influence 05", Float) = 1
        _HeightInfluence06 ("Height Influence 06", Float) = 1
        _HeightInfluence07 ("Height Influence 07", Float) = 1

        _PlanarMap00 ("Planar Map 00", Float) = 0
        _PlanarMap01 ("Planar Map 01", Float) = 0
        _PlanarMap02 ("Planar Map 02", Float) = 0
        _PlanarMap03 ("Planar Map 03", Float) = 0
        _PlanarMap04 ("Planar Map 04", Float) = 0
        _PlanarMap05 ("Planar Map 05", Float) = 0
        _PlanarMap06 ("Planar Map 06", Float) = 0
        _PlanarMap07 ("Planar Map 07", Float) = 0

        _HeightTransition ("Height Transition", Float) = 0.1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Name "HeightBake"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                float2 splatUV : TEXCOORD2;
                float2 bakeUV : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float2 splatUV : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
            };

            sampler2D _ControlMap1;
            sampler2D _ControlMap2;
            float4 _ControlMap1_ST;
            float4 _ControlMap2_ST;

            sampler2D _MaskMap00;
            sampler2D _MaskMap01;
            sampler2D _MaskMap02;
            sampler2D _MaskMap03;
            sampler2D _MaskMap04;
            sampler2D _MaskMap05;
            sampler2D _MaskMap06;
            sampler2D _MaskMap07;

            float4 _Tiling00, _Tiling01, _Tiling02, _Tiling03;
            float4 _Tiling04, _Tiling05, _Tiling06, _Tiling07;

            float _HeightBlend00, _HeightBlend01, _HeightBlend02, _HeightBlend03;
            float _HeightBlend04, _HeightBlend05, _HeightBlend06, _HeightBlend07;
            float _HeightOffset00, _HeightOffset01, _HeightOffset02, _HeightOffset03;
            float _HeightOffset04, _HeightOffset05, _HeightOffset06, _HeightOffset07;
            float _HeightContrast00, _HeightContrast01, _HeightContrast02, _HeightContrast03;
            float _HeightContrast04, _HeightContrast05, _HeightContrast06, _HeightContrast07;
            float _HeightInfluence00, _HeightInfluence01, _HeightInfluence02, _HeightInfluence03;
            float _HeightInfluence04, _HeightInfluence05, _HeightInfluence06, _HeightInfluence07;
            float _PlanarMap00, _PlanarMap01, _PlanarMap02, _PlanarMap03;
            float _PlanarMap04, _PlanarMap05, _PlanarMap06, _PlanarMap07;
            float _HeightTransition;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Direct clip-space atlas rendering bypasses Unity's camera
                // projection, so render-target APIs with a top-left UV origin do
                // not receive Unity's usual projection flip automatically. Keep
                // the generated Texture2D and mesh UV3 in the same orientation.
                float2 bakePosition = input.bakeUV;
                #if UNITY_UV_STARTS_AT_TOP
                    bakePosition.y = 1.0 - bakePosition.y;
                #endif
                output.positionCS = float4(bakePosition * 2.0 - 1.0, 0.0, 1.0);
                output.uv0 = input.uv0;
                output.splatUV = input.splatUV;
                output.positionWS = mul(unity_ObjectToWorld, input.positionOS).xyz;
                output.normalWS = UnityObjectToWorldNormal(input.normalOS);
                return output;
            }

            float SampleTriplanarHeight(sampler2D textureSampler, float2 tiling, float3 positionWS, float3 normalWS)
            {
                float3 weights = abs(normalize(normalWS));
                weights = max(weights - 0.2, 0.0);
                weights /= max(weights.x + weights.y + weights.z, 0.0001);
                float x = tex2D(textureSampler, positionWS.zy * tiling).b;
                float y = tex2D(textureSampler, positionWS.xz * tiling).b;
                float z = tex2D(textureSampler, positionWS.xy * tiling).b;
                return dot(float3(x, y, z), weights);
            }

            float SampleHeight(sampler2D textureSampler, float2 tiling, float planar, Varyings input)
            {
                // UV0 is deliberately not saturated or remapped. Loft UV0 may run
                // far beyond 0-1 and the texture sampler repeats it continuously.
                float uvHeight = tex2D(textureSampler, input.uv0 * tiling).b;
                float planarHeight = SampleTriplanarHeight(textureSampler, tiling, input.positionWS, input.normalWS);
                return lerp(uvHeight, planarHeight, step(0.5, planar));
            }

            float AdjustHeight(float value, float offset, float contrast, float influence)
            {
                float contrasted = saturate((value - 0.5) * max(contrast, 0.0001) + 0.5 + offset);
                return lerp(0.5, contrasted, max(influence, 0.0));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 first = tex2D(_ControlMap1, input.splatUV * _ControlMap1_ST.xy + _ControlMap1_ST.zw);
                float4 second = tex2D(_ControlMap2, input.splatUV * _ControlMap2_ST.xy + _ControlMap2_ST.zw);
                float weightSum = dot(first, 1.0) + dot(second, 1.0);
                first /= max(weightSum, 0.0001);
                second /= max(weightSum, 0.0001);

                float h0 = AdjustHeight(SampleHeight(_MaskMap00, _Tiling00.xy, _PlanarMap00, input), _HeightOffset00, _HeightContrast00, _HeightInfluence00);
                float h1 = AdjustHeight(SampleHeight(_MaskMap01, _Tiling01.xy, _PlanarMap01, input), _HeightOffset01, _HeightContrast01, _HeightInfluence01);
                float h2 = AdjustHeight(SampleHeight(_MaskMap02, _Tiling02.xy, _PlanarMap02, input), _HeightOffset02, _HeightContrast02, _HeightInfluence02);
                float h3 = AdjustHeight(SampleHeight(_MaskMap03, _Tiling03.xy, _PlanarMap03, input), _HeightOffset03, _HeightContrast03, _HeightInfluence03);
                float h4 = AdjustHeight(SampleHeight(_MaskMap04, _Tiling04.xy, _PlanarMap04, input), _HeightOffset04, _HeightContrast04, _HeightInfluence04);
                float h5 = AdjustHeight(SampleHeight(_MaskMap05, _Tiling05.xy, _PlanarMap05, input), _HeightOffset05, _HeightContrast05, _HeightInfluence05);
                float h6 = AdjustHeight(SampleHeight(_MaskMap06, _Tiling06.xy, _PlanarMap06, input), _HeightOffset06, _HeightContrast06, _HeightInfluence06);
                float h7 = AdjustHeight(SampleHeight(_MaskMap07, _Tiling07.xy, _PlanarMap07, input), _HeightOffset07, _HeightContrast07, _HeightInfluence07);

                // Use the same eight normalized splat weights, with each layer's
                // height-blend switch controlling whether height can bias its weight.
                float4 scoreA = first + (float4(h0, h1, h2, h3) - 0.5) * saturate(float4(_HeightBlend00, _HeightBlend01, _HeightBlend02, _HeightBlend03));
                float4 scoreB = second + (float4(h4, h5, h6, h7) - 0.5) * saturate(float4(_HeightBlend04, _HeightBlend05, _HeightBlend06, _HeightBlend07));
                float maximum = max(max(max(scoreA.x, scoreA.y), max(scoreA.z, scoreA.w)), max(max(scoreB.x, scoreB.y), max(scoreB.z, scoreB.w)));
                float transition = max(_HeightTransition, 0.001);
                float4 blendedA = max(scoreA - maximum + transition, 0.0) * first;
                float4 blendedB = max(scoreB - maximum + transition, 0.0) * second;
                float blendedSum = dot(blendedA, 1.0) + dot(blendedB, 1.0);
                blendedA /= max(blendedSum, 0.0001);
                blendedB /= max(blendedSum, 0.0001);

                float height = dot(blendedA, float4(h0, h1, h2, h3)) + dot(blendedB, float4(h4, h5, h6, h7));
                return float4(height, height, height, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
