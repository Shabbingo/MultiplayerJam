Shader "MultiplayerJam/Ping Silhouette"
{
    Properties
    {
        _PingColor ("Ping Color", Color) = (0.1, 0.9, 1, 0.85)
        _ShellThickness ("Shell Thickness", Range(0.001, 0.08)) = 0.025
        _PulseSpeed ("Pulse Speed", Range(0, 20)) = 6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "PingSilhouette"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZTest Always
            ZWrite Off
            Cull Front

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
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _PingColor;
                float _ShellThickness;
                float _PulseSpeed;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 expandedPositionOS = input.positionOS.xyz +
                    normalize(input.normalOS) * _ShellThickness;
                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(expandedPositionOS);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half pulse = 0.78h + 0.22h * sin(
                    _Time.y * _PulseSpeed + input.positionWS.y * 4.0h);
                return half4(_PingColor.rgb * pulse,
                    _PingColor.a * pulse);
            }
            ENDHLSL
        }
    }
}
