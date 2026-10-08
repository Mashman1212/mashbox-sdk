Shader "Hidden/MashBox/TerrainMergeOverlay"
{
    Properties { _Opacity ("Opacity", Range(0, 1)) = 0.55 }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Opacity;
            struct Input { float4 vertex : POSITION; float4 color : COLOR; };
            struct Output { float4 vertex : SV_POSITION; float4 color : COLOR; };
            Output vert(Input input)
            {
                Output output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                return output;
            }
            float4 frag(Output input) : SV_Target
            {
                return float4(input.color.rgb, input.color.a * _Opacity);
            }
            ENDHLSL
        }
    }
}
