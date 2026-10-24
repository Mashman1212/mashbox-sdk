Shader "Hidden/MashBox/ModelerVertexMarkers"
{
    Properties { _MarkerColor("Color",Color)=(1,1,1,1) [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth test",Float)=4 }
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
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            StructuredBuffer<float3> _MarkerPositions;
            StructuredBuffer<uint> _MarkerIds;
            float4x4 _MarkerObjectToWorld;
            float4 _MarkerViewport, _MarkerColor;
            float _MarkerSize;
            int _MarkerMode, _MarkerHover;
            struct Output { float4 position:SV_POSITION; };
            Output Vert(uint corner:SV_VertexID, uint instance:SV_InstanceID)
            {
                uint vertexIndex = _MarkerMode == 0 ? instance : _MarkerMode == 1 ? _MarkerIds[instance] : (uint)_MarkerHover;
                const float2 corners[6] = { float2(-.5,-.5), float2(-.5,.5), float2(.5,.5), float2(-.5,-.5), float2(.5,.5), float2(.5,-.5) };
                float4 world = mul(_MarkerObjectToWorld, float4(_MarkerPositions[vertexIndex],1));
                Output output; output.position = mul(UNITY_MATRIX_VP,world);
                output.position.xy += corners[corner]*_MarkerSize*2/max(_MarkerViewport.xy,1)*output.position.w;
                return output;
            }
            float4 Frag(Output input):SV_Target { return _MarkerColor; }
            ENDHLSL
        }
    }
}