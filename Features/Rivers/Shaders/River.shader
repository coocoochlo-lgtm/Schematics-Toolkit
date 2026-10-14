// Flowing water for Spline Rivers (URP). The mesh's UV.y runs downstream and UV2.x carries the flow speed per vertex,
// so normals scroll with the current (faster mid-river), using two phases blended to hide the reset.
Shader "Schematics/River"
{
    Properties
    {
        _Color ("Water Color", Color) = (0.08, 0.32, 0.38, 0.75)
        _DeepColor ("Deep Color", Color) = (0.02, 0.12, 0.18, 0.9)
        [Normal] _NormalMap ("Flow Normals", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.8
        _NormalTiling ("Normal Tiling", Float) = 1
        _FlowScale ("Flow Speed Scale", Float) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.92
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 4
        _FoamColor ("Foam Color", Color) = (0.9, 0.95, 1, 1)
        _BankFoam ("Bank Foam Width", Range(0, 0.5)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _DeepColor;
                float4 _NormalMap_ST;
                half _NormalStrength;
                float _NormalTiling;
                float _FlowScale;
                half _Smoothness;
                half _FresnelPower;
                half4 _FoamColor;
                half _BankFoam;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 flow : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float flow : TEXCOORD4;
                half fog : TEXCOORD5;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = normals.normalWS;
                output.tangentWS = float4(normals.tangentWS, input.tangentOS.w);
                output.uv = input.uv;
                output.flow = input.flow.x;
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            // Two normal samples scrolling downstream half a cycle apart, each faded out as it resets.
            half3 FlowNormal(float2 uv, float speed)
            {
                float time = _Time.y * _FlowScale;
                float phase0 = frac(time);
                float phase1 = frac(time + 0.5);
                float weight = abs((0.5 - phase0) / 0.5);

                float2 baseUV = uv * _NormalTiling;
                float2 offset = float2(0, -speed);
                half3 n0 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, baseUV + offset * phase0), _NormalStrength);
                half3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, baseUV + offset * phase1 + 0.5), _NormalStrength);
                return normalize(lerp(n0, n1, weight));
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normalTS = FlowNormal(input.uv, input.flow);
                float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));

                half3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                half facing = saturate(dot(normalWS, viewWS));
                half fresnel = pow(1.0h - facing, _FresnelPower);

                // Deeper looking in the middle of the river, lighter at the banks.
                half bank = abs(input.uv.x * 2.0 - 1.0);
                half3 body = lerp(_DeepColor.rgb, _Color.rgb, bank * bank);
                half alpha = lerp(_DeepColor.a, _Color.a, bank);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 halfVector = normalize(mainLight.direction + viewWS);
                half specularPower = exp2(10.0 * _Smoothness + 1.0);
                half specular = pow(saturate(dot(normalWS, halfVector)), specularPower) * _Smoothness;
                half3 lit = body * (mainLight.color * saturate(dot(normalWS, mainLight.direction)) * 0.5 + SampleSH(normalWS));

                half3 reflection = GlossyEnvironmentReflection(reflect(-viewWS, normalWS), 1.0h - _Smoothness, 1.0h);
                half3 color = lerp(lit, reflection, fresnel) + specular * mainLight.color * mainLight.shadowAttenuation;

                // Foam along the banks, broken up by the flowing normals.
                half foam = smoothstep(1.0 - _BankFoam, 1.0, bank + normalTS.x * 0.15);
                color = lerp(color, _FoamColor.rgb, foam * _FoamColor.a);
                alpha = saturate(max(alpha, fresnel) + foam);

                color = MixFog(color, input.fog);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
