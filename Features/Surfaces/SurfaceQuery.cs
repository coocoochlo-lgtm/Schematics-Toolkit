using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using UnityEngine;

namespace Remedy.Surfaces
{
    /// <summary>What a material cast found.</summary>
    public struct SurfaceHit
    {
        /// <summary>The surface's material (painted, tagged, or the project's default); null when there's no default.</summary>
        public SurfaceMaterial Material;
        public Vector3 Point;
        public Vector3 Normal;
        public float Distance;
        /// <summary>The object hit.</summary>
        public GameObject Object;
        /// <summary>True when it hit a painted surface (the Surfaces layer), false for a collider.</summary>
        public bool Painted;
        /// <summary>The mesh triangle hit, on painted surfaces (-1 otherwise).</summary>
        public int Triangle;

        /// <summary>One of the material's properties (0 when there's no material or no such property).</summary>
        public float Property(string name) => Material != null ? Material.Get(name) : 0f;
    }

    /// <summary>Which surfaces a material cast hits.</summary>
    public enum SurfaceLayers
    {
        /// <summary>Painted surfaces and colliders, whichever is nearer.</summary>
        All,
        /// <summary>Only painted surfaces (the Surfaces layer).</summary>
        Painted,
        /// <summary>Only colliders (tagged objects, and the default for the rest).</summary>
        Colliders,
    }

    /// <summary>
    /// Finds what a surface is made of along a ray: painted surfaces (the Surfaces layer, see <see cref="SurfacePaint"/>)
    /// and colliders, whose objects are made of their <see cref="SurfaceMaterialTag"/>'s material or else the
    /// project's default (<see cref="SurfaceLibrary"/>). Shared by the Get Surface nodes, Sound Cues' surface and
    /// occlusion controls, and your own code (main thread).
    /// </summary>
    public static class SurfaceQuery
    {
        /// <summary>
        /// The nearest surface along a ray from <paramref name="origin"/>, up to <paramref name="distance"/> meters away.
        /// <paramref name="ignore"/>'s own physics body (and the object itself) aren't hit: cast from a character
        /// without hitting the character.
        /// </summary>
        public static bool Cast(Vector3 origin, Vector3 direction, float distance, out SurfaceHit hit,
            SurfaceLayers layers = SurfaceLayers.All, GameObject ignore = null)
        {
            hit = default;
            hit.Triangle = -1;
            if (direction.sqrMagnitude < 1e-12f || distance <= 0f)
                return false;
            direction.Normalize();

            bool found = false;
            if (layers != SurfaceLayers.Colliders && SurfacePaintLayer.Raycast(origin, direction, distance, out var painted)
                && (ignore == null || !painted.Object.transform.IsChildOf(ignore.transform)))
            {
                hit = painted;
                found = true;
            }

            if (layers != SurfaceLayers.Painted)
            {
                var body = ignore != null ? ignore.GetComponentInParent<Box3DBody>() : null;
                float reach = found ? hit.Distance : distance;
                if (SchematicPhysics.SphereCast(origin, 0f, direction, out var physics, out var hitObject, reach, ~0, body)
                    && physics.distance < reach)
                {
                    hit = new SurfaceHit
                    {
                        Material = SurfaceMaterialTag.Of(hitObject) ?? SurfaceLibrary.DefaultMaterial,
                        Point = physics.point,
                        Normal = physics.normal,
                        Distance = physics.distance,
                        Object = hitObject,
                        Painted = false,
                        Triangle = -1,
                    };
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// How blocked the straight line between two points is, 0 (clear) to 1 (fully blocked): each surface in the way
        /// blocks by its material's Absorption (at least <paramref name="minimumPerHit"/>), up to
        /// <paramref name="maxHits"/> surfaces. For muffling sounds behind walls.
        /// </summary>
        public static float Occlusion(Vector3 from, Vector3 to, GameObject ignoreFrom = null, GameObject ignoreTo = null,
            int maxHits = 3, float minimumPerHit = 0.35f)
        {
            var direction = to - from;
            float total = direction.magnitude;
            if (total < 0.01f)
                return 0f;
            direction /= total;
            float clear = 1f, travelled = 0f;
            var origin = from;
            for (int i = 0; i < maxHits; i++)
            {
                if (!Cast(origin, direction, total - travelled, out var hit, SurfaceLayers.All, ignoreFrom))
                    break;
                if (ignoreTo != null && hit.Object != null && hit.Object.transform.IsChildOf(ignoreTo.transform))
                    break; // reached the target itself
                float absorption = hit.Material != null ? hit.Material.Absorption : 0f;
                clear *= 1f - Mathf.Clamp01(Mathf.Max(minimumPerHit, absorption));
                // Step just past the surface and carry on.
                float step = hit.Distance + 0.05f;
                travelled += step;
                origin += direction * step;
                if (travelled >= total)
                    break;
            }
            return 1f - clear;
        }
    }
}
