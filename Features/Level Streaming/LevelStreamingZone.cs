using System.Collections.Generic;
using UnityEngine;

namespace Remedy.LevelStreaming
{
    /// <summary>
    /// A box in the world that shows a set of chunks while a streaming target (the player, the camera) is inside it,
    /// and lets them hide again a moment after it leaves. Zones are checked centrally a few times a second; they don't
    /// use physics triggers.
    /// </summary>
    [AddComponentMenu("Level Streaming/Level Streaming Zone")]
    public class LevelStreamingZone : MonoBehaviour
    {
        public Vector3 Center = Vector3.zero;
        public Vector3 Size = new Vector3(60f, 30f, 60f);

        [Tooltip("Chunks shown while a target is inside.")]
        public List<LevelChunk> Chunks = new List<LevelChunk>();

        [Tooltip("Seconds to wait after the last target leaves before letting the chunks hide (avoids flicker at edges).")]
        public float ExitDelay = 1f;

        internal bool Occupied;
        internal float LeftAt = -1f;

        private void OnEnable() => LevelStreamer.Register(this);
        private void OnDisable() => LevelStreamer.Unregister(this);

        public bool Contains(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition) - Center;
            Vector3 half = Size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Occupied ? new Color(0.3f, 1f, 0.4f, 0.9f) : new Color(1f, 0.85f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(Center, Size);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.5f);
            foreach (var chunk in Chunks)
                if (chunk != null)
                    Gizmos.DrawLine(transform.TransformPoint(Center), chunk.WorldBounds.center);
        }
    }
}
