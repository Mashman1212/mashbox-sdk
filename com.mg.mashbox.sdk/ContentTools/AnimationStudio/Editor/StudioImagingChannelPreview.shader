Shader "Hidden/MashBox/ImagingChannelPreview"
{
    Properties { _MainTex ("Image",2D)="white"{} _Channel ("Channel",Float)=0 }
    SubShader { Cull Off ZWrite Off ZTest Always Pass {
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        sampler2D _MainTex;float _Channel;
        float4 frag(v2f_img i):SV_Target
        {
            float4 c=tex2D(_MainTex,i.uv);if(_Channel<.5)return c;
            if(_Channel<1.5)return float4(c.r,0,0,1);
            if(_Channel<2.5)return float4(0,c.g,0,1);
            if(_Channel<3.5)return float4(0,0,c.b,1);
            float value=c.a;
            #ifndef UNITY_COLORSPACE_GAMMA
            value=GammaToLinearSpace(value);
            #endif
            return float4(value,value,value,1);
        }
        ENDCG
    }}
}
