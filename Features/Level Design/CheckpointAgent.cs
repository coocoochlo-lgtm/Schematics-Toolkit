using UnityEngine;

namespace Remedy.LevelDesign
{
    /// <summary>
    /// Moves its object's Rigidbody to the position carried by the Respawn Signal.
    /// </summary>
    [SchematicBehaviorPath("Level Design/Checkpoint Agent")]
    public class CheckpointAgent : SchematicBehavior
    {
        [InputSignal]
        [Parameter("Position", typeof(Vector3))]
        public readonly SignalHandle<Vector3> Respawn;

        protected override void OnStart()
        {
            var instance = Self;
            Respawn.Subscribe(position =>
            {
                var rigidbody = instance.GetComponent<Rigidbody>();
                if (rigidbody == null)
                    return;

                bool originallyKinematic = rigidbody.isKinematic;
                rigidbody.isKinematic = true;
                rigidbody.position = position;
                rigidbody.isKinematic = originallyKinematic;
            });
        }
    }
}
