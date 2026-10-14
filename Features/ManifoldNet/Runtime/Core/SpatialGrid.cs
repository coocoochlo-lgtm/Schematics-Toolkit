using System;

namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// The optional spatial grid: each value space's points bucketed into G^D cells at least 3σ wide, so a head only
    /// checks the 3^D cells around it instead of every point. Soft sampling skips anything beyond 3σ anyway, so the
    /// result is the same set of points (in cell order). Built once from the fixed point positions; the GPU gets the
    /// same tables. Only used for D ≤ 3 and G ≥ 3 (see <see cref="MnnSettings.UsesSpatialGrid"/>).
    /// </summary>
    internal sealed class SpatialGrid
    {
        public readonly int Size, Dimensions, CellCount, StartStride;
        public readonly float CellSize;
        /// <summary>Per head: where each cell's points start in <see cref="Order"/> (CellCount + 1 entries).</summary>
        public readonly int[] Start;
        /// <summary>Per head: point indices sorted by cell (stable, so ties keep point order).</summary>
        public readonly int[] Order;
        /// <summary>The cells around the last <see cref="Candidates"/> query.</summary>
        public readonly int[] CellList;

        public SpatialGrid(MnnSettings s, float[] points)
        {
            Size = s.SpatialGridSize;
            Dimensions = s.spaceDimensions;
            CellSize = 2f * MnnSettings.SpaceRadius / Size;
            CellCount = 1;
            for (int d = 0; d < Dimensions; d++) CellCount *= Size;
            StartStride = CellCount + 1;
            int h = s.headCount, p = s.pointsPerSpace;
            Start = new int[h * StartStride];
            Order = new int[h * p];
            int around = 1;
            for (int d = 0; d < Dimensions; d++) around *= 3;
            CellList = new int[around];

            var cellOf = new int[p];
            for (int head = 0; head < h; head++)
            {
                int startAt = head * StartStride;
                for (int j = 0; j < p; j++)
                {
                    int cell = 0, scale = 1;
                    for (int d = 0; d < Dimensions; d++)
                    {
                        cell += Axis(points[(head * p + j) * Dimensions + d]) * scale;
                        scale *= Size;
                    }
                    cellOf[j] = cell;
                    Start[startAt + cell + 1]++;
                }
                for (int c = 0; c < CellCount; c++)
                    Start[startAt + c + 1] += Start[startAt + c];
                var fill = new int[CellCount];
                for (int j = 0; j < p; j++)
                {
                    int cell = cellOf[j];
                    Order[head * p + Start[startAt + cell] + fill[cell]++] = j;
                }
            }
        }

        /// <summary>The cell index along one axis of a coordinate in [-R, R).</summary>
        public int Axis(float x) => Math.Clamp((int)MathF.Floor((x + MnnSettings.SpaceRadius) / CellSize), 0, Size - 1);

        /// <summary>Fills <see cref="CellList"/> with the 3^D cells around a position (wrapping), in a fixed order; returns how many.</summary>
        public int Candidates(float[] positions, int at)
        {
            int count = CellList.Length;
            for (int k = 0; k < count; k++)
            {
                int cell = 0, scale = 1, digits = k;
                for (int d = 0; d < Dimensions; d++)
                {
                    int offset = digits % 3 - 1;
                    digits /= 3;
                    int axis = (Axis(positions[at + d]) + offset + Size) % Size;
                    cell += axis * scale;
                    scale *= Size;
                }
                CellList[k] = cell;
            }
            return count;
        }
    }
}
