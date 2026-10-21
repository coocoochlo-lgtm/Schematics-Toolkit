using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Remedy.Audio
{
    /// <summary>
    /// A space sounds share effects in, the way they share a Reverb Zone's reverb: every Sound Cue playing inside the
    /// box (its <see cref="SoundCuePlayer"/>'s position) goes through the room's <see cref="Output"/> mixer group, so
    /// one effect chain (an echo, an EQ, a distortion, a ducking compressor...) runs for all of them together, rather
    /// than one per sound. Sounds moving in or out switch over with a short fade. Its Properties are read by the Room
    /// node in Sound Cues, for anything per-sound that should change with the room.
    /// </summary>
    /// <remarks>
    /// The room's chain is a Unity Audio Mixer group (make one in an Audio Mixer and add effects to it); Sound Cue
    /// nodes run once per sound, so a chain shared by many sounds lives in the mixer. Rooms that overlap: the higher
    /// Priority wins, then the smaller room.
    /// </remarks>
    [AddComponentMenu("Remedy/Audio/Sound Room")]
    public class SoundRoom : MonoBehaviour
    {
        [Serializable]
        public struct Property
        {
            public string Name;
            public float Value;
        }

        [Tooltip("The room's box, in the object's space.")]
        public Vector3 Center;
        public Vector3 Size = new(10f, 4f, 10f);
        [Tooltip("Where overlapping rooms disagree, the higher Priority wins.")]
        public int Priority;
        [Tooltip("The mixer group (and its effects) every sound in the room plays through. None: sounds keep their own.")]
        public AudioMixerGroup Output;
        [Tooltip("Seconds a sound takes to switch over when it moves in or out.")]
        public float FadeTime = 0.1f;
        [Tooltip("Values the Room node reads by name (how echoey, how damp...).")]
        public List<Property> Properties = new();

        private static readonly List<SoundRoom> _rooms = new();

        /// <summary>How many rooms are active.</summary>
        public static int Count => _rooms.Count;

        public static IReadOnlyList<SoundRoom> All => _rooms;

        private void OnEnable() => _rooms.Add(this);
        private void OnDisable() => _rooms.Remove(this);

        /// <summary>Whether a world position is inside the room.</summary>
        public bool Contains(Vector3 position)
        {
            var local = transform.InverseTransformPoint(position) - Center;
            return Mathf.Abs(local.x) <= Size.x * 0.5f && Mathf.Abs(local.y) <= Size.y * 0.5f && Mathf.Abs(local.z) <= Size.z * 0.5f;
        }

        /// <summary>The room's size in meters (the side of a cube as big as it).</summary>
        public float WorldSize
        {
            get
            {
                var scale = transform.lossyScale;
                return Mathf.Pow(Mathf.Abs(Size.x * scale.x * Size.y * scale.y * Size.z * scale.z), 1f / 3f);
            }
        }

        /// <summary>One of the room's Properties (0 when it has none by that name).</summary>
        public float Get(string name)
        {
            foreach (var property in Properties)
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    return property.Value;
            return 0f;
        }

        /// <summary>The room a world position is in (null when none).</summary>
        public static SoundRoom At(Vector3 position)
        {
            SoundRoom best = null;
            float bestSize = float.MaxValue;
            foreach (var room in _rooms)
            {
                if (!room.Contains(position))
                    continue;
                float size = room.WorldSize;
                if (best == null || room.Priority > best.Priority || (room.Priority == best.Priority && size < bestSize))
                {
                    best = room;
                    bestSize = size;
                }
            }
            return best;
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.55f, 0.75f, 1f, 0.6f);
            Gizmos.DrawWireCube(Center, Size);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.55f, 0.75f, 1f, 0.08f);
            Gizmos.DrawCube(Center, Size);
        }
    }
}
