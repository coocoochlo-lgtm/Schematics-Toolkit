using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.LevelDesign
{
    // TODO: Create a new Level Manager to handle spawning and Enemy Pools, but implement using ScriptableEvents
    /// <summary>
    /// A place the player can be spawned. <see cref="Spawn"/> acts on the selected instance, e.g.
    /// <c>point.GetSchematicBehavior&lt;SpawnPoint&gt;().Spawn(player)</c>.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Spawn Point"", ""kind"": ""behavior"", ""menu"": ""Level Design/Spawn Point"", ""description"": ""A place the player can be spawned. Spawn acts on the selected instance, e.g. point.GetSchematicBehavior<SpawnPoint>().Spawn(player)."", ""variables"": [""AssignToLevel"", ""MustNotBeRotated"", ""SpawnOffset"", ""ExtraSpawnBehaviour""], ""signals"": [""OnSpawn""], ""use_for"": [""where players start"", ""respawn locations""]}")]
    [SchematicBehaviorPath("Level Design/Spawn Point")]
    public class SpawnPoint : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""AssignToLevel"", ""kind"": ""variable"", ""behavior"": ""Spawn Point"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""If True, this Spawn Point will be assigned to the Level it's inside of, instead of being us ed globally.""}")]
        [Tooltip("If True, this Spawn Point will be assigned to the Level it's inside of, instead of being us ed globally.")]
        public readonly VariableHandle<bool> AssignToLevel = false;
        [SchematicDoc(@"{""name"": ""MustNotBeRotated"", ""kind"": ""variable"", ""behavior"": ""Spawn Point"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""If True, this Spawn Point can only be used when the orientation is close to normal.""}")]
        [Tooltip("If True, this Spawn Point can only be used when the orientation is close to normal.")]
        public readonly VariableHandle<bool> MustNotBeRotated = false;
        [SchematicDoc(@"{""name"": ""SpawnOffset"", ""kind"": ""variable"", ""behavior"": ""Spawn Point"", ""type"": ""Vector3"", ""default"": ""Vector3.up * 1.5f"", ""description"": ""Offset from the Spawner that the Player is Spawned at. This value is shared with all other Spawn Points.""}")]
        [Tooltip("Offset from the Spawner that the Player is Spawned at. This value is shared with all other Spawn Points.")]
        public readonly VariableHandle<Vector3> SpawnOffset = Vector3.up * 1.5f;

        [SchematicDoc(@"{""name"": ""OnSpawn"", ""kind"": ""signal"", ""behavior"": ""Spawn Point"", ""direction"": ""output"", ""description"": ""Sent when this Spawner is used."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("Sent when this Spawner is used.")]
        [OutputSignal]
        [NetworkSync(SignalSender.Authority)]
        public readonly SignalHandle OnSpawn;

        /// <summary>Extra code to run on whatever is spawned here.</summary>
        [SchematicDoc(@"{""name"": ""ExtraSpawnBehaviour"", ""kind"": ""variable"", ""behavior"": ""Spawn Point"", ""type"": ""Action<GameObject>"", ""default"": ""null"", ""description"": ""Extra code to run on whatever is spawned here.""}")]
        public readonly VariableHandle<Action<GameObject>> ExtraSpawnBehaviour = (Action<GameObject>)null;

        /// <summary>
        /// Customizable function used to determine whether this Spawn Point is valid or not.
        /// </summary>
        protected virtual bool CanSpawn(BehaviorInstance instance)
        {
            // Add other Spawn Point Conditions
            return !MustNotBeRotated.Value || Vector3.Dot(instance.Transform.up, Vector3.up) > 0.99f;
        }

        /// <summary>
        /// Moves the given object to the selected Spawn Point.
        /// </summary>
        public void Spawn(GameObject gameObject)
        {
            var instance = Selected;
            gameObject.transform.position = instance.Transform.position + SpawnOffset.Value;
            OnSpawn.Invoke();
            ExtraSpawnBehaviour.Value?.Invoke(gameObject);
        }
    }
}
