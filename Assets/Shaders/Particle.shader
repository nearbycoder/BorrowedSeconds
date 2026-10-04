// Procedural additive particle: 0 soft dot, 1 diamond shard, 2 ring, 3 four-point sparkle.
Shader "BS/Particle"
{
    Properties
    {
        _Shape ("Shape", Float) = 0
        _Intensity ("Intensity", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+30" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha [_DstBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Shape;
                float _Intensity;
            CBUFFER_END

            struct A { float4 pos : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.color = i.color;
                o.uv = i.uv;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float a;
                if (_Shape < 0.5) a = saturate(1.0 - r) * saturate(1.0 - r);
                else if (_Shape < 1.5) a = saturate(1.0 - (abs(p.x) * 2.2 + abs(p.y)) ) * 1.5;
                else if (_Shape < 2.5) a = saturate(1.0 - abs(r - 0.78) * 9.0);
                else a = saturate(1.0 - (abs(p.x * p.y) * 18.0 + r * 0.9)) + saturate(1.0 - r * 3.0) * 0.6;
                half4 c = i.color;
                c.rgb *= _Intensity;
                c.a *= saturate(a);
                return c;
            }
            ENDHLSL
        }
    }
}
