Shader "Hidden/BevelHoleProbe" {
 Properties { _Color("Color",Color)=(0.6,0.6,0.6,1) _Wire("Wire",Float)=0 _Offset("Offset",Float)=0 }
 SubShader { Tags {"RenderType"="Opaque"} Pass { Cull Off ZTest LEqual ZWrite On Offset [_Offset],[_Offset]
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 float4 _Color;float _Wire;
 struct v2f {float4 pos:SV_POSITION;float3 normal:TEXCOORD0;};
 v2f vert(appdata_base v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.normal=UnityObjectToWorldNormal(v.normal);return o;}
 float4 frag(v2f i):SV_Target{float light=_Wire>.5?1:.35+.65*abs(dot(normalize(i.normal),normalize(float3(.4,.6,1))));return float4(_Color.rgb*light,1);}
 ENDCG
 } }
}
