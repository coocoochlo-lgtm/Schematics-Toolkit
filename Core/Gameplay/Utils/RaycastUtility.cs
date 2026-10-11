using System;
using UnityEngine;

namespace Remedy.Framework
{
    /// <summary>
    /// Allocation-free wrapper around physics casts (Box3D, through <see cref="Remedy.Box3DPhysics.SchematicPhysics"/>) that
    /// applies common filtering logic and returns structured results. Hits are RaycastHits carrying point, normal and
    /// distance; the GameObject each hit belongs to is in <see cref="RaycastResult.HitObjects"/>.
    /// </summary>
    public static class RaycastUtility
    {
        public const int MaxHitCapacity = 32;

        /// <summary>
        /// Holds results of a single cast (fixed capacity).
        /// Note: Results are only valid until the next RaycastUtility call
        /// on the same thread, since they come from a shared scratch buffer.
        /// </summary>
        public struct RaycastResult
        {
            public int HitCount;
            public int ClosestHitIndex; // -1 if no valid hit
            public bool HasHit;

            public bool TryGetClosestHit(out RaycastHit hit)
            {
                if (!HasHit || ClosestHitIndex > HitCount) { hit = default; return false; }
                hit = s_ScratchBuffer[ClosestHitIndex];
                return true;
            }

            /// <summary>
            /// Exposes the underlying hits array.
            /// WARNING: only valid until the next RaycastUtility call.
            /// </summary>
            public ReadOnlySpan<RaycastHit> Hits
                => new ReadOnlySpan<RaycastHit>(s_ScratchBuffer, 0, HitCount);

            /// <summary>The GameObject of each hit (its Box3D body's object; null for plain static geometry).</summary>
            public ReadOnlySpan<GameObject> HitObjects
                => new ReadOnlySpan<GameObject>(s_ObjectBuffer, 0, HitCount);

            /// <summary>Copies valid hits into a caller-provided array.</summary>
            public void CopyHits(RaycastHit[] target)
            {
                int len = Mathf.Min(HitCount, target.Length);
                Array.Copy(s_ScratchBuffer, target, len);
            }
        }

        // Shared scratch buffer reused per thread.
        [ThreadStatic]
        private static RaycastHit[] s_ScratchBuffer;
        [ThreadStatic]
        private static GameObject[] s_ObjectBuffer;

        static RaycastUtility()
        {
            s_ScratchBuffer = new RaycastHit[MaxHitCapacity];
            s_ObjectBuffer = new GameObject[MaxHitCapacity];
        }

        /// <summary>
        /// Perform a sphere cast with angle & distance filtering.
        /// </summary>
        public static RaycastResult SphereCast(
            Transform source,
            Vector3 origin,
            Vector3 direction,
            float radius,
            float distance,
            LayerMask mask,
            float minAngleDeg = -1f,
            float maxAngleDeg = -1f,
            float validDistance = Mathf.Infinity)
        {
            if (s_ScratchBuffer == null)
                s_ScratchBuffer = new RaycastHit[MaxHitCapacity];
            if (s_ObjectBuffer == null)
                s_ObjectBuffer = new GameObject[MaxHitCapacity];

            RaycastResult result = default;
            result.ClosestHitIndex = -1;

            // The caster's own body is skipped by the query.
            var ignore = source != null ? source.GetComponentInParent<Box3D.Hybrid.Box3DBody>() : null;
            int hitCount = Remedy.Box3DPhysics.SchematicPhysics.SphereCastAll(
                origin -= direction * (radius * 0.5f),
                radius,
                direction,
                distance,
                mask,
                s_ScratchBuffer,
                s_ObjectBuffer,
                ignore
            );

            float minDot = (minAngleDeg > 0) ? Mathf.Cos(minAngleDeg * Mathf.Deg2Rad) : -1f;
            float maxDot = (maxAngleDeg > 0) ? Mathf.Cos(maxAngleDeg * Mathf.Deg2Rad) : 1f;

            float closestDist = float.MaxValue;
            int validCount = 0;

            for (int i = 0; i < hitCount && validCount < MaxHitCapacity; i++)
            {
                var hit = s_ScratchBuffer[i];
                var hitObject = s_ObjectBuffer[i];

                if (hitObject != null && source != null && hitObject.transform == source) continue;
                if (hit.distance > validDistance) continue;
                if (hit.point == default) continue;

                float dot = Vector3.Dot(hit.normal, direction);
                if (minAngleDeg > 0 && dot < minDot) continue;
                if (maxAngleDeg > 0 && dot > maxDot) continue;

                // Keep track of closest
                if (hit.distance < closestDist)
                {
                    closestDist = hit.distance;
                    result.ClosestHitIndex = validCount;
                    result.HasHit = true;
                }

                // Store valid hit compacted in front
                s_ObjectBuffer[validCount] = hitObject;
                s_ScratchBuffer[validCount++] = hit;
            }

            result.HitCount = validCount;
            return result;
        }
    }
}
