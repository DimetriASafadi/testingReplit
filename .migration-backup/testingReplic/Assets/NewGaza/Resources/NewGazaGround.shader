Shader "NewGaza/Ground"
{
    Properties
    {
        _BaseMap ("Ground albedo", 2D) = "white" {}
        _BaseColor ("Ground tint", Color) = (1,1,1,1)
        _WorldScale ("Tiles per world unit", Float) = 2.5
        _CoarseBlend ("Broad texture variation", Range(0,0.4)) = 0.18
        _MacroStrength ("Large-scale earth variation", Range(0,0.25)) = 0.12
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _WorldScale;
            half _CoarseBlend;
            half _MacroStrength;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half fog : TEXCOORD2;
            float4 screenPos : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.fog = ComputeFogFactor(output.positionCS.z);
            output.screenPos = ComputeScreenPos(output.positionCS);
            return output;
        }
        float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
        float EarthNoise(float2 p)
        {
            float2 cell = floor(p);
            float2 fraction = frac(p);
            fraction = fraction * fraction * (3.0 - 2.0 * fraction);
            return lerp(lerp(Hash(cell),Hash(cell+float2(1,0)),fraction.x),
                        lerp(Hash(cell+float2(0,1)),Hash(cell+float2(1,1)),fraction.x),fraction.y);
        }
        half4 GroundFrag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            // World coordinates make batched quads / differently sized parcels
            // share the same grain scale; no giant stretched per-parcel UV image.
            float2 uv = input.positionWS.xz * _WorldScale;
            half3 fine = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb;
            float2 coarseUV = float2(uv.y,-uv.x) * 0.173 + float2(23.1,17.7);
            half3 coarse = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,coarseUV).rgb;
            half variation = (EarthNoise(uv * 0.014) - 0.5) * _MacroStrength;
            half3 albedo = lerp(fine,coarse,_CoarseBlend) * (1.0 + variation) * _BaseColor.rgb;
            half3 normalWS = normalize(input.normalWS);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadowCoord = input.screenPos;
            #else
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #endif
            Light sun = GetMainLight(shadowCoord);
            half3 ambient = max(SampleSH(normalWS),half3(0.24,0.25,0.27));
            half3 diffuse = saturate(dot(normalWS,sun.direction)) * sun.color *
                sun.distanceAttenuation * sun.shadowAttenuation;
            half3 color = albedo * (ambient + diffuse);
            return half4(MixFog(color,input.fog),1);
        }
        half4 DepthFrag(Varyings input) : SV_Target { return 0; }
        ENDHLSL
        Pass
        {
            Name "GroundForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment GroundFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    FallBack Off
}
