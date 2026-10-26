Shader "Hidden/MashBox/GizmoAntialias"
{
    Properties { _MainTex("Overlay",2D)="black"{} }
    SubShader { Pass {
        ZTest Always ZWrite Off Cull Off
        HLSLPROGRAM
        #pragma vertex vert_img
        #pragma fragment Frag
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float Luma(float4 c) { return dot(c.rgb,float3(.299,.587,.114))+c.a*.5; }
        float4 Frag(v2f_img input):SV_Target
        {
            float2 uv=input.uv, pixel=_MainTex_TexelSize.xy;
            float4 center=tex2D(_MainTex,uv);
            float nw=Luma(tex2D(_MainTex,uv+float2(-1,-1)*pixel));
            float ne=Luma(tex2D(_MainTex,uv+float2(1,-1)*pixel));
            float sw=Luma(tex2D(_MainTex,uv+float2(-1,1)*pixel));
            float se=Luma(tex2D(_MainTex,uv+float2(1,1)*pixel));
            float middle=Luma(center),low=min(middle,min(min(nw,ne),min(sw,se))),high=max(middle,max(max(nw,ne),max(sw,se)));
            if(high-low<max(.0312,high*.125))return center;
            float2 direction=float2(-((nw+ne)-(sw+se)),(nw+sw)-(ne+se));
            float reduce=max((nw+ne+sw+se)*(.25*.125),1.0/128);
            direction=clamp(direction/(min(abs(direction.x),abs(direction.y))+reduce),-8,8)*pixel;
            float4 a=.5*(tex2D(_MainTex,uv+direction*(1.0/3-.5))+tex2D(_MainTex,uv+direction*(2.0/3-.5)));
            float4 b=a*.5+.25*(tex2D(_MainTex,uv-direction*.5)+tex2D(_MainTex,uv+direction*.5));
            float luma=Luma(b);
            return luma<low||luma>high?a:b;
        }
        ENDHLSL
    } }
}
