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
            struct Input { float4 vertex:POSITION;float3 end:NORMAL;float2 corner:TEXCOORD0; };
            struct Output { float4 position:SV_POSITION; };
            Output Vert(Input input)
            {
                Output output;float4 clip=UnityObjectToClipPos(input.vertex);
                if(_Mode>1.5)
                {
                    float4 end=UnityObjectToClipPos(float4(input.end,1));
                    // Clip endpoints to the near plane before dividing by w. This also
                    // prevents very long screen quads when an edge crosses the camera.
                    #if defined(UNITY_REVERSED_Z)
                    float d0=clip.w-clip.z,d1=end.w-end.z;
                    #else
                    float d0=clip.z-UNITY_NEAR_CLIP_VALUE*clip.w,d1=end.z-UNITY_NEAR_CLIP_VALUE*end.w;
                    #endif
                    if(d0<0&&d1>=0)clip=lerp(clip,end,d0/(d0-d1));
                    else if(d1<0&&d0>=0)end=lerp(end,clip,d1/(d1-d0));
                    float2 direction=(end.xy/max(end.w,1e-6)-clip.xy/max(clip.w,1e-6))*_Viewport.xy;
                    float2 perpendicular=float2(-direction.y,direction.x)*rsqrt(max(dot(direction,direction),1e-8));
                    clip=lerp(clip,end,input.corner.x);
                    clip.xy+=perpendicular*input.corner.y*_Size*2/max(_Viewport.xy,1)*clip.w;
                }
                else if(_Mode>.5)clip.xy+=input.corner*_Size*2/max(_Viewport.xy,1)*clip.w;
                output.position=clip;return output;
            }
            float4 Frag(Output input):SV_Target {return _Color;}
            ENDHLSL
        }
    }
}
