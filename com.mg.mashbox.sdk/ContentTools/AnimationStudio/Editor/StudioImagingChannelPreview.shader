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
            float value=_Channel<1.5?c.r:_Channel<2.5?c.g:_Channel<3.5?c.b:c.a;
            #ifndef UNITY_COLORSPACE_GAMMA
            if(_Channel>3.5)value=GammaToLinearSpace(value);
            #endif
            return float4(value,value,value,1);
        }
        ENDCG
    }}
}
