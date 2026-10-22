using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// A box of space that knows what it sounds like everywhere inside: baked (in the editor, from the scene's meshes
    /// and their Surface Materials) into a grid of cells, each with how big and enclosed the space around it is and
    /// what it's made of. Sound Cues read it where they play: a Reverb in Spatial mode rings like the place, and the
    /// Acoustics node gives the numbers. Blended between cells, so walking from a corridor into a hall changes the sound
    /// smoothly. Put one in each level chunk (it streams with it); overlapping fields: the higher Priority, then the
    /// smaller one.
    /// </summary>
    [AddComponentMenu("Remedy/Audio/Sound Field")]
    [ExecuteAlways]
    public class SoundField : MonoBehaviour
    {
        public enum DebugView { Off, ReverbTime, Wet, Size, Enclosure, Absorption, Hardness, Wetness, Solid }

        [Tooltip("The box, in the object's space.")]
        public Vector3 Center;
        public Vector3 Size = new(40f, 16f, 40f);
        [Tooltip("Meters between cells: smaller is finer and slower to bake (2 suits most levels; 1 for tight interiors).")]
        [Min(0.25f)] public float CellSize = 2f;
        [Tooltip("Rays cast from each cell while baking (more: smoother, slower).")]
        [Range(12, 128)] public int BakeRays = 32;
        [Tooltip("How far baking rays look, in meters: past this counts as open sky.")]
        public float BakeReach = 64f;
        [Tooltip("Which layers' meshes are baked.")]
        public LayerMask BakeLayers = ~0;
        [Tooltip("Where overlapping fields disagree, the higher Priority wins.")]
        public int Priority;
        [Tooltip("The baked cells (made by Bake).")]
        public SoundFieldData Data;

        [Header("Debug")]
        [Tooltip("What the Scene view shows on the slice of cells when the field is selected.")]
        public DebugView View = DebugView.ReverbTime;
        [Tooltip("Which layer of cells is shown, 0 (bottom) to 1 (top).")]
        [Range(0f, 1f)] public float Slice = 0.1f;
        [Tooltip("Show the echo anchors (the surfaces that throw sound back).")]
        public bool ShowAnchors = true;
        [Tooltip("Show the doorways and openings found when baking (what AI hear sounds come through).")]
        public bool ShowDoorways = true;

        private static readonly List<SoundField> _fields = new();

        /// <summary>Every enabled field.</summary>
        public static IReadOnlyList<SoundField> All => _fields;

        private void OnEnable()
        {
            if (!_fields.Contains(this))
                _fields.Add(this);
        }

        private void OnDisable() => _fields.Remove(this);

        /// <summary>The box being baked, in the object's space.</summary>
        public Bounds Box => new(Center, Size);

        /// <summary>Whether a world position is inside the baked box.</summary>
        public bool Contains(Vector3 world)
            => Data != null && Data.Box.Contains(transform.InverseTransformPoint(world));

        /// <summary>
        /// What the place at a world position sounds like: the highest-priority (then smallest) baked field around it,
        /// blended between the open cells nearest it. False outside every field (or deep inside a wall).
        /// </summary>
        public static bool Sample(Vector3 world, out SoundAcoustics acoustics)
        {
            acoustics = default;
            SoundField best = null;
            float bestVolume = float.MaxValue;
            foreach (var field in _fields)
            {
                if (field == null || !field.isActiveAndEnabled || !field.Contains(world))
                    continue;
                var size = field.Data.Box.size;
                float volume = size.x * size.y * size.z;
                if (best == null || field.Priority > best.Priority || (field.Priority == best.Priority && volume < bestVolume))
                {
                    best = field;
                    bestVolume = volume;
                }
            }
            return best != null && best.SampleLocal(best.transform.InverseTransformPoint(world), out acoustics);
        }

        /// <summary>
        /// The field (highest priority, then smallest) whose box holds a world position, and the cell it's in (open or
        /// not). False outside every baked field.
        /// </summary>
        public static bool TryGetCell(Vector3 world, out SoundField field, out int cell)
        {
            field = null;
            cell = -1;
            float bestVolume = float.MaxValue;
            foreach (var candidate in _fields)
            {
                if (candidate == null || !candidate.isActiveAndEnabled || !candidate.Contains(world))
                    continue;
                var size = candidate.Data.Box.size;
                float volume = size.x * size.y * size.z;
                if (field == null || candidate.Priority > field.Priority || (candidate.Priority == field.Priority && volume < bestVolume))
                {
                    field = candidate;
                    bestVolume = volume;
                }
            }
            if (field == null)
                return false;
            var data = field.Data;
            var p = (field.transform.InverseTransformPoint(world) - data.Box.min) / data.CellSize;
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, data.Cells.x - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, data.Cells.y - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(p.z), 0, data.Cells.z - 1);
            cell = data.Index(x, y, z);
            return true;
        }

        /// <summary>The blend of the open cells around a point in the field's space (trilinear; walls don't count).</summary>
        public bool SampleLocal(Vector3 local, out SoundAcoustics acoustics)
        {
            acoustics = default;
            var data = Data;
            if (data == null || data.Count == 0)
                return false;
            var cells = data.Cells;
            var p = (local - data.Box.min) / data.CellSize - new Vector3(0.5f, 0.5f, 0.5f);
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
            float total = 0f;
            var sum = new SoundAcoustics();
            for (int corner = 0; corner < 8; corner++)
            {
                int dx = corner & 1, dy = (corner >> 1) & 1, dz = (corner >> 2) & 1;
                int x = Mathf.Clamp(x0 + dx, 0, cells.x - 1), y = Mathf.Clamp(y0 + dy, 0, cells.y - 1), z = Mathf.Clamp(z0 + dz, 0, cells.z - 1);
                if (!data.TryGet(data.Index(x, y, z), out var cell))
                    continue;
                float weight = (dx == 1 ? fx : 1f - fx) * (dy == 1 ? fy : 1f - fy) * (dz == 1 ? fz : 1f - fz);
                if (weight <= 0f)
                    continue;
                total += weight;
                sum.Size += cell.Size * weight;
                sum.Enclosure += cell.Enclosure * weight;
                sum.Absorption += cell.Absorption * weight;
                sum.Hardness += cell.Hardness * weight;
                sum.Wetness += cell.Wetness * weight;
                sum.NearestWall += cell.NearestDistance * weight;
            }
            if (total <= 1e-5f)
                return false;
            acoustics = new SoundAcoustics
            {
                Size = sum.Size / total,
                Enclosure = sum.Enclosure / total,
                Absorption = sum.Absorption / total,
                Hardness = sum.Hardness / total,
                Wetness = sum.Wetness / total,
                NearestWall = sum.NearestWall / total,
            };
            return true;
        }

        #region Debug view

