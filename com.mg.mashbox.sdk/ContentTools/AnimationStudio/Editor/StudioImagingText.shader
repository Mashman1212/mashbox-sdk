Shader "Hidden/MashBox/ImagingText"
{
    Properties { _MainTex ("Font",2D)="white"{} }
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct input {float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            struct output {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            output vert(input v){output o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
            float4 frag(output i):SV_Target
            {
                float a=tex2D(_MainTex,i.uv).a*i.color.a;float3 color=i.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                color=GammaToLinearSpace(color);
                #endif
                return float4(color*a,a);
            }
            ENDCG
        }
        Pass {
            Blend Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 frag(v2f_img i):SV_Target{float4 c=tex2D(_MainTex,i.uv);return float4(c.a>1e-5?c.rgb/c.a:0,c.a);}
            ENDCG
        }
    }
}
