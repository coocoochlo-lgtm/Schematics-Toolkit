using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// A big surface that throws sound back (a cliff, a canyon wall, the side of a building), found while baking a
    /// <see cref="SoundField"/>: where echoes in the open come from. In the field's space.
    /// </summary>
    [Serializable]
    public struct SoundFieldAnchor
    {
        public Vector3 Position;
        public Vector3 Normal;
        /// <summary>How much of it was seen while baking (roughly its size, as the field sees it).</summary>
        public float Area;
        /// <summary>How much sound it throws back, 0 to 1 (1 - its materials' Absorption).</summary>
        public float Reflectivity;
        /// <summary>Its materials' Hardness, 0 to 1 (bright, sharp echoes when high).</summary>
        public float Hardness;
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
        public List<SoundFieldAnchor> Anchors = new();

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

        /// <summary>Seconds for the reverb to die away (RT60), 0.1 to 8.</summary>
        public float ReverbTime => Mathf.Clamp(0.04f * Mathf.Max(0.5f, Size) / EffectiveAbsorption, 0.1f, 8f);

        /// <summary>How much reverb is heard, 0 to 1: none in the open, more the more closed in (less with soft surfaces).</summary>
        public float Wet => Mathf.Clamp01((Enclosure - 0.45f) / 0.5f) * (1f - 0.6f * Absorption) * 0.65f;

        /// <summary>How soon the high end dies away, 0 (bright: hard, bare surfaces) to 1 (dull: soft, absorbent ones).</summary>
        public float Damping => Mathf.Clamp01(0.15f + 0.6f * (1f - Hardness) + 0.3f * Absorption - 0.15f * Wetness);

        /// <summary>The Reverb node's comb feedback for <see cref="ReverbTime"/> (its combs average about 31 ms).</summary>
        public float Feedback => Mathf.Clamp(Mathf.Pow(10f, -3f * 0.0308f / ReverbTime), 0f, 0.985f);

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

        /// <summary>How much each slapback comes round again (between parallel walls: flutter), 0 to 0.85.</summary>
        public float SlapbackFeedback => Mathf.Clamp(Enclosure * (1f - Absorption) * Hardness * 0.85f, 0f, 0.85f);
    }
}
