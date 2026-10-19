#ifndef ENTRANCE_WALL_BLEND_INPUT_INCLUDED
#define ENTRANCE_WALL_BLEND_INPUT_INCLUDED

// URP owns the lighting passes; this file supplies their material surface data.
#define UNIVERSAL_LIT_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/SurfaceType.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseMap_TexelSize;
    float4 _WallMapParams;
    half4 _BaseColor;
    half4 _EmissionColor;
    half _Smoothness;
    half _Metallic;
    half _BumpScale;
    float _WallCellSize;
    float _TransitionWidth;
    float _TransitionNoise;
    UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

TEXTURE2D(_VariantA); SAMPLER(sampler_VariantA);
TEXTURE2D(_VariantB); SAMPLER(sampler_VariantB);
TEXTURE2D(_NormalA); SAMPLER(sampler_NormalA);
TEXTURE2D(_NormalB); SAMPLER(sampler_NormalB);
TEXTURE2D(_WallStyleMap); SAMPLER(sampler_WallStyleMap);

float WallHash(float2 samplePoint)
{
    float3 value = frac(samplePoint.xyx * float3(0.1031, 0.1030, 0.0973));
    value += dot(value, value.yzx + 33.33);
    return frac((value.x + value.y) * value.z);
}

float WallNoise(float2 samplePoint)
{
    float2 cell = floor(samplePoint);
    float2 blend = frac(samplePoint);
    blend = blend * blend * blend * (blend * (blend * 6.0 - 15.0) + 10.0);
    float lower = lerp(WallHash(cell), WallHash(cell + float2(1, 0)), blend.x);
    float upper = lerp(WallHash(cell + float2(0, 1)), WallHash(cell + 1.0), blend.x);
    return lerp(lower, upper, blend.y);
}

half3 WallStyleWeights(float3 positionOS)
{
    // A position-based field continues around corners and across mesh batches.
    // Height-dependent noise breaks up straight finish edges without moving UVs
    // or distorting the tile grout and plaster texture itself.
    float2 samplePoint = positionOS.xz * 2.7 + positionOS.y * float2(1.31, -1.73);
    float2 warp = float2(WallNoise(samplePoint), WallNoise(samplePoint + 29.37));
    float2 detail = float2(WallNoise(samplePoint * 3.17 + 5.9), WallNoise(samplePoint * 3.17 + 57.1));
    warp = (warp * 0.8 + detail * 0.2 - 0.5) * (2.0 * _TransitionNoise);
    float2 mapUV = (positionOS.xz + warp) * _WallMapParams.xy + _WallMapParams.zw;
    // Explicit LOD zero retains the authored labels, even at oblique angles.
    float3 weights = max(SAMPLE_TEXTURE2D_LOD(_WallStyleMap, sampler_WallStyleMap, mapUV, 0).rgb, 0.0);
    // Bilinear map samples cover one cell. Sharpen only their weights to keep
    // the visible transition near the requested width instead of the whole cell.
    float sharpness = clamp(_WallCellSize / max(_TransitionWidth, 0.05), 1.0, 32.0);
    // Scale before exponentiation so narrow transitions do not underflow at
    // three-way junctions; this leaves the normalized result unchanged.
    weights /= max(max(weights.x, max(weights.y, weights.z)), 0.00001);
    weights = pow(weights, sharpness);
    float total = dot(weights, float3(1.0, 1.0, 1.0));
    return total > 0.00001 ? (half3)(weights / total) : half3(1.0, 0.0, 0.0);
}

half4 SampleMetallicSpecGloss(float2 uv, half albedoAlpha)
{
    return half4(_Metallic, _Metallic, _Metallic, _Smoothness);
}

void InitializeWallSurfaceData(float2 uv, float3 positionOS, out SurfaceData surfaceData)
{
    half3 weights = WallStyleWeights(positionOS);
    half3 original = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
    half3 variantA = SAMPLE_TEXTURE2D(_VariantA, sampler_VariantA, uv).rgb;
    half3 variantB = SAMPLE_TEXTURE2D(_VariantB, sampler_VariantB, uv).rgb;
    half3 originalNormal = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    half3 normalA = SampleNormal(uv, TEXTURE2D_ARGS(_NormalA, sampler_NormalA), _BumpScale);
    half3 normalB = SampleNormal(uv, TEXTURE2D_ARGS(_NormalB, sampler_NormalB), _BumpScale);

    surfaceData = (SurfaceData)0;
    surfaceData.albedo = (original * weights.x + variantA * weights.y + variantB * weights.z) * _BaseColor.rgb;
    surfaceData.alpha = 1.0;
    surfaceData.metallic = _Metallic;
    surfaceData.specular = half3(0.0, 0.0, 0.0);
    surfaceData.smoothness = _Smoothness;
    surfaceData.normalTS = normalize(originalNormal * weights.x + normalA * weights.y + normalB * weights.z);
    surfaceData.occlusion = 1.0;
    surfaceData.emission = _EmissionColor.rgb;
}

// Auxiliary URP passes without a position interpolator retain the base surface.
void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surfaceData)
{
    surfaceData = (SurfaceData)0;
    surfaceData.albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb * _BaseColor.rgb;
    surfaceData.alpha = 1.0;
    surfaceData.metallic = _Metallic;
    surfaceData.specular = half3(0.0, 0.0, 0.0);
    surfaceData.smoothness = _Smoothness;
    surfaceData.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    surfaceData.occlusion = 1.0;
    surfaceData.emission = _EmissionColor.rgb;
}

#endif
