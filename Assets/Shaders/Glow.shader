// Unlit HDR colour for beams, light strips and markers. _Mode 0 = alpha blend, 1 = additive.
Shader "BS/Glow"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Pulse ("Pulse", Range(0, 1)) = 0
        _Scroll ("Scroll", Float) = 0
        _FadeV ("Vertical fade (uv.y)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 10
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Pulse;
                float _Scroll;
                float _FadeV;
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
                float pulse = 1.0 - _Pulse * (0.5 + 0.5 * sin(_Time.y * 18.0 + i.uv.x * 6.0));
                float scroll = _Scroll > 0 ? 0.75 + 0.25 * sin(i.uv.x * 40.0 - _Time.y * _Scroll) : 1.0;
                half4 c = _Color;
                c.rgb *= pulse * scroll;
                if (_FadeV > 0) c.a *= pow(saturate(1.0 - i.uv.y), _FadeV);
                return c;
            }
            ENDHLSL
        }
    }
}
