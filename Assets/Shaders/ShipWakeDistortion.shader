Shader "SeaOfCorsair/ShipWakeDistortion"
{
    Properties
    {
        [MainColor] _FoamColor ("Foam Color", Color) = (0.78, 0.93, 1.0, 0.7)
        [NoScaleOffset] _NormalMap ("Distortion Normal", 2D) = "bump" {}
        _Alpha ("Opacity", Range(0, 1)) = 0.7
        _Fade ("Global Fade", Range(0, 1)) = 1
        _FoamStrength ("Foam Strength", Range(0, 2)) = 0.45
        _DistortionStrength ("Distortion Strength", Range(0, 0.1)) = 0.02
        _WaveAmplitude ("Vertex Displacement", Range(0, 0.3)) = 0.05
        _WaveFrequency ("Wave Frequency", Range(1, 24)) = 11
        _WaveSpeed ("Wave Speed", Range(0, 12)) = 3.2
        _NoiseTiling ("Noise Tiling", Range(0.01, 2)) = 0.24
        _NoiseScroll ("Noise Scroll", Range(0, 1)) = 0.08
        _EdgePower ("Edge Feather", Range(0.25, 4)) = 1.6
        _VerticalOffset ("Water Lift", Range(0, 0.15)) = 0.02
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+25"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        LOD 100
        Cull Off
        ColorMask RGB
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual

        Stencil
        {
            Ref 64
            ReadMask 64
            WriteMask 64
            Comp NotEqual
            Pass Replace
            Fail Keep
            ZFail Keep
        }

        Pass
        {
            Name "Forward"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _FoamColor;
                half _Alpha;
                half _Fade;
                half _FoamStrength;
                half _DistortionStrength;
                half _WaveAmplitude;
                half _WaveFrequency;
                half _WaveSpeed;
                half _NoiseTiling;
                half _NoiseScroll;
                half _EdgePower;
                half _VerticalOffset;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float2 noiseUV : TEXCOORD2;
                half4 color : COLOR;
            };

            float2 SampleNormalRG(float2 uv)
            {
                return SAMPLE_TEXTURE2D_LOD(_NormalMap, sampler_NormalMap, uv, 0).xy * 2.0 - 1.0;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float2 worldNoiseUV = positionWS.xz * _NoiseTiling;
                worldNoiseUV += float2(_Time.y * _NoiseScroll, -_Time.y * (_NoiseScroll * 0.75));

                float edgeMask = pow(saturate(1.0 - abs(input.uv.y * 2.0 - 1.0)), _EdgePower);
                float2 normalRG = SampleNormalRG(worldNoiseUV);

                float primaryWave = sin(input.uv.x * _WaveFrequency - _Time.y * _WaveSpeed + normalRG.x * 2.5);
                float secondaryWave = cos((positionWS.x + positionWS.z) * 0.2 - _Time.y * (_WaveSpeed * 0.6));
                float wave = (primaryWave + secondaryWave) * 0.5;

                positionWS.y += _VerticalOffset + (wave * _WaveAmplitude * edgeMask);
                positionWS.xz += normalRG * (_WaveAmplitude * 0.2 * edgeMask);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.uv = input.uv;
                output.noiseUV = worldNoiseUV;
                output.color = input.color;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float2 normalRG = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.noiseUV).xy * 2.0 - 1.0;

                float edgeMask = pow(saturate(1.0 - abs(input.uv.y * 2.0 - 1.0)), _EdgePower);
                half fade = saturate(_Fade);
                float trailMask = saturate(input.color.a) * edgeMask;

                float2 distortedUV = screenUV + (normalRG * _DistortionStrength * trailMask * fade);
                half3 sceneColor = SampleSceneColor(distortedUV);

                half foam = saturate((0.35 + abs(normalRG.x) + abs(normalRG.y)) * _FoamStrength * trailMask * fade);
                half3 wakeColor = sceneColor + (_FoamColor.rgb * foam * _FoamColor.a);
                half alpha = saturate(_Alpha * trailMask * fade);

                clip(alpha - 0.01h);

                return half4(wakeColor, alpha);
            }
            ENDHLSL
        }
    }
}
