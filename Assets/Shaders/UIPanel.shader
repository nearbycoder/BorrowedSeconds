// Procedural UI panel for every card, plate and window: chamfered "watch case" corners, a lit
// brass rim with an inner bevel and shadow, an enamel gradient fill with faint grain, an outer
// glow, an animated sheen sweep and a clock-hand reveal. Draw it on a Simple Image whose rect is
// the panel plus _Pad pixels on every side (room for the glow); _Size is the full rect in pixels.
Shader "BS/UIPanel"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Size ("Size px", Vector) = (300, 100, 0, 0)
        _Pad ("Glow padding px", Float) = 24
        _Radius ("Corner radius px", Float) = 6
        _Chamfer ("Corner chamfer px", Float) = 14
        _FillTop ("Fill top", Color) = (0.11, 0.13, 0.25, 0.96)
        _FillBottom ("Fill bottom", Color) = (0.05, 0.06, 0.13, 0.96)
        _RimColor ("Rim", Color) = (0.79, 0.63, 0.35, 1)
        _RimWidth ("Rim px", Float) = 2.5
        _Inner ("Inner line", Color) = (1, 0.85, 0.55, 0.22)
        _Shadow ("Inner shadow", Range(0, 1)) = 0.55
        [HDR] _GlowColor ("Glow", Color) = (1, 0.82, 0.48, 1)
        _Glow ("Glow amount", Range(0, 2)) = 0
        _GlowWidth ("Glow px", Float) = 14
        _Sheen ("Sheen position", Float) = -2
        _SheenColor ("Sheen", Color) = (1, 0.95, 0.85, 0.22)
        _Reveal ("Reveal", Range(0, 1)) = 1
        _Grain ("Grain", Range(0, 0.2)) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; };

            float4 _Size;
            float _Pad, _Radius, _Chamfer, _RimWidth, _Shadow, _Glow, _GlowWidth, _Sheen, _Reveal, _Grain;
            fixed4 _FillTop, _FillBottom, _RimColor, _Inner, _GlowColor, _SheenColor;
            float4 _ClipRect;

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            // signed distance to a box with rounded and chamfered corners
            float panelSdf(float2 p, float2 h, float r, float c)
            {
                float2 q = abs(p) - h + r;
                float box = length(max(q, 0)) + min(max(q.x, q.y), 0) - r;
                float cut = (abs(p.x) + abs(p.y) - (h.x + h.y - c)) * 0.70710678;
                return c > 0 ? max(box, cut) : box;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(41.3, 289.1))) * 43758.5453); }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 size = _Size.xy;
                float2 p = (i.uv - 0.5) * size;
                float2 h = size * 0.5 - _Pad;
                float d = panelSdf(p, h, _Radius, _Chamfer);
                float aa = max(fwidth(d), 0.5);

                // fill: vertical enamel gradient, edge shadow, grain
                float inside = saturate(0.5 - d / aa);
                float t = saturate(p.y / max(h.y, 1) * 0.5 + 0.5);
                fixed4 fill = lerp(_FillBottom, _FillTop, t);
                float edge = saturate((-d - _RimWidth) / 18.0);
                fill.rgb *= lerp(1 - _Shadow, 1, edge);
                fill.rgb += (hash(floor(p)) - 0.5) * _Grain * 0.35;

                // rim: brushed metal lit from the upper left, with a thin highlight just inside it
                float2 n = normalize(p + 1e-4);
                float lit = 0.55 + 0.45 * dot(n, normalize(float2(-0.55, 0.85)));
                float rim = saturate(0.5 - (abs(d + _RimWidth * 0.5) - _RimWidth * 0.5) / aa);
                fixed3 rimCol = _RimColor.rgb * (0.55 + 0.75 * lit);
                float innerLine = saturate(0.5 - (abs(d + _RimWidth + 1.5) - 0.6) / aa) * (0.4 + 0.6 * saturate(n.y));

                fixed4 col = fill;
                col.rgb = lerp(col.rgb, _Inner.rgb, innerLine * _Inner.a);
                col.rgb = lerp(col.rgb, rimCol, rim * _RimColor.a);
                col.a = lerp(fill.a, max(fill.a, _RimColor.a), rim) * inside;

                // sheen: a soft diagonal band that sweeps across when _Sheen runs from -1 to 2
                float s = (p.x / size.x + p.y / size.y * 0.6) + 0.5;
                float sheen = exp(-pow((s - _Sheen) / 0.09, 2)) * inside;
                col.rgb += _SheenColor.rgb * _SheenColor.a * sheen * (1 + rim * 2);

                // glow outside the shape
                // glow fades to exactly zero at the quad edge so it never shows a box
                float gd = max(d, 0);
                float glow = _Glow * exp(-gd / max(_GlowWidth, 1)) * saturate(1 - gd / max(_Pad - 1, 1)) * (1 - inside);
                col.rgb = lerp(_GlowColor.rgb, col.rgb, col.a / max(col.a + glow * _GlowColor.a, 1e-4));
                col.a = saturate(col.a + glow * _GlowColor.a * 0.6);

                // clock-hand reveal from 12 o'clock, clockwise, with a bright hand at the front
                if (_Reveal < 0.999)
                {
                    float ang = frac(atan2(p.x, p.y) / 6.2831853 + 1);
                    float shown = saturate((_Reveal - ang) * 40);
                    float r = length(p);
                    float handPx = abs(ang - _Reveal) * 6.2831853 * r;
                    float hand = exp(-pow(handPx / 2.0, 2)) * saturate(_Reveal * 20) * saturate(1 - (_Reveal - 0.9) * 10);
                    col.a *= shown;
                    col.rgb += _GlowColor.rgb * hand * inside * 1.5;
                    col.a = max(col.a, hand * inside * 0.9);
                }

                col *= i.color;
                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
