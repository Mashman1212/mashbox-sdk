Shader "Hidden/MashBox/PrefabStampProjection"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _BaseMap, _NormalMap, _PreviousMap;
            float4 _BaseST, _Tint, _TileRect, _StampCenter;
            float4x4 _WorldToClip;
            float _Radius, _Falloff, _NormalScale, _Cutoff, _HasNormal, _WriteNormal, _Invert;
            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; float4 tangent:TANGENT; float2 uv:TEXCOORD0; };
            struct Varyings { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float2 mapUV:TEXCOORD1; float2 radial:TEXCOORD2; float3 normal:TEXCOORD3; float4 tangent:TEXCOORD4; };
            Varyings vert(Input v)
            {
                Varyings o;
                float3 world = v.vertex.xyz + _StampCenter.xyz;
                // The same upper source surface wins for additive and inverted stamps.
                float3 depthPosition = world; depthPosition.y = v.vertex.y * _Invert;
                o.position = mul(_WorldToClip, float4(depthPosition,1));
                o.uv = v.uv * _BaseST.xy + _BaseST.zw;
                o.mapUV = (world.xz - _TileRect.xy) / _TileRect.zw;
                o.radial = v.vertex.xz / _Radius;
                o.normal = v.normal; o.tangent = v.tangent;
                return o;
            }
            float4 frag(Varyings i):SV_Target
            {
                float distance = length(i.radial); clip(1-distance);
                float4 colour = tex2D(_BaseMap,i.uv) * _Tint; clip(colour.a-_Cutoff);
                float4 previous = tex2D(_PreviousMap,i.mapUV);
                if (_WriteNormal > .5)
                {
                    float3 n = normalize(i.normal);
                    if (_HasNormal > .5)
                    {
                        float3 t = normalize(i.tangent.xyz - n * dot(n,i.tangent.xyz));
                        float3 b = cross(n,t) * (i.tangent.w < 0 ? -1 : 1);
                        float3 detail = UnpackNormal(tex2D(_NormalMap,i.uv));
                        detail.xy *= _NormalScale; detail = normalize(detail);
                        n = normalize(t*detail.x+b*detail.y+n*detail.z);
                    }
                    float weight = _Falloff <= 0 ? 1 : smoothstep(0,1,(1-distance)/min(.5,_Falloff*.125));
                    n = normalize(lerp(normalize(previous.rgb*2-1),n,weight));
                    return float4(n*.5+.5,1);
                }
                return float4(lerp(previous.rgb,colour.rgb,pow(saturate(1-distance),_Falloff)),previous.a);
            }
            ENDCG
        }
    }
}
