// Frozen "out of time" crystal shell: fresnel rim, faceted sparkle, slow inner shimmer.
Shader "BS/Crystal"
{
    Properties
    {
        _Color ("Tint", Color) = (0.45, 0.9, 1, 0.22)
        [HDR] _RimColor ("Rim", Color) = (0.6, 2.2, 2.6, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.4
        _Sparkle ("Sparkle", Range(0, 2)) = 0.8
        _Fade ("Fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _RimColor;
                float _RimPower;
                float _Sparkle;
                float _Fade;
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

            float hash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }

            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 v = normalize(GetWorldSpaceViewDir(i.wp));
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                float3 cell = floor(i.wp * 9.0);
                float tw = hash(cell);
                float sparkle = step(0.965, frac(tw + _Time.y * 0.35)) * _Sparkle;
                float facet = 0.5 + 0.5 * dot(n, normalize(float3(0.4, 0.9, 0.2)));
                half3 col = _Color.rgb * (0.6 + 0.6 * facet) + _RimColor.rgb * rim + sparkle * _RimColor.rgb * 0.6;
                half a = saturate(_Color.a + rim * 0.75 + sparkle * 0.5) * _Fade;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
