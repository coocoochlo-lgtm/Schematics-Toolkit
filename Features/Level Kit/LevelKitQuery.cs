using System;
using System.Collections.Generic;
using Box3D;
using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using Remedy.Framework;
using Remedy.Schematics;
using Unity.Mathematics;
using UnityEngine;

namespace Remedy.LevelKit
{
    /// <summary>
    /// Shared helpers for the Level Kit Behaviors: "who is standing in this box / near this point" queries against the
    /// Box3D world (the toolkit doesn't use Unity's built-in physics, so Unity triggers would never fire), Int Tag
    /// filtering, and the "does this peer decide?" network check.
    /// </summary>
    /// <remarks>
    /// The networking rule every Level Kit Behavior follows: the object's authority decides (detects overlaps, runs
    /// timers, changes state, sends the output Signals), and every peer animates from the synced Variables. So a door
    /// swings on everyone's screen because <c>IsOpen</c> is synced, not because each peer ran the same logic.
    /// </remarks>
    public static class LevelKitQuery
    {
        private const int MaxShapes = 64;

        [ThreadStatic] private static float3[] _corners;
        [ThreadStatic] private static List<GameObject> _scratch;

        /// <summary>
        /// Whether this peer decides for the object: true offline, and on the object's authority when networked.
        /// </summary>
        public static bool HasAuthority(BehaviorInstance instance)
        {
            if (instance == null || instance.Owner == null || GraphNetwork.HasAuthority == null)
                return true;
            return GraphNetwork.HasAuthority(instance.Owner.gameObject);
        }

        /// <summary>
        /// The GameObjects of every Box3D body overlapping an oriented box (world space), skipping bodies under
        /// <paramref name="ignore"/>. Static geometry without a body isn't reported. Returns the count.
        /// </summary>
        public static int OverlapBox(Vector3 center, Quaternion rotation, Vector3 halfExtents, LayerMask layers, List<GameObject> results, Transform ignore = null)
        {
            results.Clear();
            if (!Application.isPlaying)
                return 0;
            var world = SchematicPhysics.World;
            if (!world.IsValid)
                return 0;

            _corners ??= new float3[8];
            int i = 0;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        _corners[i++] = rotation * Vector3.Scale(halfExtents, new Vector3(x, y, z));

            Span<ShapeId> shapes = stackalloc ShapeId[MaxShapes];
            int count = world.OverlapShape(center, _corners, 0f, SchematicPhysics.FilterFor(layers), shapes);
            Collect(shapes, count, results, ignore);
            return results.Count;
        }

        /// <summary>The GameObjects of every Box3D body overlapping a sphere, skipping bodies under <paramref name="ignore"/>.</summary>
        public static int OverlapSphere(Vector3 center, float radius, LayerMask layers, List<GameObject> results, Transform ignore = null)
        {
            results.Clear();
            if (!Application.isPlaying || radius <= 0f)
                return 0;
            var world = SchematicPhysics.World;
            if (!world.IsValid)
                return 0;

            Span<float3> point = stackalloc float3[1];
            point[0] = float3.zero;
            Span<ShapeId> shapes = stackalloc ShapeId[MaxShapes];
            int count = world.OverlapShape(center, point, radius, SchematicPhysics.FilterFor(layers), shapes);
            Collect(shapes, count, results, ignore);
            return results.Count;
        }

        private static void Collect(Span<ShapeId> shapes, int count, List<GameObject> results, Transform ignore)
        {
            for (int i = 0; i < count; i++)
            {
                var body = SchematicPhysics.BodyOf(shapes[i]);
                if (body == null)
                    continue;
                var go = body.gameObject;
                if (ignore != null && go.transform.IsChildOf(ignore))
                    continue;
                if (!results.Contains(go))
                    results.Add(go);
            }
        }

        /// <summary>
        /// The object that "is" something for gameplay: the Schematic Object a body belongs to (a player's root),
        /// or the body itself.
        /// </summary>
        public static GameObject AgentOf(GameObject go)
        {
            if (go == null) return null;
            var schematic = go.GetComponentInParent<SchematicObject>();
            return schematic != null ? schematic.gameObject : go;
        }

        /// <summary>Untagged accepts anything; any other tag must be on the agent (or the body).</summary>
        public static bool Matches(GameObject agent, IntTag tag)
        {
            if (agent == null) return false;
            if (tag.Value == IntTags.Untagged) return true;
            return agent.HasIntTag(tag);
        }

        /// <summary>The agents (see <see cref="AgentOf"/>) matching a tag inside an oriented box. Returns the count.</summary>
        public static int AgentsInBox(Vector3 center, Quaternion rotation, Vector3 halfExtents, LayerMask layers, IntTag tag, List<GameObject> results, Transform ignore = null)
        {
            _scratch ??= new List<GameObject>();
            OverlapBox(center, rotation, halfExtents, layers, _scratch, ignore);
            return ToAgents(_scratch, tag, results);
        }

