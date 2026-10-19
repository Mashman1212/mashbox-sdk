Shader "Hidden/MashBox/Studio/FoliageAtlas"
{
    Properties { _Wood("Wood",2D)="white"{} _Leaf("Foliage",2D)="white"{} }
    SubShader { Pass {
        ZTest Always ZWrite Off Cull Off
        HLSLPROGRAM
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        sampler2D _Wood, _Leaf;
        float4 _WoodUV, _WoodST, _WoodTint, _LeafTint;
        float _Padding, _Map, _WoodPresent, _LeafPresent, _WoodNormal, _LeafNormal;
        float4 frag(v2f_img input):SV_Target
        {
            bool wood=input.uv.x>=.75;
            float2 uv;
            uv.x=saturate((input.uv.x-(wood?.75:0)-_Padding)/((wood?.25:.75)-2*_Padding));
            uv.y=saturate((input.uv.y-_Padding)/(1-2*_Padding));
            if(wood)uv=(uv*_WoodUV.zw+_WoodUV.xy)*_WoodST.xy+_WoodST.zw;
            float4 value=wood?tex2D(_Wood,uv):tex2D(_Leaf,uv);
            bool present=(wood?_WoodPresent:_LeafPresent)>.5;
            if(!present)value=_Map<1.5?float4(.5,.5,1,1):_Map<2.5?float4(0,1,1,.2):float4(1,1,1,1);
            if(_Map<.5){value*=wood?_WoodTint:_LeafTint;if(wood)value.a=1;}
            else if(_Map<1.5&&present&&(wood?_WoodNormal:_LeafNormal)>.5)value=float4(UnpackNormal(value)*.5+.5,1);
            return value;
        }
        ENDHLSL
    } }
}
