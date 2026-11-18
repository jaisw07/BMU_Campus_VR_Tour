// URP foliage shader for tree leaves and grass cards: alpha-cutout (with alpha-to-coverage), two-sided lighting,
// height-weighted wind sway, simple back-light translucency. SRP-Batcher compatible. Shadow caster + depth-only passes
// apply the same sway and cutout so shadows move with the leaves.
Shader "BMU/Foliage"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.45
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0,2)) = 1
        _Smoothness("Smoothness", Range(0,1)) = 0.12
        _Translucency("Translucency", Range(0,2)) = 0.6
        _TranslucencyColor("Translucency Colour", Color) = (0.55,0.75,0.2,1)
        _AmbientBoost("Ambient Boost", Range(0,2)) = 1
        [Header(Wind)]
        _WindStrength("Wind Strength (m)", Range(0,1)) = 0.12
        _WindSpeed("Wind Speed", Range(0,6)) = 1.3
        _WindScale("Wind Gust Scale", Range(0.01,1)) = 0.12
        _WindHeightStart("Sway Starts At Height (m)", Float) = 1.0
        _WindHeightRange("Full Sway At Height (m)", Float) = 8.0
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _BumpScale;
            float _Smoothness;
            float _Translucency;
            float4 _TranslucencyColor;
            float _AmbientBoost;
            float _WindStrength;
            float _WindSpeed;
            float _WindScale;
            float _WindHeightStart;
            float _WindHeightRange;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

        // World-space sway: two out-of-phase sines + a slow gust, weighted by height above the ground (y = 0).
        float3 ApplyWind(float3 positionWS)
        {
            float h = saturate((positionWS.y - _WindHeightStart) / max(0.01, _WindHeightRange));
            float w = h * h * _WindStrength;
            float t = _Time.y * _WindSpeed;
            float phase = dot(positionWS.xz, float2(0.7, 0.5)) * _WindScale * 4.0;
            float gust = 0.6 + 0.4 * sin(_Time.y * 0.23 + positionWS.x * 0.01 + positionWS.z * 0.013);
            float2 sway = float2(sin(t + phase) + 0.4 * sin(t * 2.1 + phase * 1.7),
                                 cos(t * 0.9 + phase * 1.3) + 0.3 * sin(t * 2.7 + phase)) * w * gust;
            positionWS.xz += sway;
            positionWS.y -= 0.25 * dot(sway, sway);
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            AlphaToMask On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = ApplyWind(TransformObjectToWorld(input.positionOS.xyz));
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 albedoA = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(albedoA.a - _Cutoff);

                float3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                float sgn = input.tangentWS.w;
                float3 bitangent = sgn * cross(input.normalWS, input.tangentWS.xyz);
                float3 normalWS = normalize(mul(normalTS, float3x3(input.tangentWS.xyz, bitangent, input.normalWS)));
                normalWS = IS_FRONT_VFACE(facing, normalWS, -normalWS); // two-sided: flip the normal on back faces

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS) * _AmbientBoost;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedoA.rgb;
                s.alpha = 1;
                s.metallic = 0;
                s.smoothness = _Smoothness;
                s.occlusion = 1;
                s.normalTS = normalTS;

                half4 color = UniversalFragmentPBR(inputData, s);

                // Back-light translucency from the main light.
                Light mainLight = GetMainLight(inputData.shadowCoord);
                float back = pow(saturate(dot(-mainLight.direction, inputData.viewDirectionWS)), 3.0);
                color.rgb += albedoA.rgb * _TranslucencyColor.rgb * mainLight.color * mainLight.shadowAttenuation * back * _Translucency;

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = albedoA.a; // alpha-to-coverage edge
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = ApplyWind(TransformObjectToWorld(input.positionOS.xyz));
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings input) : SV_TARGET
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(ApplyWind(TransformObjectToWorld(input.positionOS.xyz)));
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half frag(Varyings input) : SV_TARGET
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
