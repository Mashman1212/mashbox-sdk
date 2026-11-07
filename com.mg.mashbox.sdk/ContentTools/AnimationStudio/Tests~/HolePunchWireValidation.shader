Shader "Hidden/HolePunchWireValidation" { SubShader { Pass { ZWrite Off ZTest LEqual Cull Off Offset -1,-1 CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
float4 vert(float4 p:POSITION):SV_POSITION{return UnityObjectToClipPos(p);}
fixed4 frag():SV_Target{return fixed4(1,.65,.1,1);}
ENDCG } } }
