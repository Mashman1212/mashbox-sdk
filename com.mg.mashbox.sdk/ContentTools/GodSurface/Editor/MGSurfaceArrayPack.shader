Shader "Hidden/MashBox/GodSurfaceArrayPack"
{
    Properties { _MainTex("Source", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _DecodeNormal;
            float4 frag(v2f_img input) : SV_Target
            {
                float4 value = tex2D(_MainTex, input.uv);
                if (_DecodeNormal > 0.5) value = float4(UnpackNormal(value) * 0.5 + 0.5, 1);
                return value;
            }
            ENDHLSL
        }
    }
}

