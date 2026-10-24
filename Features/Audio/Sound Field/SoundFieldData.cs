using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// A wall that throws sound back (a room's wall, floor or ceiling, a cliff, the side of a building), found while
    /// baking a <see cref="SoundField"/>: the cell sides that met the same flat surface, joined into one rectangle (long
    /// surfaces in tiles). In the field's space.
    /// </summary>
    [Serializable]
    public struct SoundFieldAnchor
    {
        /// <summary>Its middle.</summary>
        public Vector3 Position;
        /// <summary>The way it faces (toward the space it throws sound back into).</summary>
        public Vector3 Normal;
        /// <summary>
        /// One of its sides' directions, along the surface; the other is Normal x Tangent. Zero in older bakes (a point:
        /// echoes come from Position).
        /// </summary>
        public Vector3 Tangent;
        /// <summary>Half its size along Tangent and along the other side, in meters.</summary>
        public Vector2 HalfSize;
        /// <summary>How much of it was seen while baking (roughly its size, as the field sees it).</summary>
        public float Area;
        /// <summary>How much sound it throws back, 0 to 1 (1 - its materials' Absorption).</summary>
        public float Reflectivity;
        /// <summary>Its materials' Hardness, 0 to 1 (bright, sharp echoes when high).</summary>
        public float Hardness;
    }

    /// <summary>
    /// An opening between spaces (a doorway, a window, a gap between obstacles), found while baking a
    /// <see cref="SoundField"/>: where sound (and what hears it) gets from one side of a wall to the other. In the
    /// field's space.
    /// </summary>
    [Serializable]
    public struct SoundFieldDoorway
    {
        /// <summary>Its middle.</summary>
        public Vector3 Position;
        /// <summary>The way through it (either way round).</summary>
        public Vector3 Normal;
        /// <summary>Roughly how wide it is, in meters.</summary>
        public float Width;
        /// <summary>The steps between cells it's made of (cell * 3 + axis: from that cell along +axis).</summary>
        public int[] Links;
    }

    /// <summary>
    /// What a <see cref="SoundField"/>'s cells sound like, as baked: one cell every <see cref="CellSize"/> meters
    /// through its box, each knowing whether it's open air or inside something, how big the space around it is, how
    /// enclosed, what the surfaces around it are made of (their Surface Materials), its nearest wall, and the echo
    /// anchors it can hear. Raw measurements: how they turn into reverb is worked out when it's used
    /// (<see cref="SoundAcoustics"/>), so tuning that needs no new bake.
    /// </summary>
    public class SoundFieldData : ScriptableObject
    {
        /// <summary>Bytes per cell in <see cref="Packed"/>.</summary>
        public const int Stride = 12;
        /// <summary>No anchor, in a cell's anchor slots.</summary>
        public const ushort NoAnchor = ushort.MaxValue;

        // Cell bytes: 0 flags, 1 size (half meters), 2 enclosure, 3 absorption, 4 hardness, 5 wetness,
        // 6 nearest wall's direction (index into Directions), 7 its distance (quarter meters), 8-9 and 10-11 anchors.
        public const byte Open = 1, Solid = 2;

        [Tooltip("Cells along each axis.")] public Vector3Int Cells;
        [Tooltip("Meters between cells.")] public float CellSize = 2f;
        [Tooltip("The box baked, in the field's space.")] public Bounds Box;
        [Tooltip("Rays cast from each cell.")] public int Rays;
        [Tooltip("When it was baked.")] public string BakedAt;
        [HideInInspector] public byte[] Packed = Array.Empty<byte>();
        /// <summary>
        /// Per cell: whether sound passes straight to the next cell along +X (bit 0), +Y (bit 1) and +Z (bit 2): both
        /// open, nothing between their middles. The ways sound can get round corners (empty in older bakes).
        /// </summary>
        [HideInInspector] public byte[] Links = Array.Empty<byte>();
        public List<SoundFieldAnchor> Anchors = new();
        /// <summary>The openings between spaces (empty in older bakes).</summary>
        public List<SoundFieldDoorway> Doorways = new();
        /// <summary>
        /// Per cell, the walls it hears (up to <see cref="WallsPerCell"/>: the best one each way, nearest and biggest
        /// first; <see cref="NoAnchor"/> for none). Empty in older bakes, which have only the cell's two anchors.
        /// </summary>
        [HideInInspector] public ushort[] CellWalls = Array.Empty<ushort>();
        /// <summary>Walls kept per cell: one for each way a wall can face (four sides, floor, ceiling).</summary>
        public const int WallsPerCell = 6;

        /// <summary>Whether the bake has walls per cell (older ones: two anchors per cell).</summary>
        public bool HasCellWalls => CellWalls != null && CellWalls.Length == Count * WallsPerCell && Count > 0;

        /// <summary>
        /// The walls a cell hears, into <paramref name="into"/> (at least <see cref="WallsPerCell"/> long); how many.
        /// Older bakes: its two anchors.
        /// </summary>
        public int WallsOf(int index, ushort[] into)
        {
            int found = 0;
            if (HasCellWalls)
            {
                for (int w = 0; w < WallsPerCell; w++)
                {
                    ushort wall = CellWalls[index * WallsPerCell + w];
                    if (wall != NoAnchor && wall < Anchors.Count)
                        into[found++] = wall;
                }
                return found;
            }
            if (TryGet(index, out var cell))
            {
                if (cell.AnchorA != NoAnchor && cell.AnchorA < Anchors.Count) into[found++] = cell.AnchorA;
                if (cell.AnchorB != NoAnchor && cell.AnchorB < Anchors.Count) into[found++] = cell.AnchorB;
            }
            return found;
        }

        /// <summary>
        /// Where sound from <paramref name="from"/> bounces off a wall to reach <paramref name="to"/> (both in the field's
        /// space): the mirror reflection point, kept within the wall. False when either is behind it (no bounce). For
        /// older bakes' point anchors: the anchor itself. <paramref name="outside"/>: how far past the wall's edge the
        /// true reflection point is (0 when on it).
        /// </summary>
        public static bool Reflection(in SoundFieldAnchor wall, Vector3 from, Vector3 to, out Vector3 point, out float outside)
        {
            point = wall.Position;
            outside = 0f;
            var normal = wall.Normal;
            float fromSide = Vector3.Dot(from - wall.Position, normal), toSide = Vector3.Dot(to - wall.Position, normal);
            if (fromSide <= 0.01f || toSide <= 0.01f)
                return false;
            if (wall.Tangent.sqrMagnitude < 0.5f)
                return true;
            // The listener's mirror image behind the wall; the straight line to it crosses the wall where sound bounces.
            var mirror = to - 2f * toSide * normal;
            float t = fromSide / (fromSide - Vector3.Dot(mirror - wall.Position, normal));
            var hit = from + (mirror - from) * t;
            var u = wall.Tangent;
            var v = Vector3.Cross(normal, u);
            var offset = hit - wall.Position;
            float du = Vector3.Dot(offset, u), dv = Vector3.Dot(offset, v);
            float cu = Mathf.Clamp(du, -wall.HalfSize.x, wall.HalfSize.x), cv = Mathf.Clamp(dv, -wall.HalfSize.y, wall.HalfSize.y);
            outside = Mathf.Sqrt((du - cu) * (du - cu) + (dv - cv) * (dv - cv));
            point = wall.Position + u * cu + v * cv;
            return true;
        }

        /// <summary>Whether the bake has links (sound around corners); older bakes don't until baked again.</summary>
        public bool HasLinks => Links != null && Links.Length == Count && Count > 0;

        /// <summary>Whether sound passes from a cell to the next one along +<paramref name="axis"/> (0 X, 1 Y, 2 Z).</summary>
        public bool Linked(int index, int axis) => (Links[index] & (1 << axis)) != 0;

        public int Count => Cells.x * Cells.y * Cells.z;

        public int Index(int x, int y, int z) => x + Cells.x * (y + Cells.y * z);

        /// <summary>The middle of a cell, in the field's space.</summary>
        public Vector3 CellCenter(int x, int y, int z) => Box.min + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * CellSize;

        public bool IsOpen(int index) => (Packed[index * Stride] & Open) != 0;
        public bool IsSolid(int index) => (Packed[index * Stride] & Solid) != 0;

        /// <summary>A cell's measurements (false for solid or unbaked cells).</summary>
        public bool TryGet(int index, out SoundCell cell)
        {
            cell = default;
            if (index < 0 || index >= Count || Packed == null || Packed.Length < (index + 1) * Stride || !IsOpen(index))
                return false;
            int o = index * Stride;
            cell.Size = Packed[o + 1] * 0.5f;
            cell.Enclosure = Packed[o + 2] / 255f;
            cell.Absorption = Packed[o + 3] / 255f;
            cell.Hardness = Packed[o + 4] / 255f;
            cell.Wetness = Packed[o + 5] / 255f;
            cell.NearestDirection = Packed[o + 6];
            cell.NearestDistance = Packed[o + 7] * 0.25f;
            cell.AnchorA = (ushort)(Packed[o + 8] | (Packed[o + 9] << 8));
            cell.AnchorB = (ushort)(Packed[o + 10] | (Packed[o + 11] << 8));
            return true;
        }

        /// <summary>Writes a cell (the bake).</summary>
        public void Set(int index, bool solid, in SoundCell cell)
        {
            int o = index * Stride;
            Packed[o] = solid ? Solid : Open;
            Packed[o + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(cell.Size * 2f), 0, 255);
            Packed[o + 2] = Byte01(cell.Enclosure);
            Packed[o + 3] = Byte01(cell.Absorption);
            Packed[o + 4] = Byte01(cell.Hardness);
            Packed[o + 5] = Byte01(cell.Wetness);
            Packed[o + 6] = cell.NearestDirection;
            Packed[o + 7] = (byte)Mathf.Clamp(Mathf.RoundToInt(cell.NearestDistance * 4f), 0, 255);
            Packed[o + 8] = (byte)(cell.AnchorA & 0xFF);
            Packed[o + 9] = (byte)(cell.AnchorA >> 8);
            Packed[o + 10] = (byte)(cell.AnchorB & 0xFF);
            Packed[o + 11] = (byte)(cell.AnchorB >> 8);
        }

        private static byte Byte01(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);

        /// <summary>
        /// The directions rays are cast in while baking (evenly spread over a sphere), which a cell's
        /// <see cref="SoundCell.NearestDirection"/> indexes.
        /// </summary>
        public static Vector3[] Directions(int count)
        {
            count = Mathf.Clamp(count, 6, 255);
            var directions = new Vector3[count];
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < count; i++)
            {
                float y = 1f - (i + 0.5f) / count * 2f;
                float radius = Mathf.Sqrt(1f - y * y);
                float angle = golden * i;
                directions[i] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            }
            return directions;
        }
    }

    /// <summary>One cell's measurements, from a bake.</summary>
    public struct SoundCell
    {
        /// <summary>How far away the surfaces around it are, on average (meters): how big the space is.</summary>
        public float Size;
        /// <summary>How closed in it is, 0 (open sky all round) to 1 (walls every way).</summary>
        public float Enclosure;
        /// <summary>The surrounding surfaces' Surface Materials (nearer ones count more), 0 to 1.</summary>
        public float Absorption, Hardness, Wetness;
        /// <summary>The nearest wall: its direction (an index into the bake's directions) and distance (meters).</summary>
        public byte NearestDirection;
        public float NearestDistance;
        /// <summary>The echo anchors it hears best (<see cref="SoundFieldData.NoAnchor"/> for none).</summary>
        public ushort AnchorA, AnchorB;
    }

    /// <summary>
    /// What a place sounds like, blended from a <see cref="SoundField"/>'s cells: the measurements, and the reverb they
    /// make. Reverb Time comes from the space's size and how much its surfaces (and open sky) soak up (Sabine's
    /// formula); the reverb's level from how closed in it is; its darkness from how soft and absorbent the surfaces are.
    /// </summary>
    public struct SoundAcoustics
    {
        public float Size, Enclosure, Absorption, Hardness, Wetness;
        /// <summary>How far the nearest wall is, in meters (slapback echoes come off it).</summary>
        public float NearestWall;

        /// <summary>
        /// What open sky soaks up: everything. A place half open to the sky loses half its sound that way.
        /// </summary>
        public float EffectiveAbsorption => Mathf.Max(0.02f, Enclosure * Mathf.Max(0.02f, Absorption) + (1f - Enclosure));

        /// <summary>Seconds for the reverb to die away (RT60), 0.1 to <see cref="MaxReverbTime"/>.</summary>
        public float ReverbTime => Mathf.Clamp(0.04f * Mathf.Max(0.5f, Size) / EffectiveAbsorption, 0.1f, MaxReverbTime);

        /// <summary>
        /// The longest reverb a place makes, in seconds: past this (a bare, closed space) the reverb stops sounding like
        /// a room and starts ringing like a microphone held to its speaker.
        /// </summary>
        public const float MaxReverbTime = 4f;

        /// <summary>Open air (outside every Sound Field): nothing near but the ground, which the sky outweighs.</summary>
        public static SoundAcoustics OpenAir => new() { Size = 20f, Enclosure = 0.45f, Absorption = 0.4f, Hardness = 0.5f, NearestWall = 1f };

        /// <summary>How much reverb is heard, 0 to 1: none in the open, more the more closed in (less with soft surfaces).</summary>
        public float Wet => Mathf.Clamp01((Enclosure - 0.45f) / 0.5f) * (1f - 0.6f * Absorption) * 0.65f;

        /// <summary>How soon the high end dies away, 0 (bright: hard, bare surfaces) to 1 (dull: soft, absorbent ones).</summary>
        public float Damping => Mathf.Clamp(0.15f + 0.6f * (1f - Hardness) + 0.3f * Absorption - 0.15f * Wetness, 0.3f, 1f); // some highs always fade: no metallic ring

        /// <summary>The Reverb node's comb feedback for <see cref="ReverbTime"/> (its combs average about 31 ms).</summary>
        public float Feedback => Mathf.Clamp(Mathf.Pow(10f, -3f * 0.0308f / ReverbTime), 0f, 0.95f);

        public static SoundAcoustics Lerp(in SoundAcoustics a, in SoundAcoustics b, float t) => new()
        {
            Size = Mathf.Lerp(a.Size, b.Size, t),
            Enclosure = Mathf.Lerp(a.Enclosure, b.Enclosure, t),
            Absorption = Mathf.Lerp(a.Absorption, b.Absorption, t),
            Hardness = Mathf.Lerp(a.Hardness, b.Hardness, t),
            Wetness = Mathf.Lerp(a.Wetness, b.Wetness, t),
            NearestWall = Mathf.Lerp(a.NearestWall, b.NearestWall, t),
        };

        /// <summary>A slapback off the nearest wall: there and back, in seconds.</summary>
        public float SlapbackDelay => Mathf.Clamp(2f * NearestWall / 343f, 0.005f, 1.5f);

        /// <summary>
        /// How much a slapback is heard, 0 to 0.6: none until it comes back 50 ms after the sound (sooner, it's heard as
        /// the sound doubled, and the reverb covers it), fully from 100 ms (walls 8.6 to 17 m away); more off hard,
        /// bare walls.
        /// </summary>
        public float SlapbackWet => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.1f, 2f * NearestWall / 343f)) * (1f - Absorption) * Hardness * 0.6f;

        /// <summary>
        /// How much each slapback comes round again (between parallel walls: flutter), 0 to 0.5: a few repeats that die
        /// away (stronger, and the repeats run together into a ringing tone).
        /// </summary>
        public float SlapbackFeedback => Mathf.Clamp(Enclosure * (1f - Absorption) * Hardness * 0.5f, 0f, 0.5f);
    }
}