#if UNITY_EDITOR
        private static bool? _showEchoWalls;

        /// <summary>
        /// Draw every baked echo wall in the Scene view, selected or not (the Sound Spaces window's toggle; kept between
        /// sessions): a patch facing the way it throws sound back, glowing with how much it's echoing in Play Mode.
        /// </summary>
        public static bool ShowEchoWalls
        {
            get => _showEchoWalls ??= UnityEditor.EditorPrefs.GetBool("Remedy.Audio.ShowEchoWalls", true);
            set
            {
                _showEchoWalls = value;
                UnityEditor.EditorPrefs.SetBool("Remedy.Audio.ShowEchoWalls", value);
            }
        }
#endif

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.5f);
            Gizmos.DrawWireCube(Center, Size);
#if UNITY_EDITOR
            if (ShowEchoWalls)
                DrawEchoWalls();
#endif
        }

        // Each baked echo wall as a patch about its size, facing out, like the preview's walls: dim while quiet, glowing
        // orange (and its line out growing) with how loud the echo off it is right now. Read from the live echo walls,
        // so it costs nothing extra in the game.
        private void DrawEchoWalls()
        {
            var anchors = Data?.Anchors;
            if (anchors == null || anchors.Count == 0)
                return;
            var spaces = Application.isPlaying ? SoundSpaces.Current : null;
            var toWorld = transform.localToWorldMatrix;
            for (int a = 0; a < anchors.Count; a++)
            {
                var anchor = anchors[a];
                var bus = spaces != null ? spaces.EchoBus(this, a) : null;
                float glow = bus != null ? Mathf.Clamp01(bus.Peak * 4f) : 0f;
                float side = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0f, anchor.Area)), 0.5f, 12f);
                var normal = anchor.Normal.sqrMagnitude > 0.0001f ? anchor.Normal.normalized : Vector3.forward;
                Gizmos.matrix = toWorld * Matrix4x4.TRS(anchor.Position, Quaternion.LookRotation(normal), new Vector3(side, side, 0.02f));
                var idle = bus != null ? new Color(1f, 0.8f, 0.35f, 0.18f) : new Color(0.75f, 0.75f, 0.75f, 0.1f);
                var color = Color.Lerp(idle, new Color(1f, 0.45f, 0.15f, 0.75f), glow);
                Gizmos.color = color;
                Gizmos.DrawCube(Vector3.zero, Vector3.one);
                color.a = Mathf.Min(1f, color.a + 0.35f);
                Gizmos.color = color;
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                Gizmos.matrix = toWorld;
                Gizmos.DrawLine(anchor.Position, anchor.Position + normal * (0.5f + glow * 4f));
            }
            Gizmos.matrix = toWorld;
        }

        private void OnDrawGizmosSelected()
        {
            var data = Data;
            if (data == null || data.Count == 0 || View == DebugView.Off)
                return;
            Gizmos.matrix = transform.localToWorldMatrix;
            int y = Mathf.Clamp(Mathf.RoundToInt(Slice * (data.Cells.y - 1)), 0, data.Cells.y - 1);
            var cube = Vector3.one * (data.CellSize * 0.9f);
            for (int z = 0; z < data.Cells.z; z++)
            for (int x = 0; x < data.Cells.x; x++)
            {
                int index = data.Index(x, y, z);
                Color color;
                if (data.TryGet(index, out var cell))
                {
                    if (View == DebugView.Solid)
                        continue;
                    var acoustics = new SoundAcoustics
                    {
                        Size = cell.Size, Enclosure = cell.Enclosure, Absorption = cell.Absorption,
                        Hardness = cell.Hardness, Wetness = cell.Wetness,
                    };
                    float value = View switch
                    {
                        DebugView.ReverbTime => Mathf.InverseLerp(0.1f, 4f, acoustics.ReverbTime),
                        DebugView.Wet => acoustics.Wet / 0.65f,
                        DebugView.Size => Mathf.InverseLerp(0f, 30f, cell.Size),
                        DebugView.Enclosure => cell.Enclosure,
                        DebugView.Absorption => cell.Absorption,
                        DebugView.Hardness => cell.Hardness,
                        _ => cell.Wetness,
                    };
                    // Low is blue, middle green, high pink.
                    color = value < 0.5f
                        ? Color.Lerp(new Color(0.2f, 0.45f, 1f), new Color(0.3f, 0.95f, 0.5f), value * 2f)
                        : Color.Lerp(new Color(0.3f, 0.95f, 0.5f), new Color(1f, 0.35f, 0.75f), value * 2f - 1f);
                    color.a = 0.35f;
                }
                else if (data.IsSolid(index) && View == DebugView.Solid)
                    color = new Color(0.5f, 0.5f, 0.5f, 0.45f);
                else
                    continue;
                Gizmos.color = color;
                Gizmos.DrawCube(data.CellCenter(x, y, z), cube);
            }

            if (ShowAnchors && data.Anchors != null)
            {
                foreach (var anchor in data.Anchors)
                {
                    Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
                    Gizmos.DrawSphere(anchor.Position, Mathf.Clamp(Mathf.Sqrt(anchor.Area) * 0.15f, 0.4f, 3f));
                    Gizmos.DrawLine(anchor.Position, anchor.Position + anchor.Normal * 4f);
                }
            }

            if (ShowDoorways && data.Doorways != null)
            {
                Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.8f);
                foreach (var doorway in data.Doorways)
                {
                    // A frame across the way through, as wide as the opening.
                    var across = Vector3.Cross(doorway.Normal, Vector3.up).normalized * Mathf.Max(data.CellSize, doorway.Width);
                    Gizmos.DrawWireCube(doorway.Position, new Vector3(Mathf.Abs(across.x) + 0.1f, data.CellSize, Mathf.Abs(across.z) + 0.1f));
                    Gizmos.DrawLine(doorway.Position - doorway.Normal * data.CellSize, doorway.Position + doorway.Normal * data.CellSize);
                }
            }
        }

        #endregion
    }
}
