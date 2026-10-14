using Box3D.Hybrid;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.Box3DPhysics
{
    /// <summary>
    /// Passes Box3D impacts on this body to the Schematic Behaviors on this GameObject (their OnImpact). Added
    /// automatically by Behaviors that need it; it must be on the object before Play so the body picks it up.
    /// </summary>
    [AddComponentMenu("Schematics/Physics/Schematic Impact Relay")]
    public class SchematicImpactRelay : MonoBehaviour, IBox3DHitReceiver
    {
        public void OnBox3DHit(in Box3DHit hit)
        {
            var other = hit.OtherBody != null ? hit.OtherBody.gameObject : null;
            SchematicObject.SendImpact(gameObject, new PhysicsImpact(hit.Point, hit.Direction, hit.ApproachSpeed, other));
        }
    }
}
