#ifndef ENTRANCE_FLOOR_VARIATION_INPUT_INCLUDED
#define ENTRANCE_FLOOR_VARIATION_INPUT_INCLUDED

// Supply only the material input; URP's native Lit passes own all lighting.
#define UNIVERSAL_LIT_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/SurfaceType.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseMap_TexelSize;
    float4 _VariationOffset;
    half4 _BaseColor;
    half4 _EmissionColor;
    half _Smoothness;
    half _Metallic;
    half _BumpScale;
    float _VariationScale;
    half _VariationStrength;
    UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

TEXTURE2D(_VariantA); SAMPLER(sampler_VariantA);
TEXTURE2D(_VariantB); SAMPLER(sampler_VariantB);
TEXTURE2D(_VariantC); SAMPLER(sampler_VariantC);

float FloorHash(float2 samplePoint)
{
    float3 value = frac(samplePoint.xyx * float3(0.1031, 0.1030, 0.0973));
    value += dot(value, value.yzx + 33.33);
    return frac((value.x + value.y) * value.z);
}

float FloorNoise(float2 samplePoint)
{
    float2 cell = floor(samplePoint);
    float2 blend = frac(samplePoint);
    // Quintic interpolation keeps both slope and curvature continuous at cells.
    blend = blend * blend * blend * (blend * (blend * 6.0 - 15.0) + 10.0);
    float lower = lerp(FloorHash(cell), FloorHash(cell + float2(1, 0)), blend.x);
    float upper = lerp(FloorHash(cell + float2(0, 1)), FloorHash(cell + 1.0), blend.x);
    return lerp(lower, upper, blend.y);
}

half4 FloorWeights(float2 uv)
{
    // The mesh UVs continue across the entire maze. Cell boundaries never enter
    // this calculation, and the seed changes only the broad material regions.
    float2 samplePoint = uv * _VariationScale + _VariationOffset.xy;
    float2 warp = float2(FloorNoise(samplePoint * 0.73 + 7.1), FloorNoise(samplePoint * 0.73 + 41.7));
    samplePoint += (warp - 0.5) * 0.75;
    float x = FloorNoise(samplePoint) * 0.78 + FloorNoise(samplePoint * 2.03 + 17.4) * 0.22;
    float y = FloorNoise(float2(-samplePoint.y, samplePoint.x) + 31.8) * 0.78
            + FloorNoise(samplePoint * 1.97 + 63.1) * 0.22;
    half2 blend = smoothstep(0.28, 0.72, float2(x, y));
    // Bilinear weights are normalized by construction; broad zones blend softly.
    return half4((1 - blend.x) * (1 - blend.y), blend.x * (1 - blend.y),
                 (1 - blend.x) * blend.y, blend.x * blend.y);
}

half4 SampleMetallicSpecGloss(float2 uv, half albedoAlpha)
{
    return half4(_Metallic, _Metallic, _Metallic, _Smoothness);
}

void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surfaceData)
{
    half3 baseAlbedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
    // Rotations/offsets change repeats without changing the aggregate's size.
    half3 a = SAMPLE_TEXTURE2D(_VariantA, sampler_VariantA, float2(-uv.y, uv.x) + float2(0.37, 0.61)).rgb;
    half3 b = SAMPLE_TEXTURE2D(_VariantB, sampler_VariantB, -uv + float2(0.73, 0.19)).rgb;
    half3 c = SAMPLE_TEXTURE2D(_VariantC, sampler_VariantC, float2(uv.y, -uv.x) + float2(0.23, 0.83)).rgb;
    half4 weights = FloorWeights(uv);
    half3 variedAlbedo = baseAlbedo * weights.x + a * weights.y + b * weights.z + c * weights.w;

    surfaceData = (SurfaceData)0;
    surfaceData.albedo = lerp(baseAlbedo, variedAlbedo, saturate(_VariationStrength)) * _BaseColor.rgb;
    surfaceData.alpha = 1.0;
    surfaceData.metallic = _Metallic;
    surfaceData.specular = half3(0.0, 0.0, 0.0);
    surfaceData.smoothness = _Smoothness;
    surfaceData.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    surfaceData.occlusion = 1.0;
    // Default/setup emission is black, preserving the dark unlit areas.
    surfaceData.emission = _EmissionColor.rgb;
}

#endif
