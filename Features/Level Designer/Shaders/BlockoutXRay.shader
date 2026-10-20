// X-ray view of a blockout, drawn over an Art Design scene in the Scene view (Universal Render Pipeline).
//
// Unlit and transparent. Two passes: where the blockout is in front of the art it's drawn a little stronger; where
// it's hidden behind art (ZTest Greater) it still shows through, fainter, so the designer can line art up with
// shapes that are already covered. A fresnel rim outlines the silhouettes, and blockout meshes (which carry per-face
// coordinates in UV0/UV1, see BlockoutMeshBuilder) also get their face edges drawn. Other meshes just get the rim.
Shader "Remedy/Level Designer/Blockout X-Ray"
{
    Properties
    {
        _Color("Color", Color) = (0.3, 0.75, 1, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.35
        _HiddenOpacity("Opacity Behind Art", Range(0, 1)) = 0.5
        _RimPower("Rim Power", Range(0.5, 8)) = 2.5
        _EdgeWidth("Edge Width (m)", Range(0, 0.2)) = 0.04
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Opacity;
            half _HiddenOpacity;
            half _RimPower;
            float _EdgeWidth;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 uv0 : TEXCOORD0;
            float4 uv1 : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float4 uv0 : TEXCOORD2;
            float4 uv1 : TEXCOORD3;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings XRayVert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = positions.positionCS;
            output.positionWS = positions.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv0 = input.uv0;
            output.uv1 = input.uv1;
            return output;
        }

        half4 XRayShade(Varyings input, half strength)
        {
            float3 normalWS = normalize(input.normalWS);
            float3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            half rim = pow(1.0 - saturate(abs(dot(normalWS, viewWS))), _RimPower);

            // Face edges, for meshes that carry face coordinates (edge mode in uv0.w, face size in uv1.xy).
            float2 faceUV = input.uv0.xy;
            float2 faceSize = input.uv1.xy;
            float edgeDistance = min(faceUV.y, faceSize.y - faceUV.y);
            if (input.uv0.w < 1.5)
                edgeDistance = min(edgeDistance, min(faceUV.x, faceSize.x - faceUV.x));
            float aa = max(max(fwidth(faceUV.x), fwidth(faceUV.y)), 1e-5);
            half edge = 0;
            if (input.uv0.w > 0.5 && faceSize.x > 0 && faceSize.y > 0)
                edge = 1.0 - smoothstep(_EdgeWidth - aa, _EdgeWidth + aa, edgeDistance);

            half alpha = _Opacity * strength * (0.3 + 0.7 * rim);
            alpha = saturate(max(alpha, edge * saturate(_Opacity * strength * 2.0)));
            half3 color = lerp(_Color.rgb, 1.0.xxx, edge * 0.6);
            return half4(color, alpha * _Color.a);
        }
        ENDHLSL

        // Parts of the blockout in front of the art.
        Pass
        {
            Name "XRayVisible"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex XRayVert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            half4 Frag(Varyings input) : SV_Target
            {
                return XRayShade(input, 1.0);
            }
            ENDHLSL
        }

        // Parts of the blockout hidden behind the art: fainter, but still there.
        Pass
        {
            Name "XRayHidden"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Greater
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex XRayVert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            half4 Frag(Varyings input) : SV_Target
            {
                return XRayShade(input, _HiddenOpacity);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
