// project armada

#pragma warning disable 0414

using BlueGraph;
using Box3D;
using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using System;
using Unity.Mathematics;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>How a force is applied, as in Unity's ForceMode.</summary>
    public enum BodyForceMode
    {
        /// <summary>A push over time, scaled by mass: apply it every step (On Fixed Update) for a steady push.</summary>
        Force,
        /// <summary>A push over time that ignores mass (the same for a crate and a truck).</summary>
        Acceleration,
        /// <summary>An instant kick, scaled by mass: jumps, hits, explosions.</summary>
        Impulse,
        /// <summary>An instant change of speed that ignores mass.</summary>
        VelocityChange,
    }

    /// <summary>Whether a direction is in world axes or the object's own (forward is its blue axis).</summary>
    public enum BodySpace { World, Local }

    /// <summary>Finding the Box3D body of whatever a node's Object input holds.</summary>
    internal static class BodyNodeUtility
    {
        /// <summary>
        /// The body of a GameObject or component (or of the object running the graph when there's none): its own, or
        /// its nearest parent's. Null when it has none, or its body isn't created yet.
        /// </summary>
        public static Box3DBody Find(object target, GameObject running)
        {
            var body = target switch
            {
                Box3DBody direct => direct,
                GameObject gameObject when gameObject != null => gameObject.GetComponentInParent<Box3DBody>(),
                Component component when component != null => component.GetComponentInParent<Box3DBody>(),
                _ => null,
            };
            if (body == null && (target == null || (target is UnityEngine.Object unityObject && unityObject == null)) && running != null)
                body = running.GetComponentInParent<Box3DBody>();
            return body != null && body.IsLive() ? body : null;
        }

        /// <summary>A body other peers simulate only follows what they send: pushing it here would do nothing.</summary>
        public static bool CanPush(Box3DBody body) => body != null && !body.IsNetworkProxy();

        public static Vector3 ToWorld(Box3DBody body, Vector3 direction, BodySpace space)
            => space == BodySpace.Local ? body.transform.TransformDirection(direction) : direction;
    }

    [SchematicDoc(@"{""name"": ""Get Body"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Physics"", ""description"": ""The Box3D body of an object (its own or its nearest parent's; with nothing connected, the object running the graph), with its velocity, speed, spin and mass. Has Body is false when there's none."", ""inputs"": [{""name"": ""Object"", ""type"": ""GameObject""}], ""outputs"": [{""name"": ""Body"", ""type"": ""Box3DBody""}, {""name"": ""Has Body"", ""type"": ""bool""}, {""name"": ""Velocity"", ""type"": ""Vector3""}, {""name"": ""Speed"", ""type"": ""float""}, {""name"": ""Angular Velocity"", ""type"": ""Vector3""}, {""name"": ""Mass"", ""type"": ""float""}], ""use_for"": [""reading how fast something moves"", ""checking an object is physical"", ""passing a body to Add Force""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Physics)]
    [Node(Name = "Get Body", Path = "Physics"), Tags("Object")]
    public class GetBody : SchematicGraphNode
    {
        [Input("Object")]
        public UnityEngine.Object Object;

        [Output("Body")] public Box3DBody Body;
        [Output("Has Body")] public bool HasBody;
        [Output("Velocity")] public Vector3 Velocity;
        [Output("Speed")] public float Speed;
        [Output("Angular Velocity")] public Vector3 AngularVelocity;
        [Output("Mass")] public float Mass;

        public override object OnRequestValue(Port port)
        {
            var body = BodyNodeUtility.Find(GetInputValue<UnityEngine.Object>("Object", null), RunningInstance);
            switch (port.Name)
            {
                case "Body": return body;
                case "Has Body": return body != null;
                case "Velocity": return body != null ? body.GetVelocity() : Vector3.zero;
                case "Speed": return body != null ? body.GetVelocity().magnitude : 0f;
                case "Angular Velocity": return body != null ? (Vector3)body.Body.AngularVelocity : Vector3.zero;
                case "Mass": return body != null ? body.GetMass() : 0f;
            }
            return base.OnRequestValue(port);
        }
    }

    [SchematicDoc(@"{""name"": ""Add Force"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Physics"", ""description"": ""Pushes an object's Box3D body (with nothing connected, the object running the graph's). Mode as in Unity: Force and Acceleration push over time (run it every step, e.g. from On Fixed Update), Impulse and Velocity Change kick at once (jumps, hits, explosions); Acceleration and Velocity Change ignore mass. Space Local takes the direction in the object's own axes. At Point pushes at a world position instead of the center, which also spins it."", ""inputs"": [{""name"": ""Object"", ""type"": ""GameObject""}, {""name"": ""Force"", ""type"": ""Vector3""}, {""name"": ""Point"", ""type"": ""Vector3""}], ""use_for"": [""jumps and knockback"", ""explosions"", ""thrusters and wind"", ""launching projectiles""], ""notes"": ""Runs when flow reaches it. Does nothing to kinematic bodies, or bodies another player simulates.""}")]
    [Serializable]
    [NodeColor(NodeColors.Physics)]
    [Node(Name = "Add Force", Path = "Physics"), Tags("Object")]
    public class AddBodyForce : SchematicActionNode
    {
        [Input("Object")]
        public UnityEngine.Object Object;
        [Input("Force", Editable = true)]
        public Vector3 Force = Vector3.up;
        [Editable]
        public BodyForceMode Mode = BodyForceMode.Impulse;
        [Editable]
        public BodySpace Space = BodySpace.World;
        /// <summary>Push at Point (world position) rather than the center of mass, so it spins too.</summary>
        [Editable]
        public bool AtPoint;
        [Input("Point", Editable = true)]
        public Vector3 Point;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var body = BodyNodeUtility.Find(GetInputValue<UnityEngine.Object>("Object", null), instance);
            if (!BodyNodeUtility.CanPush(body))
                return;

            var force = BodyNodeUtility.ToWorld(body, GetInputValue("Force", Force), Space);
            // Acceleration and Velocity Change are the same pushes with mass taken out.
            if (Mode == BodyForceMode.Acceleration || Mode == BodyForceMode.VelocityChange)
                force *= body.GetMass();
            bool instant = Mode == BodyForceMode.Impulse || Mode == BodyForceMode.VelocityChange;

            var native = body.Body;
            float3 amount = force;
            if (AtPoint)
            {
                float3 point = GetInputValue("Point", Point);
                if (instant) native.ApplyLinearImpulse(amount, point, true);
                else native.ApplyForce(amount, point, true);
            }
            else if (instant)
                native.ApplyLinearImpulseToCenter(amount, true);
            else
                native.ApplyForceToCenter(amount, true);
        }
    }

    [SchematicDoc(@"{""name"": ""Add Torque"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Physics"", ""description"": ""Spins an object's Box3D body around the Torque axis (its length is how hard; with nothing connected, the object running the graph's). Impulse spins at once; Force spins over time, so run it every step (e.g. from On Fixed Update). Space Local takes the axis in the object's own axes."", ""inputs"": [{""name"": ""Object"", ""type"": ""GameObject""}, {""name"": ""Torque"", ""type"": ""Vector3""}], ""use_for"": [""spinning pickups or debris"", ""rolling balls"", ""flips and tumbles""], ""notes"": ""Runs when flow reaches it. Does nothing to kinematic bodies, or bodies another player simulates.""}")]
    [Serializable]
    [NodeColor(NodeColors.Physics)]
    [Node(Name = "Add Torque", Path = "Physics"), Tags("Object")]
    public class AddBodyTorque : SchematicActionNode
    {
        public enum TorqueMode { Force, Impulse }

        [Input("Object")]
        public UnityEngine.Object Object;
        [Input("Torque", Editable = true)]
        public Vector3 Torque = Vector3.up;
        [Editable]
        public TorqueMode Mode = TorqueMode.Impulse;
        [Editable]
        public BodySpace Space = BodySpace.World;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var body = BodyNodeUtility.Find(GetInputValue<UnityEngine.Object>("Object", null), instance);
            if (!BodyNodeUtility.CanPush(body))
                return;

            float3 torque = BodyNodeUtility.ToWorld(body, GetInputValue("Torque", Torque), Space);
            var native = body.Body;
            if (Mode == TorqueMode.Impulse)
                native.ApplyAngularImpulse(torque, true);
            else
                native.ApplyTorque(torque, true);
        }
    }

    [SchematicDoc(@"{""name"": ""Set Velocity"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Physics"", ""description"": ""Sets how fast an object's Box3D body moves (with nothing connected, the object running the graph's), ignoring mass. Space Local takes it in the object's own axes. Keep Vertical keeps its current up/down speed, so gravity and jumps still work."", ""inputs"": [{""name"": ""Object"", ""type"": ""GameObject""}, {""name"": ""Velocity"", ""type"": ""Vector3""}], ""use_for"": [""moving platforms and conveyors"", ""dashes"", ""stopping something dead""], ""notes"": ""Runs when flow reaches it. Does nothing to kinematic bodies, or bodies another player simulates.""}")]
    [Serializable]
    [NodeColor(NodeColors.Physics)]
    [Node(Name = "Set Velocity", Path = "Physics"), Tags("Object")]
    public class SetBodyVelocity : SchematicActionNode
    {
        [Input("Object")]
        public UnityEngine.Object Object;
        [Input("Velocity", Editable = true)]
        public Vector3 Velocity;
        [Editable]
        public BodySpace Space = BodySpace.World;
        [Editable]
        public bool KeepVertical;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var body = BodyNodeUtility.Find(GetInputValue<UnityEngine.Object>("Object", null), instance);
            if (!BodyNodeUtility.CanPush(body))
                return;

            var velocity = BodyNodeUtility.ToWorld(body, GetInputValue("Velocity", Velocity), Space);
            if (KeepVertical)
                velocity.y = body.GetVelocity().y;
            body.SetVelocity(velocity);
            body.WakeUp();
        }
    }
}
