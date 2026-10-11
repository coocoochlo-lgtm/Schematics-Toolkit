// The brain clouds over agents' heads, drawn with GPU instancing and no mesh: one camera-facing quad per orb.
// _Mode 0: value-space points (fixed colors), 1: heads (colored by what they're sampling), 2: head tails.
// Each agent shows one small cloud per head, in a ring that faces the camera; inside a cloud, the first three
// dimensions of the space map to world X, Y, Z, so it turns as you orbit around it.
Shader "Hidden/Manifold Network/Brain Orbs"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            StructuredBuffer<float> _Points;        // H × P × D
            StructuredBuffer<float4> _PointColors;  // H × P
            StructuredBuffer<float> _Heads;         // row r's heads at r × _HeadStride (H × D)
            StructuredBuffer<float4> _Centers;      // per row: world center (xyz), visible (w)
            StructuredBuffer<float> _Tails;         // (row × H + h) × _TailLength × D, a ring

            int _Mode, _H, _P, _D, _HeadStride, _TailLength, _TailHead, _Rows;
            float _Scale, _PointSize, _HeadSize, _Opacity, _Sigma;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float rim : TEXCOORD1;
            };

            float3 SpaceToLocal(uint at, StructuredBuffer<float> buffer)
            {
                float3 p = 0;
                p.x = buffer[at];
                if (_D > 1) p.y = buffer[at + 1];
                if (_D > 2) p.z = buffer[at + 2];
                return p;
            }

            float WrapDelta(float x) { return x - 2.0 * floor((x + 1.0) * 0.5); }

            v2f vert(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                v2f o;
                uint perRow = _Mode == 0 ? (uint)(_H * _P) : _Mode == 1 ? (uint)_H : (uint)(_H * _TailLength);
                uint row = instanceId / perRow;
                uint index = instanceId % perRow;
                uint head = _Mode == 0 ? index / (uint)_P : _Mode == 1 ? index : index / (uint)_TailLength;
                float4 center = _Centers[row];

                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;

                // This head's cloud sits on a ring around the center, facing the camera.
                float angle = _H > 1 ? 6.2831853 * head / _H : 0.0;
                float ring = _H > 1 ? 1.0 : 0.0;
                float3 cloudCenter = center.xyz + (right * sin(angle) + up * cos(angle)) * ring * _Scale;
                float cloudRadius = _H > 1 ? 0.42 * _Scale * min(1.0, 3.0 / sqrt(_H)) : _Scale;

                float3 local;
                float size;
                float4 color;
                float rim = 0;
                if (_Mode == 0)
                {
                    local = SpaceToLocal(index * _D, _Points);
                    color = _PointColors[index];
                    color.a *= _Opacity;
                    size = _PointSize;
                }
                else if (_Mode == 1)
                {
                    uint at = row * _HeadStride + head * _D;
                    local = SpaceToLocal(at, _Heads);
                    // Its color: what it's sampling, a soft blend of the nearby points.
                    float4 sum = 0;
                    float total = 1e-5;
                    float inv = 1.0 / (2.0 * _Sigma * _Sigma);
                    for (int j = 0; j < _P; j++)
                    {
                        uint pointIndex = head * _P + j;
                        float3 d = SpaceToLocal(pointIndex * _D, _Points) - local;
                        d.x = WrapDelta(d.x); d.y = WrapDelta(d.y); d.z = WrapDelta(d.z);
                        float w = exp(-min(dot(d, d) * inv, 40.0));
                        sum += _PointColors[pointIndex] * w;
                        total += w;
                    }
                    color = float4(sum.rgb / total, 1.0);
                    size = _HeadSize;
                    rim = 1;
                }
                else
                {
                    uint step = index % (uint)_TailLength;
                    uint at = ((row * _H + head) * _TailLength + step) * _D;
                    local = SpaceToLocal(at, _Tails);
                    // Older steps fade out; the ring's write position is the oldest.
                    uint age = ((uint)_TailHead + (uint)_TailLength - step) % (uint)_TailLength;
                    float t = 1.0 - age / (float)_TailLength;
                    color = float4(1, 1, 1, 0.5 * t * t);
                    size = _HeadSize * lerp(0.25, 0.7, t);
                }

                float3 world = cloudCenter + local * cloudRadius;
                float2 corner = float2((vertexId == 1 || vertexId == 2 || vertexId == 4) ? 1 : -1,
                                       (vertexId == 2 || vertexId == 4 || vertexId == 5) ? 1 : -1);
                world += (right * corner.x + up * corner.y) * size * _Scale;
                if (center.w < 0.5)
                    world = float3(0, -100000, 0);

                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                o.uv = corner;
                o.color = color;
                o.rim = rim;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float r = length(i.uv);
                if (r > 1.0) discard;
                float edge = smoothstep(1.0, 0.7, r);
                float3 color = i.color.rgb;
                // Heads get a bright rim and a soft glow so they stand out from the points.
                color = lerp(color, float3(1, 1, 1), i.rim * smoothstep(0.65, 0.9, r) * 0.8);
                color += i.rim * 0.25 * (1.0 - r);
                return fixed4(color, i.color.a * edge);
            }
            ENDCG
        }
    }
}
