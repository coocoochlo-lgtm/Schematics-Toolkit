using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// Forwards Unity physics messages from a child GameObject to the Schematic Behaviors attached to it.
    /// Added automatically at runtime; there's no need to add it by hand.
    /// </summary>
    [AddComponentMenu("")]
    public class SchematicPhysicsRelay : MonoBehaviour
    {
        internal SchematicObject Owner;

        private void Awake()
        {
            hideFlags |= HideFlags.HideInInspector;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (Owner != null) Owner.ForwardPhysics(gameObject, BehaviorRuntime.PhysicsMessage.CollisionEnter, collision, null);
        }

        private void OnCollisionExit(Collision collision)
        {
            if (Owner != null) Owner.ForwardPhysics(gameObject, BehaviorRuntime.PhysicsMessage.CollisionExit, collision, null);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (Owner != null) Owner.ForwardPhysics(gameObject, BehaviorRuntime.PhysicsMessage.TriggerEnter, null, other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (Owner != null) Owner.ForwardPhysics(gameObject, BehaviorRuntime.PhysicsMessage.TriggerExit, null, other);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (Owner != null) Owner.ForwardStay(gameObject, BehaviorRuntime.PhysicsMessage.CollisionStay, collision, null);
        }

        private void OnTriggerStay(Collider other)
        {
            if (Owner != null) Owner.ForwardStay(gameObject, BehaviorRuntime.PhysicsMessage.TriggerStay, null, other);
        }
    }
}
