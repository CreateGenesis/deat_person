Shader "Hidden/DataPerson/ForegroundMaskFilter"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_PreviousMask);
            SAMPLER(sampler_PreviousMask);

            float4 _MainTex_TexelSize;
            float _DilationPixels;
            float _Attack;
            float _Release;

            struct Attributes
            {
                float4 positionOS : POSITION;
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
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 stepSize = _MainTex_TexelSize.xy * _DilationPixels;
                float current = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).r;
                current = max(current, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(stepSize.x, 0)).r);
                current = max(current, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(stepSize.x, 0)).r);
                current = max(current, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(0, stepSize.y)).r);
                current = max(current, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(0, stepSize.y)).r);
                current = max(current, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + stepSize).r);
                current = max(current, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - stepSize).r);
                current = max(current, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(stepSize.x, -stepSize.y)).r);
                current = max(current, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(-stepSize.x, stepSize.y)).r);

                float previous = SAMPLE_TEXTURE2D(_PreviousMask, sampler_PreviousMask, input.uv).r;
                float response = current > previous ? _Attack : _Release;
                float filtered = lerp(previous, current, saturate(response));
                return filtered.xxxx;
            }
            ENDHLSL
        }
    }
}
