using System.Collections.Generic;
using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.LevelKit
{
    /// <summary>How a <see cref="MovingPlatform"/> goes round its waypoints.</summary>
    public enum PlatformLoop
    {
        /// <summary>To the last waypoint and back again.</summary>
        PingPong,
        /// <summary>To the last waypoint, then straight on to the first.</summary>
        Loop,
        /// <summary>To the last waypoint, then stops (Start Moving sends it back).</summary>
        Once,
    }

    /// <summary>
    /// A platform (lift, ferry, sliding wall) that travels through waypoints at a steady speed, pausing at each, and
    /// carries whatever stands on it.
    /// </summary>
    /// <remarks>
    /// Waypoints are offsets from where the platform starts (in its own rotated space), so a prefab works wherever it's
    /// placed. Put a kinematic <see cref="Box3DBody"/> on the platform: kinematic bodies follow their Transform, so
    /// moving the Transform each physics step moves the body, and solid things on top get the right contact velocity.<br/>
    /// Riders: each step, dynamic Box3D bodies inside the Rider Zone (a box on top of the platform) are moved by the
    /// same amount the platform moved, so characters (hover controllers included) and crates ride along instead of
    /// sliding off. Bodies simulated by another peer (network proxies) are left to their owner, whose copy of the
    /// platform carries them.<br/>
    /// Network: <c>Travel</c> (distance along the path) is synced, and every peer moves the platform from it.
    /// </remarks>
    [SchematicDoc(@"{""name"": ""Moving Platform"", ""kind"": ""behavior"", ""menu"": ""Level Kit/Moving Platform"", ""description"": ""Travels through waypoints (offsets from its start) at Speed, ping-pong, loop or once, waiting at each, and carries dynamic Box3D bodies standing on it (players, crates). Travel, IsMoving and Direction are network synced."", ""variables"": [""Waypoints"", ""Mode"", ""Speed"", ""WaitTime"", ""AutoStart"", ""CarryRiders"", ""RiderZoneSize"", ""RiderZoneHeight"", ""Travel"", ""IsMoving"", ""Direction""], ""signals"": [""StartMoving"", ""StopMoving"", ""Reverse"", ""OnArrived""], ""use_for"": [""moving platforms"", ""elevators and lifts"", ""ferries"", ""sliding walls""], ""notes"": ""Give it a kinematic Box3D body and a shape.""}")]
    [SchematicBehaviorPath("Level Kit/Moving Platform")]
    public class MovingPlatform : SchematicBehavior
    {
        #region Variables

        [SchematicDoc(@"{""name"": ""Waypoints"", ""kind"": ""variable"", ""behavior"": ""Moving Platform"", ""type"": ""Vector3[]"", ""default"": ""[(0,0,0), (0,0,6)]"", ""description"": ""Stops along the path, as offsets from where the platform starts (in its rotated space). The first is usually (0,0,0).""}")]
        [Tooltip("Stops along the path, as offsets from where the platform starts (in its rotated space). The first is usually (0,0,0).")]
        public readonly VariableHandle<Vector3[]> Waypoints = new[] { Vector3.zero, new Vector3(0f, 0f, 6f) };

        [SchematicDoc(@"{""name"": ""Mode"", ""kind"": ""variable"", ""behavior"": ""Moving Platform"", ""type"": ""PlatformLoop"", ""default"": ""PingPong"", ""description"": ""Ping Pong (there and back), Loop (back to the first after the last), or Once.""}")]
        [Tooltip("Ping Pong (there and back), Loop (back to the first after the last), or Once.")]
        public readonly VariableHandle<PlatformLoop> Mode = PlatformLoop.PingPong;

        [SchematicDoc(@"{""name"": ""Speed"", ""kind"": ""variable"", ""behavior"": ""Moving Platform"", ""type"": ""float"", ""default"": ""2"", ""description"": ""Metres per second.""}")]
        [Tooltip("Metres per second.")]
        public readonly VariableHandle<float> Speed = 2f;

        [SchematicDoc(@"{""name"": ""WaitTime"", ""kind"": ""variable"", ""behavior"": ""Moving Platform"", ""type"": ""float"", ""default"": ""1"", ""description"": ""Seconds it waits at each waypoint.""}")]
        [Tooltip("Seconds it waits at each waypoint.")]
        public readonly VariableHandle<float> WaitTime = 1f;

        [SchematicDoc(@"{""name"": ""AutoStart"", ""kind"": ""variable"", ""behavior"": ""Moving Platform"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Starts moving on its own. Off: waits for Start Moving (e.g. from a Switch).""}")]
        [Tooltip("Starts moving on its own. Off: waits for Start Moving (e.g. from a Switch).")]
        public readonly VariableHandle<bool> AutoStart = true;

        [SchematicDoc(@"{""name"": ""CarryRiders"", ""kind"": ""variable"", ""behavior"": ""Moving Platform"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Move dynamic bodies standing on it along with it.""}")]
        [Tooltip("Move dynamic bodies standing on it along with it.")]
        public readonly VariableHandle<bool> CarryRiders = true;

        [SchematicDoc(@"{""name"": ""RiderZoneSize"", ""kind"": ""variable"", ""behavior"": ""Moving Platform"", ""type"": ""Vector3"", ""default"": ""(3, 0.3, 3)"", ""description"": ""The platform's size (local, centred on the object): riders are looked for on top of this box.""}")]
        [Tooltip("The platform's size (local, centred on the object): riders are looked for on top of this box.")]
        public readonly VariableHandle<Vector3> RiderZoneSize = new Vector3(3f, 0.3f, 3f);

        [SchematicDoc(@"{""name"": ""RiderZoneHeight"", ""kind"": ""variable"", ""behavior"": ""Moving Platform"", ""type"": ""float"", ""default"": ""0.8"", ""description"": ""How far above the top a body still counts as riding (hover characters float a little above).""}")]
        [Tooltip("How far above the top a body still counts as riding (hover characters float a little above).")]
        public readonly VariableHandle<float> RiderZoneHeight = 0.8f;

        [SchematicDoc(@"{""name"": ""Travel"", ""kind"": ""variable"", ""behavior"": ""Moving Platform"", ""type"": ""float"", ""default"": ""0"", ""description"": ""Distance along the path from the first waypoint. Network synced (every peer moves the platform from it).""}")]
        [Tooltip("Distance along the path from the first waypoint.")]
        [NetworkSync]
        public readonly VariableHandle<float> Travel = 0f;

        [SchematicDoc(@"{""name"": ""IsMoving"", ""kind"": ""variable"", ""behavior"": ""Moving Platform"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Whether it's travelling (or waiting at a waypoint on the way). Network synced.""}")]
        [Tooltip("Whether it's travelling (or waiting at a waypoint on the way).")]
        [NetworkSync]
        public readonly VariableHandle<bool> IsMoving = false;

        [SchematicDoc(@"{""name"": ""Direction"", ""kind"": ""variable"", ""behavior"": ""Moving Platform"", ""type"": ""int"", ""default"": ""1"", ""description"": ""1 forwards along the waypoints, -1 backwards. Network synced.""}")]
        [Tooltip("1 forwards along the waypoints, -1 backwards.")]
        [NetworkSync]
        public readonly VariableHandle<int> Direction = 1;

        private readonly VariableHandle<float> _waitUntil = 0f;

        #endregion

        #region Signals

        [SchematicDoc(@"{""name"": ""StartMoving"", ""kind"": ""signal"", ""behavior"": ""Moving Platform"", ""direction"": ""input"", ""description"": ""Starts (or resumes) travelling. A finished Once platform heads back."", ""how"": ""Send it with a Send a Signal node, e.g. from a Switch's On Turned On.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle StartMoving;

        [SchematicDoc(@"{""name"": ""StopMoving"", ""kind"": ""signal"", ""behavior"": ""Moving Platform"", ""direction"": ""input"", ""description"": ""Stops where it is."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle StopMoving;

        [SchematicDoc(@"{""name"": ""Reverse"", ""kind"": ""signal"", ""behavior"": ""Moving Platform"", ""direction"": ""input"", ""description"": ""Turns round."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle Reverse;

        [SchematicDoc(@"{""name"": ""OnArrived"", ""kind"": ""signal"", ""behavior"": ""Moving Platform"", ""direction"": ""output"", ""params"": [{""name"": ""Waypoint"", ""type"": ""int""}], ""description"": ""Sent when it reaches a waypoint, with its index."", ""how"": ""React to it with On Signal Received (open lift doors, play a clunk).""}")]
        [OutputSignal, NetworkSync]
        [Parameter("Waypoint", typeof(int))]
        public readonly SignalHandle<int> OnArrived;

        #endregion

        private static readonly Dictionary<BehaviorInstance, Pose> _origin = new();
        private readonly List<GameObject> _riders = new();
        private readonly List<Vector3> _points = new();
        private readonly List<float> _distances = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _origin.Clear();

        protected override void OnStart()
        {
            var instance = Self;
            _origin[instance] = new Pose(instance.Transform.position, instance.Transform.rotation);
            if (LevelKitQuery.HasAuthority(instance))
                IsMoving.Value = AutoStart.Value;

            StartMoving.Subscribe(() =>
            {
                if (!LevelKitQuery.HasAuthority(instance)) return;
                if (Mode.Value == PlatformLoop.Once && !IsMoving.Value)
                {
                    BuildPath(instance, false);
                    float length = _distances[_distances.Count - 1];
                    if (Travel.Value >= length - 0.001f) Direction.Value = -1;
                    else if (Travel.Value <= 0.001f) Direction.Value = 1;
                }
                IsMoving.Value = true;
            });
            StopMoving.Subscribe(() => { if (LevelKitQuery.HasAuthority(instance)) IsMoving.Value = false; });
            Reverse.Subscribe(() => { if (LevelKitQuery.HasAuthority(instance)) Direction.Value = Direction.Value >= 0 ? -1 : 1; });
        }

        protected override void OnDisable()
        {
            // Back to the start, so a pooled platform respawns where it was placed.
            var instance = Self;
            if (_origin.TryGetValue(instance, out var origin))
                instance.Transform.SetPositionAndRotation(origin.position, origin.rotation);
        }

        protected override void OnDestroy() => _origin.Remove(Self);

        protected override void OnFixedUpdate()
        {
            var instance = Self;
            if (!_origin.ContainsKey(instance))
                return;

            bool loop = Mode.Value == PlatformLoop.Loop;
            BuildPath(instance, loop);
            if (_points.Count < 2)
                return;

            if (IsMoving.Value && Time.time >= _waitUntil.Value)
                Advance(instance, loop, Time.fixedDeltaTime);

            MoveTo(instance, PointAt(Travel.Value));
        }

        // Moves Travel along the path, stopping at the next waypoint it reaches.
        private void Advance(BehaviorInstance instance, bool loop, float dt)
        {
            float length = _distances[_distances.Count - 1];
            if (length <= 0.0001f) return;
            int direction = Direction.Value >= 0 ? 1 : -1;
            float from = Travel.Value;
            float to = from + direction * Mathf.Max(0f, Speed.Value) * dt;

            // The next waypoint in the direction of travel.
            int stop = -1;
            if (direction > 0)
            {
                for (int i = 0; i < _distances.Count; i++)
                    if (_distances[i] > from + 0.0001f) { stop = i; break; }
            }
            else
            {
                for (int i = _distances.Count - 1; i >= 0; i--)
                    if (_distances[i] < from - 0.0001f) { stop = i; break; }
            }

            if (stop < 0)
            {
                // Already at an end (Once, or a direction change landed exactly on one).
                Travel.Value = Mathf.Clamp(from, 0f, length);
                if (loop && direction > 0) Travel.Value = 0f;
                return;
            }

            bool reached = direction > 0 ? to >= _distances[stop] : to <= _distances[stop];
            if (!reached)
            {
                Travel.Value = to;
                return;
            }

            Travel.Value = _distances[stop];
            _waitUntil.Value = Time.time + WaitTime.Value;
            int waypointCount = _points.Count - (loop ? 1 : 0);
            int waypoint = stop % Mathf.Max(1, waypointCount);
            bool authority = LevelKitQuery.HasAuthority(instance);
            if (authority)
                OnArrived.Invoke(waypoint);

            // At an end?
            bool atEnd = direction > 0 ? stop == _distances.Count - 1 : stop == 0;
            if (!atEnd || !authority)
                return;
            switch (Mode.Value)
            {
                case PlatformLoop.Loop:
                    Travel.Value = 0f; // the closing point is the first waypoint again
                    break;
                case PlatformLoop.PingPong:
                    Direction.Value = -direction;
                    break;
                case PlatformLoop.Once:
                    IsMoving.Value = false;
                    break;
            }
        }

        private void MoveTo(BehaviorInstance instance, Vector3 target)
        {
            var transform = instance.Transform;
            var delta = target - transform.position;
            if (delta.sqrMagnitude < 1e-10f)
                return;

            if (CarryRiders.Value)
                CarryRidersBy(instance, delta);

            var body = instance.GetComponent<Box3DBody>();
            if (body != null && !body.IsKinematic() && body.IsLive())
                body.Teleport(target);   // a dynamic platform would fall; teleporting keeps it on its path
            else
                transform.position = target; // kinematic bodies (and plain objects) follow their Transform
        }

        private void CarryRidersBy(BehaviorInstance instance, Vector3 delta)
        {
            var transform = instance.Transform;
            var size = Vector3.Scale(RiderZoneSize.Value, transform.lossyScale);
            float height = Mathf.Max(0.05f, RiderZoneHeight.Value);
            var center = transform.position + transform.rotation * new Vector3(0f, size.y * 0.5f + height * 0.5f, 0f);
            var half = new Vector3(size.x * 0.5f, height * 0.5f, size.z * 0.5f);

            LevelKitQuery.OverlapBox(center, transform.rotation, half, -1, _riders, transform);
            foreach (var rider in _riders)
            {
                var body = rider.GetComponent<Box3DBody>();
                if (body == null || body.IsKinematic() || body.IsNetworkProxy() || body.BodyType == Box3DBodyType.Static)
                    continue;
                body.Teleport(body.transform.position + delta);
            }
        }

        // World positions of the waypoints (plus the first again at the end for Loop) and the distance to each.
        private void BuildPath(BehaviorInstance instance, bool loop)
        {
            _points.Clear();
            _distances.Clear();
            var origin = _origin.TryGetValue(instance, out var pose) ? pose : new Pose(instance.Transform.position, instance.Transform.rotation);
            var waypoints = Waypoints[instance];
            if (waypoints == null || waypoints.Length == 0)
            {
                _points.Add(origin.position);
                _distances.Add(0f);
                return;
            }
            foreach (var offset in waypoints)
                _points.Add(origin.position + origin.rotation * offset);
            if (loop && waypoints.Length > 1)
                _points.Add(_points[0]);

            float total = 0f;
            _distances.Add(0f);
            for (int i = 1; i < _points.Count; i++)
            {
                total += Vector3.Distance(_points[i - 1], _points[i]);
                _distances.Add(total);
            }
        }

        private Vector3 PointAt(float travel)
        {
            if (_points.Count == 1) return _points[0];
            travel = Mathf.Clamp(travel, 0f, _distances[_distances.Count - 1]);
            for (int i = 1; i < _points.Count; i++)
            {
                if (travel <= _distances[i])
                {
                    float segment = _distances[i] - _distances[i - 1];
                    float t = segment > 0f ? (travel - _distances[i - 1]) / segment : 0f;
                    return Vector3.Lerp(_points[i - 1], _points[i], t);
                }
            }
            return _points[_points.Count - 1];
        }

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            var waypoints = context.Get(Waypoints);
            if (waypoints == null || waypoints.Length == 0) return;

            Vector3 originPosition = context.Transform.position;
            Quaternion originRotation = context.Transform.rotation;
            if (context.Instance != null && _origin.TryGetValue(context.Instance, out var origin))
            {
                originPosition = origin.position;
                originRotation = origin.rotation;
            }

            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
            var size = Vector3.Scale(context.Get(RiderZoneSize), context.Transform.lossyScale);
            Vector3 previous = default;
            for (int i = 0; i < waypoints.Length; i++)
            {
                var point = originPosition + originRotation * waypoints[i];
                var matrix = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(point, originRotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, size);
                Gizmos.matrix = matrix;
                if (i > 0) Gizmos.DrawLine(previous, point);
                previous = point;
            }
            if (context.Get(Mode) == PlatformLoop.Loop && waypoints.Length > 1)
                Gizmos.DrawLine(previous, originPosition + originRotation * waypoints[0]);
        }
    }
}
