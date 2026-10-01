Shader "NewGaza/Animated Mediterranean Sea"
{
    Properties
    {
        _DeepColor ("Deep Sea", Color) = (0.1255, 0.3529, 0.4706, 1)
        _ShallowColor ("Coastal Sea", Color) = (0.3059, 0.5882, 0.6235, 1)
        _FoamColor ("Shore Foam", Color) = (0.66, 0.81, 0.80, 1)
        _WaveSpeed ("Wave Speed", Range(0, 2)) = 0.8
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "MediterraneanSea"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _ShallowColor;
                half4 _FoamColor;
                half _WaveSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 worldXZ = input.positionWS.xz;
                float time = _Time.y * _WaveSpeed;

                // Two small analytic wave slopes animate the lighting without
                // moving the mesh or disturbing its CPU-matched shoreline.
                // One map unit is 20m: metre-scale wave spacing, not city-size waves.
                float phaseA = dot(worldXZ, float2(3.2, 4.8)) + time * 0.8;
                float phaseB = dot(worldXZ, float2(-4.2, 2.4)) - time * 0.62;
                float slopeX = cos(phaseA) * 0.003 * 3.2
                             + cos(phaseB) * 0.002 * -4.2;
                float slopeZ = cos(phaseA) * 0.003 * 4.8
                             + cos(phaseB) * 0.002 * 2.4;
                half3 normalWS = normalize(half3(-slopeX, 1.0, -slopeZ));

                // Mesh UVs run from deep water (u=0) to the coast (u=1).
                half coastGradient = smoothstep(0.04h, 1.0h, saturate(input.uv.x));
                half3 seaColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, coastGradient);

                float glintPattern = 0.5 + 0.5
                    * sin(dot(worldXZ, float2(6.2, 3.4)) + time * 0.9)
                    * sin(dot(worldXZ, float2(-2.8, 5.4)) - time * 0.7);
                seaColor += _ShallowColor.rgb * (0.025h * glintPattern);

                // Restrict broken, soft wave crests to the last few percent of
                // the shoreward UV range; no repeating grid crosses open water.
                half shoreMask = smoothstep(0.995h, 0.9995h, saturate(input.uv.x));
                float shoreWaves = sin(dot(worldXZ, float2(1.6, 6.2)) + time * 0.7)
                                 + 0.35 * sin(dot(worldXZ, float2(-4.2, 1.2)) - time);
                half foam = shoreMask * smoothstep(0.68, 1.15, shoreWaves) * 0.34h;
                seaColor = lerp(seaColor, _FoamColor.rgb, foam);

                Light mainLight = GetMainLight();
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 litColor = seaColor
                    * (0.55h + ambient * 0.35h + mainLight.color * diffuse * 0.3h);

                half3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                half3 halfDir = normalize(mainLight.direction + viewDir);
                half specular = pow(saturate(dot(normalWS, halfDir)), 40.0h) * 0.12h;
                litColor += mainLight.color * specular;

                return half4(litColor, 1.0h);
            }
            ENDHLSL
        }
    }
}