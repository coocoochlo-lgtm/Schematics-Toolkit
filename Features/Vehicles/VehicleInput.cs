using System;
using UnityEngine;

namespace Remedy.Vehicles
{
    /// <summary>What a driver input Signal does to a vehicle.</summary>
    public enum VehicleInputAction
    {
        /// <summary>Go (0..1).</summary>
        Throttle,
        /// <summary>Brake, then reverse once stopped (0..1).</summary>
        Brake,
        /// <summary>Left (-1) to right (1).</summary>
        Steer,
        /// <summary>One axis for both: positive throttles, negative brakes/reverses (a trigger pair or W/S).</summary>
        ThrottleAndBrake,
        /// <summary>A Vector2 stick: x steers, y throttles (+) or brakes (-).</summary>
        Drive,
        /// <summary>Handbrake / drift button (held).</summary>
        Handbrake,
        /// <summary>Fire a boost (pressed).</summary>
        Boost,
        /// <summary>Turn a flipped vehicle back over (pressed).</summary>
        Unflip,
    }

    /// <summary>
    /// A driver input: a Signal (usually an Input System Signal from the Input Maps) and what it does. Like the Weapon
    /// Holder's Weapon Inputs: the vehicle listens to it itself, but only while the player is the one driving, so
    /// parked cars and AI cars don't react to the player's keys.
    /// </summary>
    [Serializable]
    public class VehicleInputBinding
    {
        [Tooltip("What this input does to the vehicle.")]
        public VehicleInputAction Action;
        [Tooltip("The Signal that carries the input (bool, float or Vector2), e.g. an Input Map's \"Input Accelerate\".")]
        public SignalData Signal;
        [Tooltip("Flip the value (e.g. an axis that's backwards).")]
        public bool Invert;
    }

    /// <summary>
    /// The small contract every vehicle type shares, so a boat, hovercraft or plane can reuse seats and input the same
    /// way <see cref="ArcadeCar"/> does: read input values loosely (any Signal type), and only take player input while
    /// the player drives.
    /// </summary>
    public static class VehicleInput
    {
        /// <summary>
        /// True if the vehicle should take the local player's input right now: with <paramref name="playerDriverOnly"/>,
        /// only while its driver is tagged as the player (GlobalSubsystem.PlayerTag); otherwise while anyone drives.
        /// A vehicle without Vehicle Seats always takes it.
        /// </summary>
        public static bool AcceptsPlayerInput(GameObject vehicle, bool playerDriverOnly)
        {
            if (!VehicleSeats.HasSeats(vehicle))
                return true;
            var driver = VehicleSeats.DriverOf(vehicle);
            if (driver == null)
                return false;
            if (!playerDriverOnly)
                return true;
            string tag = GlobalSubsystem.PlayerTag;
            return string.IsNullOrEmpty(tag) || driver.CompareTag(tag);
        }

        /// <summary>Any Signal value as an axis: bools are 0/1, Vector2s give their y.</summary>
        public static float ToFloat(object value) => value switch
        {
            null => 0f,
            bool b => b ? 1f : 0f,
            float f => f,
            int i => i,
            double d => (float)d,
            Vector2 v2 => v2.y,
            Vector3 v3 => v3.z,
            _ => 0f,
        };

        /// <summary>Any Signal value as a button: pressed past half-way.</summary>
        public static bool ToBool(object value) => value switch
        {
            null => false,
            bool b => b,
            Vector2 v2 => v2.sqrMagnitude > 0.25f,
            _ => ToFloat(value) > 0.5f,
        };

        /// <summary>Any Signal value as a stick: a single axis becomes (0, value).</summary>
        public static Vector2 ToVector2(object value) => value switch
        {
            Vector2 v2 => v2,
            Vector3 v3 => new Vector2(v3.x, v3.z),
            _ => new Vector2(0f, ToFloat(value)),
        };
    }
}
