Shader "SeaOfCorsair/ShipWakeRibbon"
{
    Properties
    {
        _FoamColor ("Foam Color", Color) = (0.92, 0.97, 1.0, 1.0)
        _EdgeColor ("Edge Color", Color) = (0.62, 0.85, 0.95, 1.0)
        _WakeOpacity ("Wake Opacity", Range(0, 1)) = 0.9
        _DistortionStrength ("Distortion Strength", Range(0, 0.08)) = 0.03
        _NoiseScale ("Noise Scale", Float) = 0.14
        _NoiseSpeed ("Noise Speed", Float) = 0.35
        _TailFade ("Tail Fade", Range(0.1, 4.0)) = 1.3
        _SoftEdge ("Soft Edge", Range(0.05, 1.0)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 worldXZ : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _FoamColor;
                half4 _EdgeColor;
                half _WakeOpacity;
                half _DistortionStrength;
                half _NoiseScale;
                half _NoiseSpeed;
                half _TailFade;
                half _SoftEdge;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                value += Noise(p) * amplitude;
                p = p * 2.03 + 31.2;
                amplitude *= 0.5;
                value += Noise(p) * amplitude;
                p = p * 2.01 + 17.7;
                amplitude *= 0.5;
                value += Noise(p) * amplitude;
                return value;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexPosition = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexPosition.positionCS;
                output.uv = input.uv;
                output.worldXZ = vertexPosition.positionWS.xz;
                output.screenPos = ComputeScreenPos(vertexPosition.positionCS);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centeredUv = input.uv * 2.0 - 1.0;
                float acrossMask = saturate(1.0 - abs(centeredUv.y));
                acrossMask = smoothstep(0.0, max(0.001, _SoftEdge), acrossMask);

                float tailFade = saturate(1.0 - pow(saturate(input.uv.x), _TailFade));
                float timeOffset = _Time.y * _NoiseSpeed;
                float2 noiseUv = input.worldXZ * _NoiseScale + float2(timeOffset, -timeOffset * 0.6);
                float breakup = Fbm(noiseUv);
                float fineBreakup = Fbm(noiseUv * 2.4 + 11.0);
                float foamMask = saturate(acrossMask * tailFade * 1.35 + breakup * 0.45 - 0.25);
                foamMask *= smoothstep(0.18, 0.9, fineBreakup + acrossMask * 0.65);

                float2 distortionVector = float2(
                    Fbm(noiseUv + float2(3.1, 8.7)) - 0.5,
                    Fbm(noiseUv + float2(9.4, 1.3)) - 0.5);
                distortionVector *= _DistortionStrength * foamMask;

                float2 screenUv = input.screenPos.xy / max(input.screenPos.w, 0.0001);
                half3 refracted = SampleSceneColor(screenUv + distortionVector);

                half foamBand = saturate(acrossMask * 1.1 + breakup * 0.25);
                half3 wakeColor = lerp(_EdgeColor.rgb, _FoamColor.rgb, foamBand) * input.color.rgb;
                wakeColor += _FoamColor.rgb * (foamMask * 0.12);

                half foamBlend = saturate(foamMask * 0.85 + acrossMask * 0.2);
                half3 finalColor = lerp(refracted, wakeColor, foamBlend);
                half alpha = saturate(foamMask * _WakeOpacity * input.color.a);

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
