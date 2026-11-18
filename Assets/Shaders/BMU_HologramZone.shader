Shader "BMU/HologramZone"
{
    Properties
    {
        _BaseColor ("Hologram Color", Color) = (0.01, 0.65, 1.0, 0.55)
        _RimColor ("Edge Glow Color", Color) = (0.35, 0.90, 1.0, 0.95)
        _PulseSpeed ("Pulse Speed", Range(0.2, 6.0)) = 2.0
        _ScanlineDensity ("Scanline Density", Range(1.0, 40.0)) = 14.0
        _FadeTop ("Top Fade Power", Range(0.5, 4.0)) = 1.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _RimColor;
                half _PulseSpeed;
                half _ScanlineDensity;
                half _FadeTop;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.uv = IN.uv;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceNormalizeViewDir(posWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 normal = normalize(IN.normalWS);
                half3 viewDir = normalize(IN.viewDirWS);

                // Fresnel edge highlight
                half ndotv = abs(dot(normal, viewDir));
                half fresnel = pow(saturate(1.0h - ndotv), 1.8h);

                // Vertical fade from bottom (uv.y = 0) to top (uv.y = 1)
                half verticalFade = pow(saturate(1.0h - IN.uv.y), _FadeTop);

                // Subtle animated scanlines + pulse
                half scan = sin((IN.uv.y * _ScanlineDensity - _Time.y * _PulseSpeed) * 6.28318h) * 0.5h + 0.5h;
                half pulse = sin(_Time.y * _PulseSpeed) * 0.18h + 0.82h;

                half3 col = lerp(_BaseColor.rgb, _RimColor.rgb, fresnel * 0.75h + scan * 0.25h);
                half alpha = _BaseColor.a * verticalFade * pulse * lerp(0.55h, 1.0h, fresnel * 0.65h + scan * 0.35h);

                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
