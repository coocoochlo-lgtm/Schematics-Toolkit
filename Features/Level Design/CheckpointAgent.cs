using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using UnityEngine;

namespace Remedy.LevelDesign
{
    /// <summary>
    /// Moves its object's Box3D body to the position carried by the Respawn Signal (stopping it there).
    /// </summary>
    [SchematicBehaviorPath("Level Design/Checkpoint Agent")]
    public class CheckpointAgent : SchematicBehavior
    {
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
