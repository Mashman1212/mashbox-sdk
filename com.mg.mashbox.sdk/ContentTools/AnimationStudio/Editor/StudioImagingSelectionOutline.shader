Shader "Hidden/MashBox/ImagingSelectionOutline"
{
    Properties { _MainTex ("Mask",2D)="black"{} }
    SubShader { Cull Off ZWrite Off ZTest Always Pass {
        CGPROGRAM
        #pragma target 3.0
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        sampler2D _MainTex;float4 _ImageRect,_ViewSize,_MaskOffset;float _Mip;
        float selected(float2 p)
        {p-=_MaskOffset.xy;if(any(p<0)||any(p>=1))return 0;return tex2Dlod(_MainTex,float4(p.x,1-p.y,0,_Mip)).a>.5?1:0;}
        float4 frag(v2f_img i):SV_Target
        {
            float2 gui=float2(i.uv.x,1-i.uv.y)*_ViewSize.xy;
            float2 p=(gui-_ImageRect.xy)/_ImageRect.zw,stepUV=1/_ImageRect.zw;
            float center=selected(p);
            float border=center*(1-min(min(selected(p+float2(stepUV.x,0)),selected(p-float2(stepUV.x,0))),min(selected(p+float2(0,stepUV.y)),selected(p-float2(0,stepUV.y)))));
            float color=fmod(floor((gui.x+gui.y)/4),2);
            return float4(color,color,color,border);
        }
        ENDCG
    }}
}
