Shader "NewGaza/Soft District Fog"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.79, 0.81, 0.78, 1)
        _FogOpacity ("Fog Opacity", Range(0, 1)) = 1
        _Softness ("Radial Softness", Range(0.01, 0.9)) = 0.16
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }
        Pass
        {
            Name "SoftDistrictFog"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZTest LEqual
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                half _FogOpacity;
                half _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half radialDistance = length(input.uv * 2.0h - 1.0h);
                half radialMask = 1.0h - smoothstep(_Softness, 1.0h, radialDistance);
                half alpha = saturate(input.color.a * _FogOpacity * radialMask);
                return half4(_FogColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}