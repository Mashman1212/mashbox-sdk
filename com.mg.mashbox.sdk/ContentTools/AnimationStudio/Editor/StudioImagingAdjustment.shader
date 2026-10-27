Shader "Hidden/MashBox/ImagingAdjustment"
{
    Properties { _MainTex ("Source",2D)="white"{} _SelectionMask ("Selection",2D)="white"{} }
    SubShader { Cull Off ZWrite Off ZTest Always Pass {
        CGPROGRAM
        #pragma target 3.0
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        sampler2D _MainTex,_SelectionMask;
        float _Adjustment,_Channel,_HasSelection,_UseMask;
        float4 _Hue,_Levels,_Output,_Crop,_Layer,_SourceSize,_Selection,_MaskOffset,_Corner0,_Corner1,_Corner2,_Corner3;
        float3 hsv(float3 c)
        {
            float4 k=float4(0,-1.0/3,2.0/3,-1);
            float4 p=lerp(float4(c.bg,k.wz),float4(c.gb,k.xy),step(c.b,c.g));
            float4 q=lerp(float4(p.xyw,c.r),float4(c.r,p.yzx),step(p.x,c.r));
            float d=q.x-min(q.w,q.y);return float3(abs(q.z+(q.w-q.y)/(6*d+1e-10)),d/(q.x+1e-10),q.x);
        }
        float3 rgb(float3 h){return h.z*lerp(1,saturate(abs(frac(h.x+float3(0,2.0/3,1.0/3))*6-3)-1),h.y);}
        float3 adjust(float3 c)
        {
            if(_Adjustment<1.5)
            {
                float3 h=hsv(c);h.x=frac(h.x+_Hue.x/360);h.y=_Hue.y<0?h.y*(1+_Hue.y/100):lerp(h.y,1,_Hue.y/100);
                c=rgb(float3(h.x,saturate(h.y),h.z));return lerp(c,_Hue.z<0?0:1,abs(_Hue.z)/100);
            }
            if(_Adjustment<2.5)return lerp(_Output.x,_Output.y,pow(saturate((c-_Levels.x)/max(.001,_Levels.y-_Levels.x)),1/max(.1,_Levels.z)));
            float l=dot(c,float3(.2126,.7152,.0722));float shadow=(1-l)*(1-l)*_Output.z,highlight=l*l*_Output.w;
            return lerp(lerp(c,1,shadow*.7),0,highlight*.7);
        }
        float4 frag(v2f_img i):SV_Target
        {
            float4 original=tex2D(_MainTex,i.uv);float2 source=float2(i.uv.x,1-i.uv.y);
            if(any(source<_Crop.xy)||any(source>=_Crop.xy+_Crop.zw))return original;
            float2 uv=(source-_Crop.xy)/_Crop.zw;
            float2 q=lerp(lerp(_Corner0.xy,_Corner1.xy,uv.x),lerp(_Corner3.xy,_Corner2.xy,uv.x),uv.y);
            float2 local=(q-.5)*_Crop.zw*_SourceSize.xy*_Layer.z;
            float s=sin(_Layer.w),c=cos(_Layer.w);float2 canvas=(_Layer.xy+float2(c*local.x-s*local.y,s*local.x+c*local.y))/_SourceSize.z;
            if(_HasSelection>.5)
            {
                if(any(canvas<_Selection.xy)||any(canvas>=_Selection.zw))return original;
                if(_UseMask>.5){float2 mask=canvas-_MaskOffset.xy;if(any(mask<0)||any(mask>=1)||tex2Dlod(_SelectionMask,float4(mask.x,1-mask.y,0,0)).a<.5)return original;}
            }
            float3 color=original.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
            color=LinearToGammaSpace(color);
            #endif
            float3 result;
            float alpha=original.a;
            if(_Channel<.5){if(original.a<=0)return original;result=adjust(color);}
            else
            {
                float value=_Channel<1.5?color.r:_Channel<2.5?color.g:_Channel<3.5?color.b:original.a;
                float changed=adjust(value.xxx).r;result=color;
                if(_Channel<1.5)result.r=changed;else if(_Channel<2.5)result.g=changed;else if(_Channel<3.5)result.b=changed;else alpha=changed;
            }
            #ifndef UNITY_COLORSPACE_GAMMA
            result=GammaToLinearSpace(result);
            #endif
            return float4(result,alpha);
        }
        ENDCG
    }}
}
