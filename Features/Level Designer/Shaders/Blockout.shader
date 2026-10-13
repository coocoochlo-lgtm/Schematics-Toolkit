// Blockout surface for the Level Designer (Universal Render Pipeline).
//
// Simple lit surface (main light with shadows + ambient) that draws, without any texture:
//  - a world-aligned 1 m grid with a 0.25 m subgrid (fades when it would get too dense),
//  - highlighted edges on every face (from per-face coordinates baked into UV0/UV1 by BlockoutMeshBuilder),
//  - the shape's size in meters on its faces ("2x1.5x4m"), drawn as seven-segment style glyphs from distance
//    functions. The glyph codes come from a MaterialPropertyBlock set by BlockoutShape (_Label0.._Label4).
// Mesh data (see BlockoutMeshBuilder): UV0 = (u, v) on the face in meters, z = label on/off, w = edge mode
// (0 none, 1 all sides, 2 top/bottom only). UV1 = (face width, face height, label centre u, label centre v).
Shader "Remedy/Level Designer/Blockout"
{
    Properties
    {
        _BaseColor("Color", Color) = (0.6, 0.6, 0.65, 1)
        _Size("Size (m)", Vector) = (1, 1, 1, 0)
        _GridStrength("Grid (1 m)", Range(0, 1)) = 0.3
        _SubGridStrength("Subgrid (0.25 m)", Range(0, 1)) = 0.12
        _EdgeWidth("Edge Width (m)", Range(0, 0.2)) = 0.03
        _EdgeColor("Edge Color", Color) = (1, 1, 1, 0.8)
        _LabelColor("Label Color", Color) = (0.05, 0.05, 0.06, 1)
        _LabelOutlineColor("Label Outline", Color) = (1, 1, 1, 0.85)
        _LabelMaxHeight("Label Max Height (m)", Float) = 0.45
        [HideInInspector] _LabelLength("Label Length", Float) = 0
        [HideInInspector] _Label0("Label 0", Vector) = (13, 13, 13, 13)
        [HideInInspector] _Label1("Label 1", Vector) = (13, 13, 13, 13)
        [HideInInspector] _Label2("Label 2", Vector) = (13, 13, 13, 13)
        [HideInInspector] _Label3("Label 3", Vector) = (13, 13, 13, 13)
        [HideInInspector] _Label4("Label 4", Vector) = (13, 13, 13, 13)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Every pass shares the same per-material buffer (SRP Batcher compatible when no property block is set).
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _Size;
            half _GridStrength;
            half _SubGridStrength;
            float _EdgeWidth;
            half4 _EdgeColor;
            half4 _LabelColor;
            half4 _LabelOutlineColor;
            float _LabelMaxHeight;
            float _LabelLength;
            float4 _Label0;
            float4 _Label1;
            float4 _Label2;
            float4 _Label3;
            float4 _Label4;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "BlockoutForward"
            // ForwardOnly renders in both the Forward and Deferred renderers.
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float4 screenPos : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv0 = input.uv0;
                output.uv1 = input.uv1;
                output.screenPos = ComputeScreenPos(positions.positionCS);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            // Anti-aliased grid lines: 1 on a line, 0 between lines.
            float GridLines(float2 p, float spacing, float widthPixels)
            {
                float2 c = p / spacing;
                float2 d = max(fwidth(c), 1e-5);
                float2 g = abs(frac(c - 0.5) - 0.5) / d;
                float lineMask = 1.0 - saturate(min(g.x, g.y) / widthPixels);
                // Fade out when the cells get smaller than a few pixels.
                float density = saturate(1.0 - max(d.x, d.y) * 3.0);
                return lineMask * density;
            }

            float SegmentDistance(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            // Distance from p (glyph space: 0..0.6 wide, 0..1 tall) to the centre line of a glyph's strokes.
            // Codes: 0-9 digits, 10 '.', 11 'x', 12 'm', 13 space, 14 '-'.
            float GlyphDistance(int code, float2 p)
            {
                const float x0 = 0.08;
                const float x1 = 0.52;
                const float yb = 0.06;
                const float ym = 0.5;
                const float yt = 0.94;
                const float gap = 0.05;
                float d = 1e3;

                if (code <= 9)
                {
                    // Seven segments: a (top), b (upper right), c (lower right), d (bottom), e (lower left),
                    // f (upper left), g (middle).
                    static const int masks[10] = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };
                    int mask = masks[clamp(code, 0, 9)];
                    if (mask & 1)  d = min(d, SegmentDistance(p, float2(x0 + gap, yt), float2(x1 - gap, yt)));
                    if (mask & 2)  d = min(d, SegmentDistance(p, float2(x1, yt - gap), float2(x1, ym + gap)));
                    if (mask & 4)  d = min(d, SegmentDistance(p, float2(x1, ym - gap), float2(x1, yb + gap)));
                    if (mask & 8)  d = min(d, SegmentDistance(p, float2(x0 + gap, yb), float2(x1 - gap, yb)));
                    if (mask & 16) d = min(d, SegmentDistance(p, float2(x0, ym - gap), float2(x0, yb + gap)));
                    if (mask & 32) d = min(d, SegmentDistance(p, float2(x0, yt - gap), float2(x0, ym + gap)));
                    if (mask & 64) d = min(d, SegmentDistance(p, float2(x0 + gap, ym), float2(x1 - gap, ym)));
                }
                else if (code == 10)
                {
                    d = max(length(p - float2(0.3, yb + 0.02)) - 0.03, 0.0);
                }
                else if (code == 11)
                {
                    d = min(SegmentDistance(p, float2(0.14, 0.2), float2(0.46, 0.6)),
                            SegmentDistance(p, float2(0.14, 0.6), float2(0.46, 0.2)));
                }
                else if (code == 12)
                {
                    float top = 0.52;
                    d = SegmentDistance(p, float2(x0, yb), float2(x0, top));
                    d = min(d, SegmentDistance(p, float2(0.3, yb), float2(0.3, top)));
                    d = min(d, SegmentDistance(p, float2(x1, yb), float2(x1, top)));
                    d = min(d, SegmentDistance(p, float2(x0, top), float2(x1, top)));
                }
                else if (code == 14)
                {
                    d = SegmentDistance(p, float2(0.14, ym), float2(0.46, ym));
                }
                return d;
            }

            int LabelGlyph(int index)
            {
                float4 labels[5] = { _Label0, _Label1, _Label2, _Label3, _Label4 };
                float4 group = labels[clamp(index >> 2, 0, 4)];
                int lane = index & 3;
                float value = lane == 0 ? group.x : (lane == 1 ? group.y : (lane == 2 ? group.z : group.w));
                return (int)round(value);
            }

            // Returns (text coverage, outline coverage) for this pixel of the face.
            float2 Label(float2 uv, float2 faceSize, float2 center, float uvDerivative)
            {
                float count = _LabelLength;
                if (count < 0.5)
                    return 0;

                const float advance = 0.7; // glyph cell width, in glyph heights
                float height = min(_LabelMaxHeight, min(faceSize.y * 0.25, faceSize.x * 0.85 / (count * advance)));
                if (height < 0.02)
                    return 0;

                float2 p = (uv - center) / height;
                p.x += count * advance * 0.5;
                p.y += 0.5;
                if (p.x < -0.1 || p.y < -0.2 || p.y > 1.2 || p.x > count * advance + 0.1)
                    return 0;

                int index = (int)floor(p.x / advance);
                float2 local = float2(p.x - index * advance - (advance - 0.6) * 0.5, p.y);
                float d = 1e3;
                if (index >= 0 && index < (int)count)
                    d = GlyphDistance(LabelGlyph(index), local);

                // Derivatives were taken before any branching; here they're scaled into glyph units.
                float aa = max(uvDerivative / height, 1e-4);
                const float stroke = 0.075;
                float text = 1.0 - smoothstep(stroke - aa, stroke + aa, d);
                float outline = 1.0 - smoothstep(stroke + 0.06 - aa, stroke + 0.06 + aa, d);
                return float2(text, outline);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                float3 absNormal = abs(normalWS);

                // World grid, projected along the face's main axis.
                float2 gridPos = absNormal.x > absNormal.y && absNormal.x > absNormal.z ? input.positionWS.zy
                               : (absNormal.y > absNormal.z ? input.positionWS.xz : input.positionWS.xy);
                float major = GridLines(gridPos, 1.0, 1.5);
                float minor = GridLines(gridPos, 0.25, 1.0);

                half3 albedo = _BaseColor.rgb;
                albedo *= 1.0 - minor * _SubGridStrength;
                albedo *= 1.0 - major * _GridStrength;

                // Edges of the face.
                // (Screen derivatives are taken here, outside any branch, so they're well defined.)
                float2 faceUV = input.uv0.xy;
                float2 faceSize = input.uv1.xy;
                float edgeMode = input.uv0.w;
                float uvDerivative = max(fwidth(faceUV.x), fwidth(faceUV.y));
                float edgeDistance = min(faceUV.y, faceSize.y - faceUV.y);
                if (edgeMode < 1.5)
                    edgeDistance = min(edgeDistance, min(faceUV.x, faceSize.x - faceUV.x));
                if (edgeMode > 0.5 && faceSize.x > 0 && faceSize.y > 0)
                {
                    float aa = max(uvDerivative, 1e-5);
                    float edge = 1.0 - smoothstep(_EdgeWidth - aa, _EdgeWidth + aa, edgeDistance);
                    albedo = lerp(albedo, _EdgeColor.rgb, edge * _EdgeColor.a);
                }

                // Lighting: main light with shadows, plus ambient. Faces facing away get a gentle tint so the
                // shape's form reads even in flat light.
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = input.screenPos;
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                half lambert = saturate(dot(normalWS, mainLight.direction));
                half3 lighting = mainLight.color * lambert * mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                lighting += SampleSH(normalWS);
                lighting *= lerp(0.85, 1.0, normalWS.y * 0.5 + 0.5);
                half3 color = albedo * lighting;

                // The size label (unlit, so it stays readable in shadow).
                if (input.uv0.z > 0.5 && faceSize.x > 0 && faceSize.y > 0)
                {
                    float2 label = Label(faceUV, faceSize, input.uv1.zw, uvDerivative);
                    color = lerp(color, _LabelOutlineColor.rgb, label.y * _LabelOutlineColor.a);
                    color = lerp(color, _LabelColor.rgb, label.x * _LabelColor.a);
                }

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set by URP while rendering shadow maps.
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                    return half4(packedNormalWS, 0.0);
                #else
                    return half4(normalWS, 0.0);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
