using System.Collections.Generic;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.LevelKit
{
    /// <summary>
    /// Spawns a prefab every Interval seconds while fewer than Max Alive of its spawns are around: enemies, crates,
    /// pickups. Schematic prefabs come from their pool (<see cref="SchematicsManager.Spawn(GameObject, Vector3, Quaternion, Transform)"/>);
    /// anything else is instantiated.
    /// </summary>
    /// <remarks>
    /// Spawning happens on the authority. Over the network, spawned objects need their own NetworkObject to appear on
    /// other peers; the Spawner doesn't register them with Netcode itself.
    /// </remarks>
    [SchematicDoc(@"{""name"": ""Spawner"", ""kind"": ""behavior"", ""menu"": ""Level Kit/Spawner"", ""description"": ""Spawns Prefab every Interval seconds while fewer than Max Alive of its spawns exist; Spawn One, Start/Stop Spawning and Despawn All Signals; On Spawned with the new object. AliveCount and IsSpawning are network synced."", ""variables"": [""Prefab"", ""Interval"", ""MaxAlive"", ""SpawnOnStart"", ""AutoSpawn"", ""SpawnOffset"", ""ScatterRadius"", ""IsSpawning"", ""AliveCount""], ""signals"": [""SpawnOne"", ""StartSpawning"", ""StopSpawning"", ""DespawnAll"", ""OnSpawned""], ""use_for"": [""enemy waves"", ""respawning crates and pickups"", ""ambient spawners""]}")]
    [SchematicBehaviorPath("Level Kit/Spawner")]
    public class Spawner : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""Prefab"", ""kind"": ""variable"", ""behavior"": ""Spawner"", ""type"": ""GameObject"", ""default"": ""null"", ""description"": ""What to spawn. Schematic prefabs are pooled.""}")]
        [Tooltip("What to spawn. Schematic prefabs are pooled.")]
        public readonly VariableHandle<GameObject> Prefab = (GameObject)null;

        [SchematicDoc(@"{""name"": ""Interval"", ""kind"": ""variable"", ""behavior"": ""Spawner"", ""type"": ""float"", ""default"": ""3"", ""description"": ""Seconds between spawns while spawning.""}")]
        [Tooltip("Seconds between spawns while spawning.")]
        public readonly VariableHandle<float> Interval = 3f;

        [SchematicDoc(@"{""name"": ""MaxAlive"", ""kind"": ""variable"", ""behavior"": ""Spawner"", ""type"": ""int"", ""default"": ""3"", ""description"": ""No more spawns while this many of its spawns are still around. 0: no limit.""}")]
        [Tooltip("No more spawns while this many of its spawns are still around. 0: no limit.")]
        public readonly VariableHandle<int> MaxAlive = 3;

        [SchematicDoc(@"{""name"": ""SpawnOnStart"", ""kind"": ""variable"", ""behavior"": ""Spawner"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Spawn one straight away.""}")]
        [Tooltip("Spawn one straight away.")]
        public readonly VariableHandle<bool> SpawnOnStart = true;

        [SchematicDoc(@"{""name"": ""AutoSpawn"", ""kind"": ""variable"", ""behavior"": ""Spawner"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Start spawning on its own. Off: waits for Start Spawning.""}")]
        [Tooltip("Start spawning on its own. Off: waits for Start Spawning.")]
        public readonly VariableHandle<bool> AutoSpawn = true;

        [SchematicDoc(@"{""name"": ""SpawnOffset"", ""kind"": ""variable"", ""behavior"": ""Spawner"", ""type"": ""Vector3"", ""default"": ""(0, 0.5, 0)"", ""description"": ""Where spawns appear, relative to the spawner (local).""}")]
        [Tooltip("Where spawns appear, relative to the spawner (local).")]
        public readonly VariableHandle<Vector3> SpawnOffset = new Vector3(0f, 0.5f, 0f);

        [SchematicDoc(@"{""name"": ""ScatterRadius"", ""kind"": ""variable"", ""behavior"": ""Spawner"", ""type"": ""float"", ""default"": ""0"", ""description"": ""Spawns land at a random spot this far around the spawn point (flat).""}")]
        [Tooltip("Spawns land at a random spot this far around the spawn point (flat).")]
        public readonly VariableHandle<float> ScatterRadius = 0f;

        [SchematicDoc(@"{""name"": ""IsSpawning"", ""kind"": ""variable"", ""behavior"": ""Spawner"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Whether it's spawning on its timer. Network synced.""}")]
        [Tooltip("Whether it's spawning on its timer.")]
        [NetworkSync]
        public readonly VariableHandle<bool> IsSpawning = false;

        [SchematicDoc(@"{""name"": ""AliveCount"", ""kind"": ""variable"", ""behavior"": ""Spawner"", ""type"": ""int"", ""default"": ""0"", ""description"": ""How many of its spawns are still around. Read-only. Network synced.""}")]
        [Tooltip("How many of its spawns are still around. Read-only.")]
        [NetworkSync]
        public readonly VariableHandle<int> AliveCount = 0;

        private readonly VariableHandle<float> _nextAt = 0f;

        [SchematicDoc(@"{""name"": ""SpawnOne"", ""kind"": ""signal"", ""behavior"": ""Spawner"", ""direction"": ""input"", ""description"": ""Spawns one now (Max Alive still applies)."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle SpawnOne;

        [SchematicDoc(@"{""name"": ""StartSpawning"", ""kind"": ""signal"", ""behavior"": ""Spawner"", ""direction"": ""input"", ""description"": ""Starts the timer."", ""how"": ""Send it with a Send a Signal node, e.g. from a Switch or Pressure Plate.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle StartSpawning;

        [SchematicDoc(@"{""name"": ""StopSpawning"", ""kind"": ""signal"", ""behavior"": ""Spawner"", ""direction"": ""input"", ""description"": ""Stops the timer (spawned objects stay)."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle StopSpawning;

        [SchematicDoc(@"{""name"": ""DespawnAll"", ""kind"": ""signal"", ""behavior"": ""Spawner"", ""direction"": ""input"", ""description"": ""Removes everything it spawned."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle DespawnAll;

        [SchematicDoc(@"{""name"": ""OnSpawned"", ""kind"": ""signal"", ""behavior"": ""Spawner"", ""direction"": ""output"", ""params"": [{""name"": ""Spawned"", ""type"": ""GameObject""}], ""description"": ""Sent with each new object."", ""how"": ""React to it with On Signal Received (aim it at the player, play an effect).""}")]
        [OutputSignal, NetworkSync]
        [Parameter("Spawned", typeof(GameObject))]
        public readonly SignalHandle<GameObject> OnSpawned;

        private static readonly Dictionary<BehaviorInstance, List<GameObject>> _spawned = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _spawned.Clear();

        protected override void OnStart()
        {
            var instance = Self;
            SpawnOne.Subscribe(() => TrySpawn(instance));
            StartSpawning.Subscribe(() => SetSpawning(instance, true));
            StopSpawning.Subscribe(() => SetSpawning(instance, false));
            DespawnAll.Subscribe(() => RemoveAll(instance));

            if (!LevelKitQuery.HasAuthority(instance))
                return;
            IsSpawning.Value = AutoSpawn.Value;
            _nextAt.Value = Time.time + Interval.Value;
            if (SpawnOnStart.Value)
                TrySpawn(instance);
        }

        protected override void OnFixedUpdate()
        {
            var instance = Self;
            if (!LevelKitQuery.HasAuthority(instance))
                return;
            int alive = Prune(instance);
            if (alive != AliveCount.Value)
                AliveCount.Value = alive;
            if (IsSpawning.Value && Time.time >= _nextAt.Value)
            {
                _nextAt.Value = Time.time + Mathf.Max(0.05f, Interval.Value);
                TrySpawn(instance);
            }
        }

        protected override void OnDestroy() => _spawned.Remove(Self);

        /// <summary>Spawns one from the selected spawner now. Returns the object, or null (limit reached, no prefab).</summary>
        public GameObject SpawnNow() => TrySpawn(Selected);

        private void SetSpawning(BehaviorInstance instance, bool on)
        {
            if (!LevelKitQuery.HasAuthority(instance)) return;
            using (Pick(instance))
            {
                if (on && !IsSpawning.Value) _nextAt.Value = Time.time + Interval.Value;
                IsSpawning.Value = on;
            }
        }

        private GameObject TrySpawn(BehaviorInstance instance)
        {
            if (instance == null || !LevelKitQuery.HasAuthority(instance))
                return null;
            using (Pick(instance))
            {
                var prefab = Prefab.Value;
                if (prefab == null)
                    return null;
                int max = MaxAlive.Value;
                if (max > 0 && Prune(instance) >= max)
                    return null;

                var transform = instance.Transform;
                var position = transform.TransformPoint(SpawnOffset.Value);
                float scatter = ScatterRadius.Value;
                if (scatter > 0f)
                {
                    var circle = Random.insideUnitCircle * scatter;
                    position += transform.right * circle.x + transform.forward * circle.y;
                }

                GameObject spawned;
                if (prefab.TryGetComponent(out SchematicObject _))
                    spawned = SchematicsManager.Spawn(prefab, position, transform.rotation)?.gameObject;
                else
                    spawned = Object.Instantiate(prefab, position, transform.rotation);
                if (spawned == null)
                    return null;

                if (!_spawned.TryGetValue(instance, out var list))
                    _spawned[instance] = list = new List<GameObject>();
                list.Add(spawned);
                AliveCount.Value = list.Count;
                OnSpawned.Invoke(spawned);
                return spawned;
            }
        }

        // Forgets spawns that were destroyed or went back to their pool. Returns how many are left.
        private static int Prune(BehaviorInstance instance)
        {
            if (!_spawned.TryGetValue(instance, out var list))
                return 0;
            list.RemoveAll(go => go == null || !go.activeInHierarchy || (go.TryGetComponent(out SchematicObject schematic) && !schematic.IsSpawned));
            return list.Count;
        }

        private void RemoveAll(BehaviorInstance instance)
        {
            if (!LevelKitQuery.HasAuthority(instance) || !_spawned.TryGetValue(instance, out var list))
                return;
            foreach (var go in list.ToArray())
            {
                if (go == null) continue;
                if (go.TryGetComponent(out SchematicObject schematic)) schematic.Destroy();
                else Object.Destroy(go);
            }
            list.Clear();
            using (Pick(instance))
                AliveCount.Value = 0;
        }

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            var point = context.Transform.TransformPoint(context.Get(SpawnOffset));
            Gizmos.color = new Color(0.75f, 0.45f, 1f, 0.9f);
            Gizmos.DrawWireSphere(point, Mathf.Max(0.3f, context.Get(ScatterRadius)));
            Gizmos.DrawLine(point, point + context.Transform.forward);
        }
    }
}
