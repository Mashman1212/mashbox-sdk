Shader "Hidden/MashBox/ModelerWireBatch"
{
    Properties { [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth test",Float)=4 }
    SubShader
    {
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            StructuredBuffer<float3> _WirePositions;
            StructuredBuffer<uint> _WireEdges;
            StructuredBuffer<int> _WireAdjacent;
            float4x4 _WireObjectToWorld;
            float4 _WireViewport,_WireColor,_WireCamera,_WireForward;
            float _WireWidth,_WireXRay;
            struct Output { float4 position:SV_POSITION; noperspective float2 pixel:TEXCOORD0; nointerpolation float segmentLength:TEXCOORD1; nointerpolation float valid:TEXCOORD2; };
            float3 Bias(float3 p)
            {
                if(_WireXRay>.5)return p;
                if(_WireCamera.w<.5)return lerp(p,_WireCamera.xyz,.001);
                return p-_WireForward.xyz*max(_WireForward.w,dot(p-_WireCamera.xyz,_WireForward.xyz))*.001;
            }
            float Cross2(float2 a,float2 b){return a.x*b.y-a.y*b.x;}
            float FaceDepth(float4 a,float4 b,float4 c,float2 pixel,float fallback)
            {
                float3 pa=a.xyz/a.w,pb=b.xyz/b.w;
                // Homogeneous form also works when the adjacent face vertex is on
                // or behind the camera plane. Dividing by c.w would flip its side.
                float3 towardsC=c.xyz-c.w*pa;
                // Only the side covered by this face can occlude the expanded stroke.
                if(Cross2(pb.xy-pa.xy,towardsC.xy)*Cross2(pb.xy-pa.xy,pixel-pa.xy)<0)return fallback;
                float3 plane=cross(pb-pa,towardsC);
                if(abs(plane.z)<1e-10)return fallback;
                float depth=pa.z-dot(plane.xy,pixel-pa.xy)/plane.z;
                #if defined(UNITY_REVERSED_Z)
                    return depth>=0&&depth<=1?max(fallback,depth):fallback;
                #else
                    return depth>=UNITY_NEAR_CLIP_VALUE&&depth<=1?min(fallback,depth):fallback;
                #endif
            }
            Output Vert(uint vertex:SV_VertexID,uint edge:SV_InstanceID)
            {
                Output output;
                float3 a=mul(_WireObjectToWorld,float4(_WirePositions[_WireEdges[edge*2]],1)).xyz;
                float3 b=mul(_WireObjectToWorld,float4(_WirePositions[_WireEdges[edge*2+1]],1)).xyz;
                int first=_WireAdjacent[edge*2],second=_WireAdjacent[edge*2+1];
                float3 c=first>0?mul(_WireObjectToWorld,float4(_WirePositions[first-1],1)).xyz:a;
                float3 d=second!=0?mul(_WireObjectToWorld,float4(_WirePositions[abs(second)-1],1)).xyz:a;
                a=Bias(a);b=Bias(b);c=Bias(c);d=Bias(d);
                float da=dot(a-_WireCamera.xyz,_WireForward.xyz),db=dot(b-_WireCamera.xyz,_WireForward.xyz);
                output.valid=(max(da,db)>=_WireForward.w);
                if(output.valid<.5){output.position=float4(2,2,0,1);output.pixel=0;output.segmentLength=0;return output;}
                if(da<_WireForward.w && db>=_WireForward.w)a=lerp(a,b,saturate((_WireForward.w-da)/max(db-da,1e-8)));
                else if(db<_WireForward.w && da>=_WireForward.w)b=lerp(b,a,saturate((_WireForward.w-db)/max(da-db,1e-8)));
                float4 ca=UnityWorldToClipPos(a),cb=UnityWorldToClipPos(b);
                float2 delta=(cb.xy/max(cb.w,1e-6)-ca.xy/max(ca.w,1e-6))*_WireViewport.xy*.5;
                float segmentLength=max(length(delta),1e-5);float2 tangent=delta/segmentLength,normal=float2(-tangent.y,tangent.x);
                bool end=(vertex==2 || vertex==4 || vertex==5);
                float side=(vertex==0 || vertex==3 || vertex==5)?-1:1;
                float radius=_WireWidth*.5+.75;
                float along=end?segmentLength+radius:-radius;
                float4 clip=end?cb:ca;
                clip.xy+=(normal*side*radius+tangent*(end?radius:-radius))*2/max(_WireViewport.xy,1)*clip.w;
                // Extend depth with the cap in screen space as well. Holding endpoint
                // depth fixed makes an oblique line sink into its own face near a cap.
                float depthSlope=(cb.z/cb.w-ca.z/ca.w)/segmentLength;
                clip.z+=depthSlope*(end?radius:-radius)*clip.w;
                if(_WireXRay<.5)
                {
                    float depth=clip.z/clip.w;
                    if(first>0)depth=FaceDepth(ca,cb,UnityWorldToClipPos(c),clip.xy/clip.w,depth);
                    if(second!=0)depth=FaceDepth(ca,cb,UnityWorldToClipPos(d),clip.xy/clip.w,depth);
                    clip.z=depth*clip.w;
                }
                output.position=clip;output.pixel=float2(along,side*radius);output.segmentLength=segmentLength;
                return output;
            }
            float4 Frag(Output input):SV_Target
            {
                float cap=max(max(-input.pixel.x,input.pixel.x-input.segmentLength),0);
                float distance=length(float2(cap,input.pixel.y));
                float coverage=saturate(_WireWidth*.5+.5-distance)*input.valid;
                return float4(_WireColor.rgb,_WireColor.a*coverage);
            }
            ENDHLSL
        }
    }
}
