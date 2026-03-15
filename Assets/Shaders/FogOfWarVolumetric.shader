Shader "Hidden/BoatAttack/FogOfWarVolumetric"
{
    Properties
    {
        _FogDensity("Fog Density", Range(0, 0.2)) = 0.065
        _FogMaxOpacity("Fog Max Opacity", Range(0, 1)) = 0.92
        _ExploredReveal("Explored Reveal", Range(0, 1)) = 0.35
        _VolumeHeightOffset("Volume Height Offset", Float) = 6
        _VolumeHeightFalloff("Volume Height Falloff", Float) = 20
        _DistanceBoost("Distance Boost", Range(0, 4)) = 1.2
        _NoiseScale("Noise Scale", Float) = 0.03
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "FogOfWarVolumetric"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BoatAttackFogOfWarMask);
            SAMPLER(sampler_BoatAttackFogOfWarMask);

            float4 _BoatAttackFogOfWarBounds;
            half4 _BoatAttackFogOfWarFogColor;
            half _BoatAttackFogOfWarEnabled;
            half _BoatAttackFogOfWarWaterLevel;

            half _FogDensity;
            half _FogMaxOpacity;
            half _ExploredReveal;
            half _VolumeHeightOffset;
            half _VolumeHeightFalloff;
            half _DistanceBoost;
            half _NoiseScale;
            half _NoiseStrength;

            float Hash12(float2 value)
            {
                float3 hash = frac(float3(value.xyx) * 0.1031);
                hash += dot(hash, hash.yzx + 33.33);
                return frac((hash.x + hash.y) * hash.z);
            }

            half2 SampleReveal(float3 worldPosition)
            {
                float2 fogUv = (worldPosition.xz - _BoatAttackFogOfWarBounds.xy) / max(_BoatAttackFogOfWarBounds.zw, float2(0.0001, 0.0001));
                if (fogUv.x < 0.0 || fogUv.y < 0.0 || fogUv.x > 1.0 || fogUv.y > 1.0)
                {
                    return half2(1.0h, 1.0h);
                }

                return saturate(SAMPLE_TEXTURE2D(_BoatAttackFogOfWarMask, sampler_BoatAttackFogOfWarMask, fogUv).rg);
            }

            half EvaluateDensity(float3 worldPosition, half rayProgress, half destinationReveal)
            {
                float volumeHeight = _BoatAttackFogOfWarWaterLevel + _VolumeHeightOffset;
                half heightDensity = exp(-abs(worldPosition.y - volumeHeight) / max(_VolumeHeightFalloff, 0.001h));
                half2 reveal = SampleReveal(worldPosition);
                half sampleReveal = max(reveal.x, reveal.y * _ExploredReveal);
                half unrevealed = 1.0h - max(sampleReveal, destinationReveal);

                float noiseInputX = worldPosition.x * _NoiseScale + _Time.y * 0.18;
                float noiseInputY = worldPosition.z * _NoiseScale - _Time.y * 0.11;
                half noise = lerp(1.0h, 0.65h + 0.35h * Hash12(float2(noiseInputX, noiseInputY)), _NoiseStrength);
                half distanceFactor = lerp(0.6h, 1.0h, saturate(rayProgress * _DistanceBoost));

                return _FogDensity * heightDensity * unrevealed * noise * distanceFactor;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord.xy);
                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                if (_BoatAttackFogOfWarEnabled < 0.5h)
                {
                    return sceneColor;
                }

                float rawDepth = SampleSceneDepth(uv);
#if UNITY_REVERSED_Z
                if (rawDepth <= 0.00001)
                {
                    return sceneColor;
                }
                float deviceDepth = rawDepth;
#else
                if (rawDepth >= 0.99999)
                {
                    return sceneColor;
                }
                float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
#endif

                float3 worldPosition = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                float3 rayVector = worldPosition - _WorldSpaceCameraPos;
                float rayLength = length(rayVector);

                if (rayLength <= 0.001)
                {
                    return sceneColor;
                }

                float3 rayDirection = rayVector / rayLength;
                half2 destinationRevealChannels = SampleReveal(worldPosition);
                half currentVisibility = destinationRevealChannels.x;
                if (currentVisibility >= 0.98h)
                {
                    return sceneColor;
                }

                half destinationReveal = max(currentVisibility, destinationRevealChannels.y * _ExploredReveal);
                const int stepCount = 12;
                float stepLength = rayLength / stepCount;
                half accumulatedDensity = 0.0h;

                [unroll]
                for (int stepIndex = 0; stepIndex < stepCount; stepIndex++)
                {
                    half rayProgress = (stepIndex + 0.5h) / stepCount;
                    float3 samplePosition = _WorldSpaceCameraPos + rayDirection * (rayProgress * rayLength);
                    accumulatedDensity += EvaluateDensity(samplePosition, rayProgress, destinationReveal) * stepLength;
                }

                half fogAmount = saturate(1.0h - exp(-accumulatedDensity));
                fogAmount *= (1.0h - destinationReveal);
                fogAmount = min(fogAmount, _FogMaxOpacity);

                return lerp(sceneColor, half4(_BoatAttackFogOfWarFogColor.rgb, sceneColor.a), fogAmount);
            }
            ENDHLSL
        }
    }
}
