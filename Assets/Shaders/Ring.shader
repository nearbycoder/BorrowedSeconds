// Flat radial timer drawn on a quad (uv 0..1): filled arc, dim remainder and 12 clock ticks.
Shader "BS/Ring"
{
    Properties
    {
        [HDR] _Color ("Fill Color", Color) = (0.5, 1.8, 2, 1)
        _BackColor ("Back Color", Color) = (1, 1, 1, 0.12)
        _Fill ("Fill", Range(0, 1)) = 1
        _Inner ("Inner Radius", Range(0, 1)) = 0.72
        _Outer ("Outer Radius", Range(0, 1)) = 0.92
        _Ticks ("Ticks", Float) = 12
        _Alpha ("Alpha", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _BackColor;
                float _Fill;
                float _Inner;
                float _Outer;
                float _Ticks;
                float _Alpha;
            CBUFFER_END

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.uv = i.uv;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float aa = fwidth(r) * 1.5;
                float band = smoothstep(_Inner - aa, _Inner + aa, r) * (1.0 - smoothstep(_Outer - aa, _Outer + aa, r));
                // angle clockwise from 12 o'clock, 0..1
                float ang = frac(atan2(p.x, p.y) / 6.2831853 + 1.0);
                float filled = step(ang, _Fill);
                float tickPhase = frac(ang * _Ticks);
                float tick = (1.0 - smoothstep(0.0, 0.06, min(tickPhase, 1.0 - tickPhase))) * band;
                half4 c = lerp(_BackColor, _Color, filled);
                c.a *= band;
                c.rgb = lerp(c.rgb, c.rgb * 0.35, tick * 0.6);
                c.a *= _Alpha;
                return c;
            }
            ENDHLSL
        }
    }
}
