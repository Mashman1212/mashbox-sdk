Shader "Hidden/StudioValidationSolid" { SubShader { Pass { Cull Off ZWrite On ZTest LEqual
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 float4 vert(float4 p:POSITION):SV_POSITION{return UnityObjectToClipPos(p);}
 float4 frag():SV_Target{return float4(1,1,1,1);}
 ENDCG
 } } }
