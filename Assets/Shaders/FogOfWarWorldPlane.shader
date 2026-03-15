Shader "BoatAttack/FogOfWarWorldPlane"
{
    Properties
    {
        _FogMask("Fog Mask", 2D) = "black" {}
        _FogTint("Fog Tint", Color) = (0.48, 0.52, 0.56, 1)
        _HiddenAlpha("Hidden Alpha", Range(0, 1)) = 0.8
        _Density("Density", Range(0, 2)) = 1
        _NoiseContrast("Noise Contrast", Range(0.5, 3)) = 1.45
        _PrimaryScale("Primary Scale", Float) = 0.075
        _SecondaryScale("Secondary Scale", Float) = 0.16
        _DetailScale("Detail Scale", Float) = 0.42
        _WarpStrength("Warp Strength", Range(0, 1.5)) = 0.55
        _FlowA("Flow A", Vector) = (0.02, 0.01, 0, 0)
        _FlowB("Flow B", Vector) = (-0.018, 0.014, 0, 0)
        _FlowC("Flow C", Vector) = (0.028, -0.022, 0, 0)
        _EdgeSoftness("Edge Softness", Range(0, 6)) = 2.2
        _LightScatterStrength("Light Scatter Strength", Range(0, 1)) = 0.28
        _LightScatterPower("Light Scatter Power", Range(1, 8)) = 4
        _RimStrength("Rim Strength", Range(0, 1)) = 0.24
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "FogOfWarPlane"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            TEXTURE2D(_FogMask);
            SAMPLER(sampler_FogMask);

            float4 _FogTexelSize;
            half4 _FogTint;
            half _HiddenAlpha;
            half _Density;
            half _NoiseContrast;
            half _PrimaryScale;
            half _SecondaryScale;
            half _DetailScale;
            half _WarpStrength;
            float4 _FlowA;
            float4 _FlowB;
            float4 _FlowC;
            half _EdgeSoftness;
            half _LightScatterStrength;
            half _LightScatterPower;
            half _RimStrength;

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float ValueNoise(float2 uv)
            {
                float2 cell = floor(uv);
                float2 localUv = frac(uv);
                localUv = localUv * localUv * (3.0 - 2.0 * localUv);

                float bottomLeft = Hash21(cell);
                float bottomRight = Hash21(cell + float2(1.0, 0.0));
                float topLeft = Hash21(cell + float2(0.0, 1.0));
                float topRight = Hash21(cell + float2(1.0, 1.0));

                float bottom = lerp(bottomLeft, bottomRight, localUv.x);
                float top = lerp(topLeft, topRight, localUv.x);
                return lerp(bottom, top, localUv.y);
            }

            float FractalNoise(float2 uv)
            {
                float noise = 0.0;
                float amplitude = 0.5;
                float2 currentUv = uv;

                [unroll(4)]
                for (int octave = 0; octave < 4; octave++)
                {
                    noise += ValueNoise(currentUv) * amplitude;
                    currentUv = mul(float2x2(1.6, -1.2, 1.2, 1.6), currentUv) * 1.07 + 19.19;
                    amplitude *= 0.5;
                }

                return noise;
            }

            float SampleVisibility(float2 uv)
            {
                return saturate(SAMPLE_TEXTURE2D(_FogMask, sampler_FogMask, uv).r);
            }

            float SampleEdgeFade(float2 uv, float visibility)
            {
                float neighborAverage =
                    SampleVisibility(uv + float2(_FogTexelSize.x, 0.0)) +
                    SampleVisibility(uv - float2(_FogTexelSize.x, 0.0)) +
                    SampleVisibility(uv + float2(0.0, _FogTexelSize.y)) +
                    SampleVisibility(uv - float2(0.0, _FogTexelSize.y));
                neighborAverage *= 0.25;

                float edge = saturate(abs(visibility - neighborAverage) * _EdgeSoftness);
                return lerp(1.0, 0.4, edge);
            }

            float SampleFogPattern(float2 worldUv, float time)
            {
                float2 secondaryUv = worldUv * _SecondaryScale + time * _FlowB.xy;
                float2 warp = float2(
                    FractalNoise(secondaryUv + 11.3),
                    FractalNoise(secondaryUv - 7.1));

                float2 primaryUv = worldUv * _PrimaryScale + time * _FlowA.xy + warp * _WarpStrength;
                float primary = FractalNoise(primaryUv);
                float detail = FractalNoise(worldUv * _DetailScale + time * _FlowC.xy + primary);

                float fogShape = saturate(primary * 0.75 + detail * 0.35);
                fogShape = smoothstep(0.15, 0.95, fogShape);
                fogShape = pow(fogShape, _NoiseContrast);
                return saturate(fogShape * _Density);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(float3(0.0, 0.0, 1.0));
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half visibility = SampleVisibility(uv);
                half hiddenAmount = saturate(1.0h - visibility);

                if (hiddenAmount <= 0.001h)
                {
                    return half4(0.0h, 0.0h, 0.0h, 0.0h);
                }

                float3 viewDirection = SafeNormalize(_WorldSpaceCameraPos - input.positionWS);
                float fogPattern = SampleFogPattern(input.positionWS.xz, _Time.y);
                float edgeFade = SampleEdgeFade(uv, visibility);
                Light mainLight = GetMainLight();
                float scattering = pow(saturate(dot(viewDirection, -mainLight.direction)), _LightScatterPower) * _LightScatterStrength;
                float rim = pow(1.0 - saturate(dot(viewDirection, normalize(input.normalWS))), 2.0) * _RimStrength;

                float3 fogColor = _FogTint.rgb * (0.82 + fogPattern * 0.35 + rim);
                fogColor += mainLight.color.rgb * scattering * 0.2;

                half alpha = hiddenAmount * _HiddenAlpha * edgeFade * lerp(0.55h, 1.0h, fogPattern);
                alpha = saturate(alpha);

                return half4(fogColor, alpha);
            }
            ENDHLSL
        }
    }
}
