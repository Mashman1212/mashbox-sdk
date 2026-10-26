Shader "Hidden/MashBox/ModelerComponents"
{
    Properties
    {
        _Color ("Color",Color)=(1,1,1,1)
        _Mode ("Overlay mode",Float)=0
        _Size ("Pixel size",Float)=4
        _Viewport ("Viewport size",Vector)=(1920,1080,0,0)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth test",Float)=8
    }
    SubShader
    {
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            Offset -1,-1
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            float4 _Color,_Viewport;
            float _Mode,_Size;
            struct Input {float4 vertex:POSITION;float2 corner:TEXCOORD0;};
            struct Output {float4 position:SV_POSITION;};
            Output Vert(Input input)
            {
                Output output;float4 clip=UnityObjectToClipPos(input.vertex);
                if(_Mode>.5)clip.xy+=input.corner*_Size*2/max(_Viewport.xy,1)*clip.w;
                output.position=clip;return output;
            }
            float4 Frag(Output input):SV_Target {return _Color;}
            ENDHLSL
        }
    }
}
