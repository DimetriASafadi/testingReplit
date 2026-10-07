Shader "NewGaza/Soft Dust"
{
    Properties
    {
        _Tint ("Dust Tint", Color) = (0.72, 0.64, 0.52, 0.42)
        _DustTex ("Soft Radial Alpha", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "SoftDust"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DustTex);
            SAMPLER(sampler_DustTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float4 _DustTex_ST;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _Tint;
                output.uv = TRANSFORM_TEX(input.uv, _DustTex);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_DustTex, sampler_DustTex, input.uv).a;
                return half4(input.color.rgb, input.color.a * alpha);
            }
            ENDHLSL
        }
    }
}