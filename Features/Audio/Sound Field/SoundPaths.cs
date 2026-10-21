using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>How a sound reaches the listener.</summary>
    public enum SoundRoute
    {
        /// <summary>Straight there (or nothing's known about the way: no baked field around both).</summary>
        Clear,
        /// <summary>Blocked straight, but there's a way round: it's heard from the last corner it comes round.</summary>
        Around,
        /// <summary>Blocked, with no way round near enough: only what gets through the walls is heard.</summary>
        Sealed,
    }

    /// <summary>
    /// Sound round corners, in the game: the way a sound reaches the listener when the straight line is blocked, through
    /// a <see cref="SoundField"/>'s cells and the links baked between them (where sound passes from one cell to the
    /// next). From the listener's cell it floods out over the links (again whenever the listener moves to another cell),
    /// so each sound's way is a walk back along the flood. That way is pulled straight round the corners it has to
    /// turn: the last corner before the listener is where the sound is heard from, and the whole length is how far it
    /// travelled.
    /// </summary>
    public static class SoundPaths
    {
        /// <summary>How far a flood reaches from the listener, in cells (sounds farther round than this are sealed off).</summary>
        public const int MaxSteps = 48;

        private sealed class Flood
        {
            public SoundFieldData Data;
            public int From = -1;
            public int Generation;
            public int[] Stamp, Parent;
            public short[] Steps;
        }

        private static readonly Dictionary<SoundField, Flood> _floods = new();
        private static readonly Queue<int> _queue = new();
        private static readonly List<int> _path = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _floods.Clear();

        /// <summary>
        /// How a sound at <paramref name="source"/> reaches the listener. Round a corner: where it's heard from (the
        /// corner), the whole length of the way, and how sharply it bends there (0 straight on, 1 right back).
        /// </summary>
        public static SoundRoute Around(Vector3 source, Vector3 listener, out Vector3 heardFrom, out float length, out float bend)
        {
            heardFrom = source;
            length = Vector3.Distance(source, listener);
            bend = 0f;
            if (!SoundField.TryGetCell(listener, out var field, out int to) || !SoundField.TryGetCell(source, out var sourceField, out int from) || sourceField != field)
                return SoundRoute.Clear;
            var data = field.Data;
            if (!data.HasLinks)
                return SoundRoute.Clear;
            // A sound (or listener) sunk into the floor or a wall is heard from the open cell beside it.
            from = OpenNear(data, from);
            to = OpenNear(data, to);
            if (from < 0 || to < 0 || Visible(data, from, to))
                return SoundRoute.Clear;

            var flood = FloodFrom(field, data, to);
            if (flood.Stamp[from] != flood.Generation)
                return SoundRoute.Sealed;

            // The cells from the sound back to the listener.
            _path.Clear();
            for (int cell = from; cell >= 0 && _path.Count <= MaxSteps + 1; cell = flood.Parent[cell])
                _path.Add(cell);
            int last = _path.Count - 1;

            // The corner: the farthest cell along the way the listener can still see.
            int corner = last;
            while (corner > 0 && Visible(data, _path[corner - 1], to))
                corner--;
            var toWorld = field.transform;
            heardFrom = Center(data, toWorld, _path[corner]);
            length = Vector3.Distance(listener, heardFrom);

            // On from the corner to the sound, pulled straight: each leg to the farthest cell it can see.
            var at = heardFrom;
            var before = source;
            int current = corner;
            for (int legs = 0; current > 0 && legs < 16; legs++)
            {
                int next = current - 1;
                while (next > 0 && Visible(data, _path[next - 1], _path[current]))
                    next--;
                var point = next == 0 ? source : Center(data, toWorld, _path[next]);
                if (legs == 0)
                    before = point;
                length += Vector3.Distance(at, point);
                at = point;
                current = next;
            }
            if (current == 0 && at != source)
                length += Vector3.Distance(at, source);
            bend = Vector3.Angle(heardFrom - before, listener - heardFrom) / 180f;
            return SoundRoute.Around;
        }

        private static Vector3 Center(SoundFieldData data, Transform field, int cell)
        {
            int x = cell % data.Cells.x, y = cell / data.Cells.x % data.Cells.y, z = cell / (data.Cells.x * data.Cells.y);
            return field.TransformPoint(data.CellCenter(x, y, z));
        }

        private static int OpenNear(SoundFieldData data, int cell)
        {
            if (data.IsOpen(cell))
                return cell;
            for (int axis = 0; axis < 3; axis++)
                for (int sign = 1; sign >= -1; sign -= 2)
                {
                    int next = Neighbour(data, cell, axis, sign);
                    if (next >= 0 && data.IsOpen(next))
                        return next;
                }
            return -1;
        }

        // The listener's flood (made again when it moves to another cell, or the field is baked again).
        private static Flood FloodFrom(SoundField field, SoundFieldData data, int to)
        {
            if (!_floods.TryGetValue(field, out var flood) || flood.Data != data || flood.Stamp.Length != data.Count)
            {
                flood = new Flood { Data = data, Stamp = new int[data.Count], Parent = new int[data.Count], Steps = new short[data.Count] };
                _floods[field] = flood;
            }
            if (flood.From == to)
                return flood;
            flood.From = to;
            int generation = ++flood.Generation;
            _queue.Clear();
            flood.Stamp[to] = generation;
            flood.Parent[to] = -1;
            flood.Steps[to] = 0;
            _queue.Enqueue(to);
            while (_queue.Count > 0)
            {
                int cell = _queue.Dequeue();
                if (flood.Steps[cell] >= MaxSteps)
                    continue;
                for (int axis = 0; axis < 3; axis++)
                    for (int sign = 1; sign >= -1; sign -= 2)
                    {
                        int next = Passes(data, cell, axis, sign);
                        if (next < 0 || flood.Stamp[next] == generation)
                            continue;
                        flood.Stamp[next] = generation;
                        flood.Parent[next] = cell;
                        flood.Steps[next] = (short)(flood.Steps[cell] + 1);
                        _queue.Enqueue(next);
                    }
            }
            return flood;
        }

        // The next cell along an axis (-1 past the edge).
        private static int Neighbour(SoundFieldData data, int cell, int axis, int sign)
        {
            var cells = data.Cells;
            int x = cell % cells.x, y = cell / cells.x % cells.y, z = cell / (cells.x * cells.y);
            if (axis == 0) x += sign; else if (axis == 1) y += sign; else z += sign;
            if (x < 0 || y < 0 || z < 0 || x >= cells.x || y >= cells.y || z >= cells.z)
                return -1;
            return data.Index(x, y, z);
        }

        // The next cell along an axis, if sound passes into it (-1 if not).
        private static int Passes(SoundFieldData data, int cell, int axis, int sign)
        {
            int next = Neighbour(data, cell, axis, sign);
            if (next < 0)
                return -1;
            return data.Linked(sign > 0 ? cell : next, axis) ? next : -1;
        }

        // Whether sound goes straight from one cell's middle to another's, crossing only linked cells (a walk along the
        // line through the grid, a cell at a time).
        private static bool Visible(SoundFieldData data, int from, int to)
        {
            if (from == to)
                return true;
            var cells = data.Cells;
            int x = from % cells.x, y = from / cells.x % cells.y, z = from / (cells.x * cells.y);
            int tx = to % cells.x, ty = to / cells.x % cells.y, tz = to / (cells.x * cells.y);
            int dx = tx - x, dy = ty - y, dz = tz - z;
            int sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            // Along the line from one middle to the other (0 to 1): when it next crosses into a new cell on each axis.
            float stepX = dx != 0 ? 1f / Mathf.Abs(dx) : float.MaxValue, stepY = dy != 0 ? 1f / Mathf.Abs(dy) : float.MaxValue, stepZ = dz != 0 ? 1f / Mathf.Abs(dz) : float.MaxValue;
            float nextX = stepX * 0.5f, nextY = stepY * 0.5f, nextZ = stepZ * 0.5f;
            int cell = from;
            for (int guard = Mathf.Abs(dx) + Mathf.Abs(dy) + Mathf.Abs(dz); guard > 0; guard--)
            {
                int axis, sign;
                if (nextX <= nextY && nextX <= nextZ) { axis = 0; sign = sx; nextX += stepX; }
                else if (nextY <= nextZ) { axis = 1; sign = sy; nextY += stepY; }
                else { axis = 2; sign = sz; nextZ += stepZ; }
                cell = Passes(data, cell, axis, sign);
                if (cell < 0)
                    return false;
                if (cell == to)
                    return true;
            }
            return cell == to;
        }
    }
}
