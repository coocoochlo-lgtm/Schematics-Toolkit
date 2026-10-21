#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>Surroundings the Sound Cue preview can play in (Spatial Reverb and Delay hear them like a baked Sound Field).</summary>
    public enum PreviewSpace { None, OpenField, SmallRoom, Hall, Cave, Tunnel, Street, Canyon, Cliff }

    /// <summary>
    /// Editor only: made-up surroundings around the preview's imaginary listener (at the middle, facing +Z): walls with
    /// materials, and maybe a room. Its room and walls are fixed sources (rooms ring from where they are, echoes come off
    /// the walls along the real reflected path), so tails stay put while the previewed sound moves.
    /// </summary>
    public sealed class PreviewSpaceSimulation : SoundSpaceSimulation
    {
        /// <summary>A wall, as seen from above: a segment (X, Z) facing the listener's side, made of something.</summary>
        public sealed class Wall
        {
            public Vector2 A, B, Normal;
            public float Reflectivity, Hardness;
            public SpaceBus Bus;
            /// <summary>The point on the wall nearest the listener: where its echoes are heard from.</summary>
            public Vector2 Foot;
        }

        public readonly PreviewSpace Space;
        public readonly List<Wall> Walls = new();
        /// <summary>The room's floor plan (X, Z), when the space is a room.</summary>
        public readonly Rect? Room;
        public SpaceBus RoomBus { get; private set; }
        private readonly TailPool _tails;
        public override TailPool Tails => _tails;
        private readonly SoundAcoustics _place;
        private readonly int _rate;

        public override int Rate => _rate;

        public PreviewSpaceSimulation(PreviewSpace space, Transform host)
        {
            Space = space;
            _rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            _tails = new TailPool(host, _rate, flat: true);
            // Rough places (the size is how far surfaces are on average), and their walls.
            switch (space)
            {
                case PreviewSpace.OpenField:
                    _place = Place(size: 20f, enclosure: 0.45f, absorption: 0.4f, hardness: 0.5f);
                    break;
                case PreviewSpace.SmallRoom: // a tiled 4 x 5 m bathroom
                    _place = Place(2.5f, 1f, 0.05f, 0.95f, wetness: 0.3f);
                    Box(new Rect(-2f, -2.5f, 4f, 5f), 0.95f, 0.95f);
                    Room = new Rect(-2f, -2.5f, 4f, 5f);
                    break;
                case PreviewSpace.Hall: // a 24 x 36 m stone hall
                    _place = Place(12f, 1f, 0.08f, 0.9f);
                    Box(new Rect(-12f, -18f, 24f, 36f), 0.92f, 0.9f);
                    Room = new Rect(-12f, -18f, 24f, 36f);
                    break;
                case PreviewSpace.Cave: // a long wet rock cave
                    _place = Place(9f, 1f, 0.03f, 1f, wetness: 0.4f);
                    Box(new Rect(-8f, -15f, 16f, 30f), 0.97f, 1f);
                    Room = new Rect(-8f, -15f, 16f, 30f);
                    break;
                case PreviewSpace.Tunnel: // a 6 m wide concrete tunnel, its ends far ahead and behind
                    _place = Place(5f, 0.95f, 0.05f, 0.95f);
                    Box(new Rect(-3f, -60f, 6f, 120f), 0.95f, 0.95f);
                    Room = new Rect(-3f, -60f, 6f, 120f);
                    break;
                case PreviewSpace.Street: // buildings either side, open above
                    _place = Place(12f, 0.6f, 0.15f, 0.85f);
                    Line(new Vector2(-10f, -60f), new Vector2(-10f, 60f), Vector2.right, 0.85f, 0.85f);
                    Line(new Vector2(10f, -60f), new Vector2(10f, 60f), Vector2.left, 0.85f, 0.85f);
                    break;
                case PreviewSpace.Canyon: // rock cliffs either side
                    _place = Place(30f, 0.55f, 0.05f, 1f);
                    Line(new Vector2(-35f, -120f), new Vector2(-35f, 120f), Vector2.right, 0.95f, 1f);
                    Line(new Vector2(35f, -120f), new Vector2(35f, 120f), Vector2.left, 0.95f, 1f);
                    break;
                case PreviewSpace.Cliff: // one cliff face far ahead
                    _place = Place(40f, 0.35f, 0.05f, 1f);
                    Line(new Vector2(-120f, 70f), new Vector2(120f, 70f), Vector2.down, 0.95f, 1f);
                    break;
            }

            // The fixed sources: the room around the listener, and each wall (heard from its nearest point).
            if (Room.HasValue)
            {
                RoomBus = SpaceBus.Create(host, _rate);
                RoomBus.BeginRoom(this, Vector3.zero, 1f);
                RoomBus.SetRoom(_place.Feedback, _place.Damping);
                RoomBus.SetFlat(0f, 1f);
            }
            foreach (var wall in Walls)
            {
                wall.Foot = ClosestOnSegment(wall.A, wall.B, Vector2.zero);
                wall.Bus = SpaceBus.Create(host, _rate);
                wall.Bus.BeginEcho(wall, new Vector3(wall.Foot.x, 0f, wall.Foot.y), (wall.B - wall.A).magnitude * 0.2f, wall.Hardness);
                float distance = wall.Foot.magnitude;
                float near = Mathf.Clamp((wall.B - wall.A).magnitude * 0.2f, 3f, 40f);
                wall.Bus.SetFlat(distance > 0.01f ? wall.Foot.x / distance : 0f, Mathf.Min(1f, near / Mathf.Max(near, distance)));
            }
            // Facing walls echo into each other (a canyon's or a street's flutter).
            foreach (var wall in Walls)
                foreach (var other in Walls)
                    if (other != wall && Vector2.Dot(wall.Normal, other.Normal) < -0.9f)
                    {
                        float gap = Mathf.Abs(Vector2.Dot(other.A - wall.A, wall.Normal));
                        if (gap / SoundSpaces.SpeedOfSound >= SoundSpaces.MinFlutterDelay)
                            wall.Bus.SetPartner(other.Bus, (int)(gap / SoundSpaces.SpeedOfSound * _rate), SoundSpaces.FlutterGain * wall.Reflectivity);
                    }
        }

        private static SoundAcoustics Place(float size, float enclosure, float absorption, float hardness, float wetness = 0f)
            => new() { Size = size, Enclosure = enclosure, Absorption = absorption, Hardness = hardness, Wetness = wetness };

        private void Box(Rect r, float reflectivity, float hardness)
        {
            Line(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), Vector2.right, reflectivity, hardness);
            Line(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), Vector2.left, reflectivity, hardness);
            Line(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), Vector2.up, reflectivity, hardness);
            Line(new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), Vector2.down, reflectivity, hardness);
        }

        private void Line(Vector2 a, Vector2 b, Vector2 normal, float reflectivity, float hardness)
            => Walls.Add(new Wall { A = a, B = b, Normal = normal, Reflectivity = reflectivity, Hardness = hardness });

        private static Vector2 ClosestOnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return a + ab * t;
        }

        private static Vector2 Flat(Vector3 p) => new(p.x, p.z);

        public override bool Sample(Vector3 position, out SoundAcoustics place)
        {
            place = _place;
            // The nearest wall from where the sound is (the ground doesn't count: it's under everything).
            var p = Flat(position);
            float nearest = 60f;
            foreach (var wall in Walls)
                nearest = Mathf.Min(nearest, (ClosestOnSegment(wall.A, wall.B, p) - p).magnitude);
            place.NearestWall = nearest;
            return Space != PreviewSpace.None;
        }

        public override SpaceBus RoomAt(Vector3 position, out float wet)
        {
            wet = _place.Wet;
            return Room.HasValue && Room.Value.Contains(Flat(position)) ? RoomBus : null;
        }

        // Mirror-image echoes: the sound reflected in a wall's line is heard along the straight path from its image to
        // the listener, if that path crosses the wall itself (and both are on the wall's open side).
        public override int Echoes(Vector3 position, Vector3 listener, SpaceBus[] buses, float[] paths, float[] reflectivity, float[] hardness)
        {
            var s = Flat(position);
            var l = Flat(listener);
            int count = 0;
            float scoreA = -1f, scoreB = -1f;
            foreach (var wall in Walls)
            {
                float ds = Vector2.Dot(s - wall.A, wall.Normal), dl = Vector2.Dot(l - wall.A, wall.Normal);
                if (ds <= 0.01f || dl <= 0.01f) continue;
                var image = s - 2f * ds * wall.Normal;
                var hit = l + (image - l) * (dl / (dl + ds));
                var ab = wall.B - wall.A;
                float along = Vector2.Dot(hit - wall.A, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude);
                if (along < 0f || along > 1f) continue;
                float path = (image - l).magnitude;
                float score = wall.Reflectivity / (1f + path / 50f);
                if (score > scoreA)
                {
                    if (count > 0) { buses[1] = buses[0]; paths[1] = paths[0]; reflectivity[1] = reflectivity[0]; hardness[1] = hardness[0]; scoreB = scoreA; }
                    buses[0] = wall.Bus; paths[0] = path; reflectivity[0] = wall.Reflectivity; hardness[0] = wall.Hardness; scoreA = score;
                    count = Mathf.Min(2, count + 1);
                }
                else if (score > scoreB)
                {
                    buses[1] = wall.Bus; paths[1] = path; reflectivity[1] = wall.Reflectivity; hardness[1] = wall.Hardness; scoreB = score;
                    count = 2;
                }
            }
            return count;
        }
    }
}
#endif
