Shader "FPLab/Dust Light Shaft"
{
    Properties
    {
        _BaseColor ("Dust light colour", Color) = (0.24, 0.34, 0.46, 0.06)
        _SoftDistance ("Intersection fade metres", Range(0.01, 0.5)) = 0.18
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "Dust Light Shaft"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _SoftDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float across = saturate(1.0 - abs(input.uv.x * 2.0 - 1.0));
                float ends = smoothstep(0.0, 0.13, input.uv.y) * (1.0 - smoothstep(0.4, 1.0, input.uv.y));
                // Hide the edge of either crossed card instead of exposing a flat sheet.
                float facing = abs(dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS)));
                float angleFade = smoothstep(0.06, 0.48, facing);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float shaftDepth = -TransformWorldToView(input.positionWS).z;
                float intersection = saturate((sceneDepth - shaftDepth) / max(_SoftDistance, 0.01));
                float dust = frac(sin(dot(floor(input.positionWS * 85.0), float3(12.9898, 78.233, 37.719))) * 43758.5453);
                float opacity = across * across * ends * angleFade * intersection * lerp(0.82, 1.0, dust);
                return half4(_BaseColor.rgb, _BaseColor.a * opacity);
            }
            ENDHLSL
        }
    }
}
