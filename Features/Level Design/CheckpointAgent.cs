using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using UnityEngine;

namespace Remedy.LevelDesign
{
    /// <summary>
    /// Moves its object's Box3D body to the position carried by the Respawn Signal (stopping it there).
    /// </summary>
    [SchematicDoc(@"{""name"": ""Checkpoint Agent"", ""kind"": ""behavior"", ""menu"": ""Level Design/Checkpoint Agent"", ""description"": ""Moves its object's Box3D body to the position carried by the Respawn Signal (stopping it there)."", ""signals"": [""Respawn""], ""use_for"": [""respawning the player at checkpoints""]}")]
    [SchematicBehaviorPath("Level Design/Checkpoint Agent")]
    public class CheckpointAgent : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""Respawn"", ""kind"": ""signal"", ""behavior"": ""Checkpoint Agent"", ""direction"": ""input"", ""params"": [{""name"": ""Position"", ""type"": ""Vector3""}], ""description"": ""Tells Checkpoint Agent to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [InputSignal]
        [Parameter("Position", typeof(Vector3))]
        public readonly SignalHandle<Vector3> Respawn;

        protected override void OnStart()
        {
            Respawn.Subscribe(position =>
            {
                var body = Self.GetComponent<Box3DBody>();
                if (body == null)
                {
                    Self.Transform.position = position;
                    return;
                }

                body.Teleport(position);
                body.SetVelocity(Vector3.zero);
            });
        }
    }
}
