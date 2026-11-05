Shader "Hidden/StudioFlipNormalsProbe" { SubShader { Pass { Cull Back ZWrite On ZTest LEqual CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
float4 vert(float4 vertex:POSITION):SV_POSITION {return UnityObjectToClipPos(vertex);}
float4 frag():SV_Target {return float4(.8,.8,.8,1);}
ENDCG
} } }