        /// <summary>The agents matching a tag inside a sphere. Returns the count.</summary>
        public static int AgentsInSphere(Vector3 center, float radius, LayerMask layers, IntTag tag, List<GameObject> results, Transform ignore = null)
        {
            _scratch ??= new List<GameObject>();
            OverlapSphere(center, radius, layers, _scratch, ignore);
            return ToAgents(_scratch, tag, results);
        }

        private static int ToAgents(List<GameObject> bodies, IntTag tag, List<GameObject> results)
        {
            results.Clear();
            foreach (var body in bodies)
            {
                var agent = AgentOf(body);
                if (Matches(agent, tag) && !results.Contains(agent))
                    results.Add(agent);
            }
            return results.Count;
        }

        /// <summary>Moves an agent to a pose: its Box3D body is teleported and stopped; anything else just moves.</summary>
        public static void Teleport(GameObject agent, Vector3 position, Quaternion? rotation = null)
        {
            if (agent == null) return;
            var body = agent.GetComponent<Box3DBody>();
            if (body == null)
            {
                agent.transform.position = position;
                if (rotation.HasValue) agent.transform.rotation = rotation.Value;
                return;
            }
            body.Teleport(position);
            if (rotation.HasValue) body.TeleportRotation(rotation.Value);
            body.SetVelocity(Vector3.zero);
        }

        /// <summary>Shows or hides every renderer under a transform (pickups hide instead of being destroyed, so it syncs).</summary>
        public static void SetRenderersVisible(Transform root, bool visible)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled != visible)
                    renderer.enabled = visible;
        }

        /// <summary>Tints every renderer under a transform (URP's _BaseColor and the built-in _Color) without new materials.</summary>
        public static void Tint(Transform root, Color color)
        {
            if (root == null) return;
            var block = new MaterialPropertyBlock();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>Draws a box Gizmo in an object's local space.</summary>
        public static void DrawLocalBox(Transform transform, Vector3 center, Vector3 size, Color color, bool filled = false)
        {
            var previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = color;
            if (filled)
            {
                Gizmos.color = new Color(color.r, color.g, color.b, color.a * 0.15f);
                Gizmos.DrawCube(center, size);
                Gizmos.color = color;
            }
            Gizmos.DrawWireCube(center, size);
            Gizmos.matrix = previous;
        }
    }

    /// <summary>
    /// Who carries which keys. Key Items give keys, Locks and Doors check (and maybe take) them. Keys are plain
    /// string ids ("Red Key"), so a key and its lock only need to agree on a name.
    /// </summary>
    /// <remarks>
    /// Kept on the peer that decides (the authority), which is also the only one that checks it. A HUD on other peers
    /// should listen to the Key Item's synced <c>OnCollected</c> Signal instead of reading this.
    /// </remarks>
    public static class KeyRing
    {
        private static readonly Dictionary<GameObject, HashSet<string>> _keys = new();

        /// <summary>Raised when a holder gets a key: (holder, key id).</summary>
        public static event Action<GameObject, string> KeyAdded;

        /// <summary>Raised when a key is taken from a holder: (holder, key id).</summary>
        public static event Action<GameObject, string> KeyRemoved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _keys.Clear();
            KeyAdded = null;
            KeyRemoved = null;
        }

        /// <summary>Whether the holder (or the Schematic Object it belongs to) carries the key. An empty id needs no key.</summary>
        public static bool Has(GameObject holder, string keyId)
        {
            if (string.IsNullOrEmpty(keyId)) return true;
            holder = LevelKitQuery.AgentOf(holder);
            return holder != null && _keys.TryGetValue(holder, out var keys) && keys.Contains(keyId);
        }

        /// <summary>Gives the holder a key.</summary>
        public static void Give(GameObject holder, string keyId)
        {
            holder = LevelKitQuery.AgentOf(holder);
            if (holder == null || string.IsNullOrEmpty(keyId)) return;
            if (!_keys.TryGetValue(holder, out var keys))
                _keys[holder] = keys = new HashSet<string>();
            if (keys.Add(keyId))
                KeyAdded?.Invoke(holder, keyId);
        }

        /// <summary>Takes a key from the holder. False if they didn't have it.</summary>
        public static bool Take(GameObject holder, string keyId)
        {
            holder = LevelKitQuery.AgentOf(holder);
            if (holder == null || string.IsNullOrEmpty(keyId) || !_keys.TryGetValue(holder, out var keys) || !keys.Remove(keyId))
                return false;
            KeyRemoved?.Invoke(holder, keyId);
            return true;
        }

        /// <summary>The keys a holder carries (empty if none).</summary>
        public static IReadOnlyCollection<string> KeysOf(GameObject holder)
        {
            holder = LevelKitQuery.AgentOf(holder);
            return holder != null && _keys.TryGetValue(holder, out var keys) ? keys : (IReadOnlyCollection<string>)Array.Empty<string>();
        }

        /// <summary>Takes every key from a holder (e.g. on death).</summary>
        public static void Clear(GameObject holder)
        {
            holder = LevelKitQuery.AgentOf(holder);
            if (holder == null || !_keys.TryGetValue(holder, out var keys)) return;
            _keys.Remove(holder);
            foreach (var key in keys)
                KeyRemoved?.Invoke(holder, key);
        }
    }
}
