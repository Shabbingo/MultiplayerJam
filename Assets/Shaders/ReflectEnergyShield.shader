Shader "MultiplayerJam/Reflect Energy Shield"
{
    Properties
    {
        _ShieldColor ("Shield Color", Color) = (0.15, 0.7, 1, 0.38)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.4
        _PulseSpeed ("Pulse Speed", Range(0, 20)) = 9
        _WaveScale ("Wave Scale", Range(0, 30)) = 8
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
            Name "ReflectEnergyShield"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _ShieldColor;
                float _RimPower;
                float _PulseSpeed;
                float _WaveScale;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirectionWS = SafeNormalize(
                    GetWorldSpaceViewDir(input.positionWS));
                half fresnel = pow(1.0h - saturate(
                    dot(normalWS, viewDirectionWS)), _RimPower);
                half wave = 0.78h + 0.22h * sin(
                    _Time.y * _PulseSpeed + input.positionWS.y * _WaveScale);
                half energy = saturate(fresnel * 1.35h + 0.1h) * wave;
                half alpha = _ShieldColor.a * energy;
                half3 color = _ShieldColor.rgb *
                    (1.25h + fresnel * 2.5h) * wave;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
