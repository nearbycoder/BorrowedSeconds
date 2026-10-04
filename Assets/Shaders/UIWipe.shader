// Full-screen screen-to-screen transition: a clock hand sweeps from 12 o'clock and the dial it
// leaves behind covers the screen (_Progress 0..1), then keeps sweeping to uncover it (1..2).
// The cover is a dark enamel dial with tick marks; the hand is a bright ice line with a halo.
Shader "BS/UIWipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Progress ("Progress", Range(0, 2)) = 0
        _Aspect ("Aspect", Float) = 1.7777
        _Ink ("Ink", Color) = (0.03, 0.035, 0.075, 1)
        _Line ("Ticks", Color) = (0.35, 0.42, 0.75, 1)
        [HDR] _Hand ("Hand", Color) = (0.6, 2.2, 2.6, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            float _Progress, _Aspect;
            fixed4 _Ink, _Line, _Hand;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * float2(_Aspect, 1);
                float r = length(p);
                float ang = frac(atan2(p.x, p.y) / 6.2831853 + 1);
                float soft = 0.004 / max(r, 0.02);
                float cover = _Progress <= 1 ? saturate((_Progress - ang) / soft) : saturate((ang - (_Progress - 1)) / soft);
                // dial decoration on the cover
                float tickA = frac(ang * 60);
                float tick = (1 - smoothstep(0.0, 0.06, min(tickA, 1 - tickA))) * step(0.36, r) * step(r, 0.41);
                float tick5A = frac(ang * 12);
                float tick5 = (1 - smoothstep(0.0, 0.03, min(tick5A, 1 - tick5A))) * step(0.33, r) * step(r, 0.41);
                float ring = 1 - smoothstep(0.0, 0.0025, abs(r - 0.43));
                float hub = 1 - smoothstep(0.012, 0.016, r);
                fixed3 col = _Ink.rgb + _Line.rgb * saturate(tick * 0.5 + tick5 * 0.9 + ring * 0.6) * 0.35;
                // the hand: a line along the front angle
                float front = frac(_Progress);
                float da = abs(ang - front);
                da = min(da, 1 - da) * 6.2831853 * r;
                float active = step(0.001, _Progress) * step(_Progress, 1.999);
                float hand = (1 - smoothstep(0.0, 0.004, da)) * active;
                float halo = exp(-da * 60) * active * 0.6;
                col += _Hand.rgb * (hand + halo) + _Hand.rgb * hub * active;
                float a = saturate(cover + hand + halo * 0.5) * _Ink.a;
                return fixed4(col, a) * i.color;
            }
            ENDCG
        }
    }
}
