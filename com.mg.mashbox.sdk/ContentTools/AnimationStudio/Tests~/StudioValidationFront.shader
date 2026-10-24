Shader "Hidden/StudioValidationFront"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) _Cull ("Cull", Float) = 2 }
    SubShader { Pass { Cull [_Cull] ZWrite On ZTest LEqual
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #include "UnityCG.cginc"
        float4 _Color;
        float4 vert(float4 p:POSITION):SV_POSITION { return UnityObjectToClipPos(p); }
        float4 frag():SV_Target { return _Color; }
        ENDCG
    } }
}
