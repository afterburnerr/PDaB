Shader "FPLab/UI/Loading Background Blur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Background", 2D) = "white" {}
        _BlurRadius ("Blur radius in texture pixels", Range(0, 64)) = 36
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [unity_GUIZTestMode]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            float _BlurRadius;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // A compact Gaussian kernel affects only the artwork, keeping
                // the loading caption sharp. No screen capture is required.
                const float weights[5] = { 1, 4, 6, 4, 1 };
                float2 stepUV = _MainTex_TexelSize.xy * (_BlurRadius * 0.5);
                half4 color = 0;
                [unroll] for (int y = 0; y < 5; y++)
                {
                    [unroll] for (int x = 0; x < 5; x++)
                    {
                        float2 uv = saturate(input.uv + float2(x - 2, y - 2) * stepUV);
                        color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * (weights[x] * weights[y] / 256.0);
                    }
                }
                return color * input.color;
            }
            ENDHLSL
        }
    }
}
