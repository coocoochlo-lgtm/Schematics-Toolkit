using System.Collections.Generic;
using UnityEngine;

namespace Remedy.LevelStreaming
{
    /// <summary>
    /// A piece of a level: everything under this GameObject. Chunks are shown and hidden at runtime by
    /// <see cref="LevelStreamingZone"/>s (no scene loading, so no loading stutter), spread over several frames within a
    /// time budget. In the editor, double-click a chunk's volume to edit it on its own with the rest of the level
    /// greyed out.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Level Streaming/Level Chunk")]
    public class LevelChunk : MonoBehaviour
    {
        public enum HideMode
        {
            /// <summary>Deactivates the chunk's children: nothing in it runs, renders or collides.</summary>
            Deactivate,
            /// <summary>Only turns rendering off: scripts and physics keep running (cheaper to show again).</summary>
            RenderersOnly,
        }

        [Tooltip("The chunk's volume, relative to this object. Shown in the Scene view; double-click it to edit the chunk.")]
        public Vector3 Center = Vector3.zero;
        public Vector3 Size = new Vector3(50f, 20f, 50f);

        [Tooltip("Shown when the game starts, before any zone asks for it.")]
        public bool VisibleAtStart = false;
        public HideMode Hiding = HideMode.Deactivate;

        [Tooltip("Scene-view colour of the volume.")]
        public Color Color = new Color(0.3f, 0.8f, 1f, 1f);

        /// <summary>Whether the chunk is shown (or on its way to being shown).</summary>
        public bool Visible { get; internal set; } = true;

        /// <summary>The volume in world space (axis-aligned around the rotated box).</summary>
        public Bounds WorldBounds
        {
            get
            {
                var matrix = transform.localToWorldMatrix;
                var bounds = new Bounds(matrix.MultiplyPoint3x4(Center), Vector3.zero);
                Vector3 half = Size * 0.5f;
                for (int i = 0; i < 8; i++)
                {
                    var corner = Center + new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                    bounds.Encapsulate(matrix.MultiplyPoint3x4(corner));
                }
                return bounds;
            }
        }

        /// <summary>Whether a world position is inside the (rotated) volume.</summary>
        public bool Contains(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition) - Center;
            Vector3 half = Size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        private List<Renderer> _renderers;

        /// <summary>The chunk's renderers (gathered once).</summary>
        internal List<Renderer> Renderers
        {
            get
            {
                if (_renderers == null)
                {
                    _renderers = new List<Renderer>();
                    GetComponentsInChildren(true, _renderers);
                }
                return _renderers;
            }
        }

        private void Start()
        {
            // Hidden at once at startup (no point spreading it out while loading).
            if (!VisibleAtStart)
                LevelStreamer.SetVisible(this, false, immediate: true);
        }

        /// <summary>Shows the chunk (spread over frames). Zones call this; it's also fine to call directly.</summary>
        public void Show() => LevelStreamer.Request(this, true);

        /// <summary>Hides the chunk (spread over frames), unless a zone still wants it.</summary>
        public void Hide() => LevelStreamer.Request(this, false);
    }
}
