Shader "Hidden/DataPerson/ForegroundOccluder"
{
    Properties
    {
        _MaskTex ("Foreground Mask", 2D) = "black" {}
        _Threshold ("Threshold", Range(0, 1)) = 0.55
        _FlipY ("Flip Y", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry-10"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForegroundDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ColorMask 0
            ZWrite On
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            float _Threshold;
            float _FlipY;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = float4(input.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                uv.y = lerp(uv.y, 1.0 - uv.y, saturate(_FlipY));
                float foreground = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv).r;
                clip(foreground - _Threshold);
                return 0;
            }
            ENDHLSL
        }
    }
}
