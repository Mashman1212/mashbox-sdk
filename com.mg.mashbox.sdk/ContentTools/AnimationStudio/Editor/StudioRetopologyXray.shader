Shader "Hidden/MashBox/RetopologyXray"
{
    Properties
    {
        _Color("Color",Color)=(.35,1,.8,.14)
        _Depth("Maximum penetration",Float)=.04
        _Near("Camera near plane",Float)=.01
    }
    SubShader
    {
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            float4 _Color;
            float _Depth,_Near;
            struct Output {float4 position:SV_POSITION;};
            Output Vert(float4 vertex:POSITION)
            {
                Output output;
                float4 view=mul(UNITY_MATRIX_MV,vertex);
                float4 clip=mul(UNITY_MATRIX_P,view);
                // Bias depth only, in camera-space metres. Keep the original XY/W
                // so topology never moves on screen, even in perspective views.
                view.z=min(-max(_Near,0.0001)*1.001,view.z+max(_Depth,0));
                float4 biased=mul(UNITY_MATRIX_P,view);
                clip.z=biased.z/max(biased.w,0.00001)*clip.w;
                output.position=clip;return output;
            }
            float4 Frag(Output input):SV_Target {return _Color;}
            ENDHLSL
        }
    }
}
