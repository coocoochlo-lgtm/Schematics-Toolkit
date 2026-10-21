using Box3D;
using Box3D.Hybrid;
using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Remedy.Box3DPhysics
{
    /// <summary>
    /// The toolkit's physics entry point, backed by Box3D (Unity's built-in physics isn't used). Queries take Unity
    /// layer masks and report hits as <see cref="RaycastHit"/>s (point, normal, distance) plus the GameObject hit, so
    /// code written against Unity's API keeps working.
    /// </summary>
    public static class SchematicPhysics
    {
        /// <summary>The Box3D world (created on first use).</summary>
        public static World World => Box3DWorld.Instance.World;

        /// <summary>The world's gravity.</summary>
        public static Vector3 Gravity
        {
            get
            {
                var world = World;
                return world.IsValid ? (Vector3)world.GetGravity() : new Vector3(0f, -10f, 0f);
            }
        }

        /// <summary>A query filter that hits the layers in a Unity layer mask (Box3D shapes use their layer as their category).</summary>
        public static QueryFilter FilterFor(LayerMask mask)
        {
            var filter = QueryFilter.Default;
            filter.MaskBits = unchecked((ulong)(uint)mask.value);
            return filter;
        }

        /// <summary>The Box3D body component a shape belongs to, or null (plain static geometry).</summary>
        public static Box3DBody BodyOf(ShapeId shapeId)
        {
            var shape = new Shape { Id = shapeId };
            if (!shape.IsValid)
                return null;
            IntPtr userData = shape.UserData;
            return userData == IntPtr.Zero ? null : GCHandle.FromIntPtr(userData).Target as Box3DBody;
        }

        /// <summary>The GameObject a shape belongs to (its body's), or null.</summary>
        public static GameObject GameObjectOf(ShapeId shapeId)
        {
            var body = BodyOf(shapeId);
            return body != null ? body.gameObject : null;
        }

        /// <summary>
        /// Sweeps a sphere and writes every hit into the buffers (both the same length), skipping the body
        /// <paramref name="ignore"/> belongs to. Returns the hit count. Distances are measured from the origin.
        /// </summary>
        public static int SphereCastAll(Vector3 origin, float radius, Vector3 direction, float distance, LayerMask mask,
            RaycastHit[] hits, GameObject[] objects, Box3DBody ignore = null)
        {
            Vector3 translation = direction.normalized * distance;
            Span<float3> proxy = stackalloc float3[1];
            Span<RayHit> raw = stackalloc RayHit[RaycastHitBufferSize];
            int count = radius > 0f
                ? World.CastShape(origin, proxy, radius, translation, FilterFor(mask), raw)
                : World.CastRay(origin, translation, FilterFor(mask), raw);

            int written = 0;
            ulong ignoreId = ignore != null && ignore.Body.IsValid ? ignore.Body.Id.ToUInt64() : 0UL;
            for (int i = 0; i < count && written < hits.Length; i++)
            {
                var shape = new Shape { Id = raw[i].ShapeId };
                if (ignoreId != 0UL && shape.IsValid && shape.GetBody().Id.ToUInt64() == ignoreId)
                    continue;

                var hit = new RaycastHit
                {
                    point = raw[i].Point,
                    normal = raw[i].Normal,
                    distance = raw[i].Fraction * distance,
                };
                hits[written] = hit;
                if (objects != null && written < objects.Length)
                    objects[written] = GameObjectOf(raw[i].ShapeId);
                written++;
            }
            return written;
        }

        /// <summary>The closest ray hit, skipping the body <paramref name="ignore"/> belongs to.</summary>
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float distance, LayerMask mask, Box3DBody ignore = null)
            => SphereCast(origin, 0f, direction, out hit, out _, distance, mask, ignore);

        /// <summary>The closest sphere-cast hit (radius 0 casts a ray), skipping the body <paramref name="ignore"/> belongs to.</summary>
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hit, out GameObject hitObject,
            float distance, LayerMask mask, Box3DBody ignore = null)
        {
            if (radius <= 0f)
                return RaycastClosest(origin, direction, out hit, out hitObject, distance, mask, ignore);

            var hits = _single ??= new RaycastHit[RaycastHitBufferSize];
            var objects = _singleObjects ??= new GameObject[RaycastHitBufferSize];
            int count = SphereCastAll(origin, radius, direction, distance, mask, hits, objects, ignore);

            hit = default;
            hitObject = null;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    hit = hits[i];
                    hitObject = objects[i];
                }
            }
            return count > 0;
        }

        private static bool RaycastClosest(Vector3 origin, Vector3 direction, out RaycastHit hit,
            out GameObject hitObject, float distance, LayerMask mask, Box3DBody ignore)
        {
            hit = default;
            hitObject = null;
            if (distance <= 0f || direction.sqrMagnitude < 1e-8f)
                return false;

            direction.Normalize();
            float traversed = 0f;
            float advance = Mathf.Max(0.001f, distance * 1e-6f);
            for (int attempt = 0; attempt < RaycastHitBufferSize && traversed < distance; attempt++)
            {
                float remaining = distance - traversed;
                var result = World.CastRayClosest(origin + direction * traversed, direction * remaining, FilterFor(mask));
                if (!result.Hit)
                    return false;

                var body = BodyOf(result.ShapeId);
                float hitDistance = traversed + result.Fraction * remaining;
                if (ignore == null || body != ignore)
                {
                    hit = new RaycastHit
                    {
                        point = (Vector3)result.Point,
                        normal = result.Normal,
                        distance = hitDistance,
                    };
                    hitObject = GameObjectOf(result.ShapeId);
                    return true;
                }

                traversed = hitDistance + advance;
            }
            return false;
        }

        public const int RaycastHitBufferSize = 32;
        [ThreadStatic] private static RaycastHit[] _single;
        [ThreadStatic] private static GameObject[] _singleObjects;
    }
}
