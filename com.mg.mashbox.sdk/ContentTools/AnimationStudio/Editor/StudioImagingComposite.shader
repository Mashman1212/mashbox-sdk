Shader "Hidden/MashBox/ImagingComposite"
{
    Properties
    {
        _MainTex ("Composite", 2D) = "black" {}
        _Layer ("Layer", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Layer;
            float4 _Placement, _Crop, _Tint;
            float4 _Corner0, _Corner1, _Corner2, _Corner3;
            float _Warped;
            float cross2(float2 a,float2 b){return a.x*b.y-a.y*b.x;}
            float _Rotation, _BlackToAlpha, _Blend, _Exposure, _Saturation;
            float4 _MainTex_TexelSize;
            float lum(float3 c){return dot(c,float3(.3,.59,.11));}
            float sat(float3 c){return max(c.r,max(c.g,c.b))-min(c.r,min(c.g,c.b));}
            float3 clipcolor(float3 c)
            {
                float l=lum(c),n=min(c.r,min(c.g,c.b)),x=max(c.r,max(c.g,c.b));
                if(n<0)c=l+(c-l)*l/max(l-n,1e-6);
                if(x>1)c=l+(c-l)*(1-l)/max(x-l,1e-6);
                return saturate(c);
            }
            float3 setlum(float3 c,float l){return clipcolor(c+(l-lum(c)));}
            float3 setsat(float3 c,float s){float n=min(c.r,min(c.g,c.b)),x=max(c.r,max(c.g,c.b));return (c-n)*s/max(x-n,1e-6);}
            float burn(float d,float s){if(d>=1)return 1;if(s<=0)return 0;return 1-min(1,(1-d)/s);}
            float dodge(float d,float s){if(d<=0)return 0;if(s>=1)return 1;return min(1,d/(1-s));}
            float3 burn3(float3 d,float3 s){return float3(burn(d.r,s.r),burn(d.g,s.g),burn(d.b,s.b));}
            float3 dodge3(float3 d,float3 s){return float3(dodge(d.r,s.r),dodge(d.g,s.g),dodge(d.b,s.b));}
            float3 vivid(float3 d,float3 s){return lerp(burn3(d,2*s),dodge3(d,2*s-1),step(.5,s));}
            float3 blendmode(float3 d,float3 s)
            {
                if(_Blend==1)return d*s;
                if(_Blend==2)return 1-(1-d)*(1-s);
                if(_Blend==3)return d+s;
                if(_Blend==5)return min(d,s);
                if(_Blend==6)return burn3(d,s);
                if(_Blend==7)return max(0,d+s-1);
                if(_Blend==8)return lum(s)<lum(d)?s:d;
                if(_Blend==9)return max(d,s);
                if(_Blend==10)return dodge3(d,s);
                if(_Blend==11)return lum(s)>lum(d)?s:d;
                if(_Blend==12)return lerp(2*d*s,1-2*(1-d)*(1-s),step(.5,d));
                if(_Blend==13)
                {float3 g=lerp(((16*d-12)*d+4)*d,sqrt(max(0,d)),step(.25,d));return lerp(d-(1-2*s)*d*(1-d),d+(2*s-1)*(g-d),step(.5,s));}
                if(_Blend==14)return lerp(2*d*s,1-2*(1-d)*(1-s),step(.5,s));
                if(_Blend==15)return vivid(d,s);
                if(_Blend==16)return saturate(d+2*s-1);
                if(_Blend==17)return lerp(min(d,2*s),max(d,2*s-1),step(.5,s));
                if(_Blend==18)return step(.5,vivid(d,s));
                if(_Blend==19)return abs(d-s);
                if(_Blend==20)return d+s-2*d*s;
                if(_Blend==21)return max(0,d-s);
                if(_Blend==22)return saturate(d/max(s,1e-6));
                if(_Blend==23)return setlum(setsat(s,sat(d)),lum(d));
                if(_Blend==24)return setlum(setsat(d,sat(s)),lum(d));
                if(_Blend==25)return setlum(s,lum(d));
                if(_Blend==26)return setlum(d,lum(s));
                return s;
            }
            float4 frag(v2f_img i):SV_Target
            {
                float4 dst=tex2D(_MainTex,i.uv);
                float2 p=i.uv-_Placement.xy;
                float c=cos(_Rotation),s=sin(_Rotation);
                p=float2(c*p.x-s*p.y,s*p.x+c*p.y)/max(_Placement.zw,0.000001);
                float2 uv=p+_Crop.xy+_Crop.zw*.5;
                if(_Warped>.5)
                {
                    float2 target=float2(p.x/_Crop.z+.5,.5-p.y/_Crop.w);
                    float2 a=_Corner0.xy,b=_Corner1.xy-a,c=_Corner3.xy-a;
                    float2 d=a-_Corner1.xy+_Corner2.xy-_Corner3.xy;
                    float2 q=float2(.5,.5);
                    [loop] for(int step=0;step<12;step++)
                    {
                        float2 error=a+b*q.x+c*q.y+d*(q.x*q.y)-target;
                        if(dot(error,error)<1e-12)break;
                        float2 du=b+d*q.y,dv=c+d*q.x;float det=cross2(du,dv);
                        if(abs(det)<1e-8)return dst;
                        q-=float2(cross2(error,dv),cross2(du,error))/det;
                    }
                    float2 residual=a+b*q.x+c*q.y+d*(q.x*q.y)-target;
                    if(any(q<0)||any(q>1)||dot(residual,residual)>1e-8)return dst;
                    uv=_Crop.xy+float2(q.x,1-q.y)*_Crop.zw;
                }
                if(any(uv<_Crop.xy)||any(uv>_Crop.xy+_Crop.zw))return dst;
                float4 src=tex2D(_Layer,uv);
                if(_BlackToAlpha>.5){src.a*=dot(src.rgb,float3(.2126,.7152,.0722));src.rgb=1;}
                src*=_Tint;src.rgb*=exp2(_Exposure);
                src.rgb=lerp(dot(src.rgb,float3(.2126,.7152,.0722)).xxx,src.rgb,_Saturation);
                if(_Blend==4)
                {
                    float2 pixel=floor(i.uv*_MainTex_TexelSize.zw);
                    float noise=frac(sin(dot(pixel,float2(12.9898,78.233)))*43758.5453);
                    src.a=src.a>noise?1:0;
                }
                float3 blend=blendmode(dst.rgb,src.rgb);
                float alpha=src.a+dst.a*(1-src.a);
                float3 rgb=src.a*((1-dst.a)*src.rgb+dst.a*blend)+dst.a*(1-src.a)*dst.rgb;
                return float4(alpha>0?rgb/alpha:0,alpha);
            }
            ENDCG
        }
    }
}
