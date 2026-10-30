Shader "Hidden/FrameSubdivisionCheck" {
SubShader { Tags { "RenderType"="Opaque" } Pass { CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
struct v2f { float4 pos:SV_POSITION; float3 normal:TEXCOORD0; };
v2f vert(appdata_base v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.normal=UnityObjectToWorldNormal(v.normal); return o; }
fixed4 frag(v2f i):SV_Target { float shade=.18+.7*saturate(dot(normalize(i.normal),normalize(float3(.4,.7,-.6)))); return fixed4(shade.xxx,1); }
ENDCG } }
}
