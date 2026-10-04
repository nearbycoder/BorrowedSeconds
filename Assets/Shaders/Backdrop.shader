// The void under the board: a deep gradient with slowly turning clock-face rings and tick marks.
Shader "BS/Backdrop"
{
    Properties
    {
        _Top ("Top", Color) = (0.11, 0.13, 0.26, 1)
        _Bottom ("Bottom", Color) = (0.05, 0.06, 0.13, 1)
        [HDR] _RingColor ("Rings", Color) = (0.35, 0.45, 0.9, 1)
        _Spin ("Spin", Float) = 0.02
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-50" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Top;
                half4 _Bottom;
                half4 _RingColor;
                float _Spin;
                half4 _Tint;
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

            float ring(float r, float radius, float width)
            {
                float aa = fwidth(r) * 1.2;
                return 1.0 - smoothstep(width - aa, width + aa, abs(r - radius));
            }

            float ticks(float2 p, float r, float radius, float count, float len, float width, float rot)
            {
                float ang = atan2(p.y, p.x) + rot;
                float s = frac(ang / 6.2831853 * count);
                float d = min(s, 1.0 - s) / count * 6.2831853 * r;
                float inBand = step(radius - len, r) * step(r, radius);
                float aa = fwidth(d) * 1.2;
                return (1.0 - smoothstep(width - aa, width + aa, d)) * inBand;
            }

            half4 frag(V i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;
                float r = length(p);
                half3 col = lerp(_Top.rgb, _Bottom.rgb, saturate(r * 1.1));
                float t = _Time.y * _Spin;
                float l = 0.0;
                l += ring(r, 0.26, 0.0015) * 0.8;
                l += ring(r, 0.42, 0.003) * 0.9;
                l += ticks(p, r, 0.42, 60.0, 0.018, 0.0016, t) * 0.8;
                l += ticks(p, r, 0.42, 12.0, 0.05, 0.004, t) * 1.0;
                l += ring(r, 0.58, 0.0012) * 0.6;
                l += ticks(p, r, 0.74, 120.0, 0.012, 0.0012, -t * 0.6) * 0.5;
                l += ring(r, 0.74, 0.0025) * 0.7;
                l += ring(r, 0.9, 0.0015) * 0.4;
                // a slow sweeping second hand
                float ang = atan2(p.x, p.y);
                float hand = frac((ang / 6.2831853) - _Time.y / 60.0);
                float sweep = smoothstep(0.0, 0.35, 1.0 - hand) * 0.10 * step(r, 0.74) * step(0.02, r);
                col += _RingColor.rgb * (l * 0.22 + sweep * 0.25) * (1.0 - smoothstep(0.75, 1.0, r));
                return half4(col * _Tint.rgb, 1);
            }
            ENDHLSL
        }
    }
}
