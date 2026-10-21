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

    /// <summary>The way sound takes between two points (see <see cref="SoundPaths.Between"/>).</summary>
    public struct SoundWay
    {
        public SoundRoute Route;
        /// <summary>How far it travels: the whole way round when it goes round, else the straight distance.</summary>
        public float Length;
        /// <summary>
        /// Going round: where each end hears it come from (the last corner before A, and before B), and how sharply it
        /// bends there (0 straight on, 1 right back). Otherwise the other end itself, and 0.
        /// </summary>
        public Vector3 CornerA, CornerB;
        public float BendA, BendB;
        /// <summary>The doorway it passes through nearest each end (an index into the field's doorways; -1: none).</summary>
        public int DoorwayA, DoorwayB;
        /// <summary>The field the way was found in (null: none).</summary>
        public SoundField Field;
    }

    /// <summary>
    /// Sound round corners: the way sound takes between two points when the straight line is blocked, through a
    /// <see cref="SoundField"/>'s cells and the links baked between them (where sound passes from one cell to the
    /// next). From one end's cell it floods out over the links (floods are kept for the latest few ends: the listener,
    /// the noises AI are hearing), so the way from anywhere is a walk back along the flood. That way is pulled straight
    /// round the corners it has to turn: the last corner before each end is where that end hears it from, and the
    /// whole length is how far it travelled. On the way it notes the doorways it goes through.
    /// </summary>
    public static class SoundPaths
    {
        /// <summary>How far a flood reaches, in cells (farther round than this is sealed off).</summary>
        public const int MaxSteps = 48;
        /// <summary>How many floods are kept (the listener's, and the noises being heard).</summary>
        public const int KeptFloods = 8;

        private sealed class Flood
        {
            public SoundField Field;
            public SoundFieldData Data;
            public int From = -1;
            public int Generation;
            public int[] Stamp, Parent;
            public short[] Steps;
        }

        // Latest used last.
        private static readonly List<Flood> _floods = new();
        private static readonly Dictionary<SoundFieldData, Dictionary<int, int>> _doorwayOf = new();
        private static readonly Queue<int> _queue = new();
        private static readonly List<int> _path = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _floods.Clear();
            _doorwayOf.Clear();
        }

        /// <summary>
        /// How a sound at <paramref name="source"/> reaches the listener. Round a corner: where it's heard from (the
        /// corner), the whole length of the way, and how sharply it bends there (0 straight on, 1 right back).
        /// </summary>
        public static SoundRoute Around(Vector3 source, Vector3 listener, out Vector3 heardFrom, out float length, out float bend)
        {
            var way = Between(source, listener);
            heardFrom = way.CornerB;
            length = way.Length;
            bend = way.BendB;
            return way.Route;
        }

        /// <summary>
        /// The way sound takes between <paramref name="a"/> and <paramref name="b"/> (it floods from B: keep B the end
        /// many ask about, a listener or a noise).
        /// </summary>
        public static SoundWay Between(Vector3 a, Vector3 b)
        {
            var way = new SoundWay
            {
                Route = SoundRoute.Clear, Length = Vector3.Distance(a, b), CornerA = b, CornerB = a, DoorwayA = -1, DoorwayB = -1,
            };
            if (!SoundField.TryGetCell(b, out var field, out int to) || !SoundField.TryGetCell(a, out var other, out int from) || other != field)
                return way;
            way.Field = field;
            var data = field.Data;
            if (!data.HasLinks)
                return way;
            // An end sunk into the floor or a wall is taken from the open cell beside it.
            from = OpenNear(data, from);
            to = OpenNear(data, to);
            if (from < 0 || to < 0)
                return way;
            var doorways = DoorwaysOf(data);
            if (Visible(data, from, to, doorways, out way.DoorwayA, out way.DoorwayB))
                return way;

            var flood = FloodFrom(field, data, to);
            if (flood.Stamp[from] != flood.Generation)
            {
                way.Route = SoundRoute.Sealed;
                return way;
            }

            // The cells from A back to B, and the doorways on the way (the first from each end).
            _path.Clear();
            for (int cell = from; cell >= 0 && _path.Count <= MaxSteps + 1; cell = flood.Parent[cell])
                _path.Add(cell);
            int last = _path.Count - 1;
            if (doorways != null)
                for (int i = 0; i < last; i++)
                {
                    int doorway = DoorwayBetween(data, doorways, _path[i], _path[i + 1]);
                    if (doorway < 0) continue;
                    if (way.DoorwayA < 0) way.DoorwayA = doorway;
                    way.DoorwayB = doorway;
                }

            var transform = field.transform;
            // B's corner: the farthest cell along the way B can still see.
            int cornerB = last;
            while (cornerB > 0 && Visible(data, _path[cornerB - 1], to))
                cornerB--;
            way.CornerB = Center(data, transform, _path[cornerB]);
            float length = Vector3.Distance(b, way.CornerB);

            // On from B's corner to A, pulled straight: each leg to the farthest cell it can see.
            var at = way.CornerB;
            var beyondB = a;
            int current = cornerB;
            for (int legs = 0; current > 0 && legs < 16; legs++)
            {
                int next = current - 1;
                while (next > 0 && Visible(data, _path[next - 1], _path[current]))
                    next--;
                var point = next == 0 ? a : Center(data, transform, _path[next]);
                if (legs == 0)
                    beyondB = point;
                length += Vector3.Distance(at, point);
                at = point;
                current = next;
            }
            way.Length = length;
            way.BendB = Vector3.Angle(way.CornerB - beyondB, b - way.CornerB) / 180f;

            // A's corner: the farthest cell along the way A can still see, and the next one round from there.
            int cornerA = 0;
            while (cornerA < last && Visible(data, from, _path[cornerA + 1]))
                cornerA++;
            way.CornerA = Center(data, transform, _path[cornerA]);
            int beyond = cornerA;
            while (beyond < last && Visible(data, _path[cornerA], _path[beyond + 1]))
                beyond++;
            var beyondA = beyond >= last ? b : Center(data, transform, _path[beyond]);
            way.BendA = Vector3.Angle(way.CornerA - a, beyondA - way.CornerA) / 180f;
            way.Route = SoundRoute.Around;
            return way;
        }

        /// <summary>A field's doorway in the world: its middle, the way through it, and roughly how wide it is.</summary>
        public static bool TryGetDoorway(SoundField field, int doorway, out Vector3 position, out Vector3 through, out float width)
        {
            position = through = default;
            width = 0f;
            var list = field != null && field.Data != null ? field.Data.Doorways : null;
            if (list == null || doorway < 0 || doorway >= list.Count)
                return false;
            var d = list[doorway];
            position = field.transform.TransformPoint(d.Position);
            through = field.transform.TransformDirection(d.Normal).normalized;
            width = d.Width;
            return true;
        }

        /// <summary>
        /// Whether sound passes straight between two world points through the field's cells (true when there's no
        /// field around both, or it was baked without links).
        /// </summary>
        public static bool InSight(SoundField field, Vector3 a, Vector3 b)
        {
            var data = field != null ? field.Data : null;
            if (data == null || !data.HasLinks)
                return true;
            int from = OpenNear(data, CellOf(field, a)), to = OpenNear(data, CellOf(field, b));
            return from < 0 || to < 0 || Visible(data, from, to, null, out _, out _);
        }

        private static int CellOf(SoundField field, Vector3 world)
        {
            var data = field.Data;
            var p = (field.transform.InverseTransformPoint(world) - data.Box.min) / data.CellSize;
            return data.Index(Mathf.Clamp(Mathf.FloorToInt(p.x), 0, data.Cells.x - 1), Mathf.Clamp(Mathf.FloorToInt(p.y), 0, data.Cells.y - 1), Mathf.Clamp(Mathf.FloorToInt(p.z), 0, data.Cells.z - 1));
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

        // Which doorway each of a field's doorway steps belongs to (cell * 3 + axis -> doorway), made once per bake.
        private static Dictionary<int, int> DoorwaysOf(SoundFieldData data)
        {
            if (data.Doorways == null || data.Doorways.Count == 0)
                return null;
            if (_doorwayOf.TryGetValue(data, out var map))
                return map;
            map = new Dictionary<int, int>();
            for (int d = 0; d < data.Doorways.Count; d++)
                if (data.Doorways[d].Links != null)
                    foreach (int key in data.Doorways[d].Links)
                        map[key] = d;
            _doorwayOf[data] = map;
            return map;
        }

        // The doorway the step between two neighbouring cells goes through (-1: none).
        private static int DoorwayBetween(SoundFieldData data, Dictionary<int, int> doorways, int p, int q)
        {
            int difference = q - p, low = Mathf.Min(p, q);
            int axis = Mathf.Abs(difference) == 1 ? 0 : Mathf.Abs(difference) == data.Cells.x ? 1 : 2;
            return doorways.TryGetValue(low * 3 + axis, out int doorway) ? doorway : -1;
        }

        // A flood from a cell: kept for the latest few, made again when the field is baked again.
        private static Flood FloodFrom(SoundField field, SoundFieldData data, int to)
        {
            for (int i = _floods.Count - 1; i >= 0; i--)
            {
                var kept = _floods[i];
                if (kept.Field == field && kept.Data == data && kept.From == to)
                {
                    _floods.RemoveAt(i);
                    _floods.Add(kept);
                    return kept;
                }
            }
            Flood flood;
            if (_floods.Count >= KeptFloods)
            {
                // The least recently used one makes way (its arrays reused when they fit).
                flood = _floods[0];
                _floods.RemoveAt(0);
            }
            else
                flood = new Flood();
            if (flood.Stamp == null || flood.Stamp.Length != data.Count)
            {
                flood.Stamp = new int[data.Count];
                flood.Parent = new int[data.Count];
                flood.Steps = new short[data.Count];
                flood.Generation = 0;
            }
            flood.Field = field;
            flood.Data = data;
            flood.From = to;
            _floods.Add(flood);

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

        private static bool Visible(SoundFieldData data, int from, int to) => Visible(data, from, to, null, out _, out _);

        // Whether sound goes straight from one cell's middle to another's, crossing only linked cells (a walk along the
        // line through the grid, a cell at a time), and the first and last doorways it crosses on the way.
        private static bool Visible(SoundFieldData data, int from, int to, Dictionary<int, int> doorways, out int firstDoorway, out int lastDoorway)
        {
            firstDoorway = lastDoorway = -1;
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
                int next = Passes(data, cell, axis, sign);
                if (next < 0)
                {
                    firstDoorway = lastDoorway = -1;
                    return false;
                }
                if (doorways != null && doorways.TryGetValue(Mathf.Min(cell, next) * 3 + axis, out int doorway))
                {
                    if (firstDoorway < 0) firstDoorway = doorway;
                    lastDoorway = doorway;
                }
                cell = next;
                if (cell == to)
                    return true;
            }
            return cell == to;
        }
    }
}
