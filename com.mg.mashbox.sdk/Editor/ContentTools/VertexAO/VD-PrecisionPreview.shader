Shader "Hidden/VertexDirt/PrecisionPreview"
{
    Properties { _Channel ("Channel", Int) = 0 _HasColors ("Has colors", Float) = 1 }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f { float4 position : SV_POSITION; float4 color : COLOR; };
            int _Channel;
            float _HasColors;
            v2f vert(appdata v) { v2f o; o.position = UnityObjectToClipPos(v.vertex); o.color = lerp(float4(1,1,1,1), v.color, _HasColors); return o; }
            float4 frag(v2f i) : SV_Target
            {
                float3 color = i.color.rgb;
                if (_Channel == 1) color = i.color.rrr;
                if (_Channel == 2) color = i.color.ggg;
                if (_Channel == 3) color = i.color.bbb;
                if (_Channel == 4) color = i.color.aaa;
                #ifndef UNITY_COLORSPACE_GAMMA
                color = GammaToLinearSpace(color);
                #endif
                return float4(color, 1);
            }
            ENDCG
        }
    }
}
