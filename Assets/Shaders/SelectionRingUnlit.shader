Shader "SeaOfCorsair/SelectionRingUnlit"
{
    Properties
    {
        [MainColor] _Color ("Color", Color) = (0.05, 0.8, 0.2, 1)
        _Size ("Outer Radius", Range(0, 1)) = 0.95
        _Border ("Inner Radius", Range(0, 1)) = 0.90
        _Density ("Intensity", Range(0, 50)) = 20
        _Speed ("Scroll Speed", Range(0, 2)) = 0.2
        _AlphaTres ("Alpha Threshold", Range(0, 1)) = 0.5
        _YOffset ("Vertical Offset", Range(0, 0.2)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Name "Forward"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Size;
                float _Border;
                float _Density;
                float _Speed;
                float _AlphaTres;
                float _YOffset;
            CBUFFER_END

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

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 posOS = IN.positionOS.xyz;
                // Quad is rotated in prefab; offset in local Z lifts it slightly above water in world space.
                posOS.z -= _YOffset;
                OUT.positionCS = TransformObjectToHClip(posOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 centered = IN.uv - 0.5;
                float radius = length(centered) * 2.0;

                float outer = step(radius, _Size);
                float inner = step(_Border, radius);
                float ring = outer * inner;

                float angle = atan2(centered.y, centered.x) * (1.0 / (2.0 * PI));
                float sweep = frac(angle + _Time.y * _Speed);
                float sweepMask = smoothstep(_AlphaTres, 1.0, sweep);

                float alpha = saturate(ring * sweepMask);
                half3 rgb = _Color.rgb * (_Density * 0.05);
                return half4(rgb, alpha * _Color.a);
            }
            ENDHLSL
        }
    }
}
