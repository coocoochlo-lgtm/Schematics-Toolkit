using System;
using UnityEngine;

namespace Remedy.Vehicles
{
    /// <summary>
    /// One wheel of an <see cref="ArcadeCar"/>. Wheels are rays, not colliders: each casts down from its anchor every
    /// physics step and pushes the car with a spring, grip and engine force at that point. That's what makes the car
    /// easy to tune and drift, while the body itself (a Box3D shape) still crashes, tumbles and flips for real.
    /// </summary>
    [Serializable]
    public class Wheel
    {
        [Tooltip("For you: shown in gizmos and the IO Dock (e.g. \"Front Left\").")]
        public string Name = "Wheel";

        [Tooltip("The top of the suspension: the ray starts here and goes down (along the car's down). Put it inside the body, above where the wheel hub sits at rest.")]
        public Transform Anchor;

        [Tooltip("The wheel's mesh. Optional: moved up and down with the suspension, turned with the steering and spun with the speed. Make it a child of the anchor or the car (not the anchor itself).")]
        public Transform Visual;

        [Tooltip("Wheel radius in metres: how far below the suspension the tyre touches the ground.")]
        public float Radius = 0.35f;

        [Tooltip("Turns with the steering (normally the front wheels).")]
        public bool Steers;

        [Tooltip("The engine pushes through this wheel (rear = RWD, front = FWD, all = 4WD).")]
        public bool Drives = true;

        [Tooltip("The handbrake locks this wheel (normally the rear ones). Handbrake wheels lose grip, which is how a handbrake starts a slide.")]
        public bool HasHandbrake;

        public Wheel() { }

        public Wheel(string name, Transform anchor, bool steers, bool drives, bool handbrake)
        {
            Name = name;
            Anchor = anchor;
            Steers = steers;
            Drives = drives;
            HasHandbrake = handbrake;
        }
    }
}
