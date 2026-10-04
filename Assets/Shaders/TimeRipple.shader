// Full-screen "time distortion": up to two expanding radial ripples (a loan snapping onto an
// obstacle, a debt snapping onto the player) that bend the image and split its colour channels
// along the ripple front, plus a faint radial smear while time runs backwards.
// Driven by globals set from WorldEnvironment; rendered by a FullScreenPassRendererFeature.
Shader "BS/TimeRipple"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        Pass
        {
            Name "TimeRipple"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _BS_RippleA;   // xy centre (viewport uv), z age (s, <0 = off), w strength
            float4 _BS_RippleB;
            float4 _BS_TimeFx;    // x rewind amount, y aspect (w/h)

            float2 Ripple(float2 uv, float4 r, float aspect, out float front)
            {
                front = 0;
                if (r.z < 0 || r.w <= 0) return 0;
                float2 d = uv - r.xy;
                d.x *= aspect;
                float dist = length(d);
                float radius = r.z * 1.35;
                float width = 0.035 + r.z * 0.06;
                float band = saturate(1 - abs(dist - radius) / width);
                band = band * band * (3 - 2 * band);
                float fade = saturate(1 - r.z / 0.75);
                front = band * fade * r.w;
                float2 dir = dist > 1e-4 ? d / dist : 0;
                dir.x /= aspect;
                // push outward on the leading half of the front, inward on the trailing half
                float wave = sin(saturate((dist - radius) / width * 0.5 + 0.5) * 6.2831853);
                return dir * wave * front * 0.022;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.texcoord;
                float aspect = max(_BS_TimeFx.y, 0.1);
                float fa, fb;
                float2 off = Ripple(uv, _BS_RippleA, aspect, fa) + Ripple(uv, _BS_RippleB, aspect, fb);
                float front = saturate(fa + fb);

                // rewind: a gentle zoom-smear toward the centre
                float rw = _BS_TimeFx.x;
                float2 c = uv - 0.5;
                off += -c * rw * 0.012 * (0.5 + 0.5 * sin(_Time.y * 40 + length(c) * 60));

                float split = front * 0.006 + rw * 0.002;
                float2 sdir = normalize(c + 1e-5) * split;
                half r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + off + sdir).r;
                half g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + off).g;
                half b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + off - sdir).b;
                half3 col = half3(r, g, b);
                // a thin cyan glint riding the front
                col += half3(0.35, 0.9, 1.0) * front * 0.08;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
