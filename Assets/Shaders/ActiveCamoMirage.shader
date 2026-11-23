Shader "MultiplayerJam/Active Camo Mirage"
{
    Properties
    {
        [HDR] _MirageTint ("Mirage Tint", Color) = (0.35, 0.85, 1, 1)
        _DistortionStrength ("Distortion Strength", Range(0, 0.04)) = 0.012
        _MirageOpacity ("Mirage Opacity", Range(0.05, 1)) = 0.78
        _WobbleStrength ("Surface Wobble", Range(0, 0.04)) = 0.009
        [HideInInspector] _UseSceneColor ("Use Scene Color", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+20"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ActiveCamoMirage"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZTest LEqual
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float3 viewDirectionWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _MirageTint;
                float _DistortionStrength;
                float _MirageOpacity;
                float _WobbleStrength;
                float _UseSceneColor;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float waveA = sin(positionWS.y * 19.0 + _Time.y * 7.0);
                float waveB = sin(positionWS.y * 31.0 - _Time.y * 11.0 +
                    positionWS.x * 4.0);
                positionWS += normalWS * (waveA + waveB * 0.45) *
                    _WobbleStrength;
                output.positionWS = positionWS;
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.normalWS = normalWS;
                output.viewDirectionWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirectionWS = normalize(input.viewDirectionWS);
                half fresnel = pow(saturate(1.0h -
                    dot(normalWS, viewDirectionWS)), 2.2h);
                float ripple = sin(input.positionWS.y * 26.0 - _Time.y * 12.0 +
                    sin(input.positionWS.x * 9.0 + _Time.y * 3.0));
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionHCS);
                float2 distortion = float2(ripple,
                    sin(input.positionWS.y * 17.0 + _Time.y * 9.0)) *
                    _DistortionStrength * (0.35 + fresnel);
                half3 sceneColor = SampleSceneColor(
                    saturate(screenUV + distortion));
                half shimmer = 0.15h + fresnel * 0.65h +
                    pow(abs(ripple), 12.0h) * 0.2h;
                half3 fallbackColor = _MirageTint.rgb *
                    (0.35h + shimmer * 1.25h);
                half3 refractedColor = lerp(sceneColor,
                    _MirageTint.rgb, saturate(shimmer * 0.32h));
                half useSceneColor = step(0.5h, _UseSceneColor);
                half3 finalColor = lerp(fallbackColor, refractedColor,
                    useSceneColor);
                half fallbackAlpha = 0.24h + fresnel * 0.34h;
                half refractedAlpha = _MirageOpacity *
                    (0.72h + fresnel * 0.28h);
                return half4(finalColor,
                    lerp(fallbackAlpha, refractedAlpha, useSceneColor));
            }
            ENDHLSL
        }
    }
}

