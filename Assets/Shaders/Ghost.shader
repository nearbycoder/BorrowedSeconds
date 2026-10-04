// Translucent preview silhouettes (future positions). Fresnel edge + soft scanlines.
Shader "BS/Ghost"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.5, 1.6, 2, 0.35)
        _Fill ("Fill", Range(0, 1)) = 0.18
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Fill;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 normal : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; };

            V vert(A i)
            {
                V o;
                o.wp = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(o.wp);
                o.n = TransformObjectToWorldNormal(i.normal);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 v = normalize(GetWorldSpaceViewDir(i.wp));
                float rim = pow(1.0 - saturate(dot(n, v)), 2.0);
                float scan = 0.75 + 0.25 * sin(i.wp.y * 60.0 - _Time.y * 6.0);
                float a = saturate(_Fill + rim) * _Color.a * scan;
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
}
