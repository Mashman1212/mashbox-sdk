Shader "Hidden/MashBox/TerrainPicking"
{
    SubShader
    {
        Pass
        {
            Cull Back
            ZWrite On
            ZTest LEqual
            Blend Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _SelectionID;
            float4 vert(float4 position : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(position);
            }
            float4 frag() : SV_Target { return _SelectionID; }
            ENDHLSL
        }
    }
}
