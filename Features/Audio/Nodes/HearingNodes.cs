using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Audio
{
    [SchematicDoc(@"{""name"": ""Hear"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Senses"", ""description"": ""What an AI hears: the loudest noise reaching its ears (Sound Cues as they play, by their Output's Heard By AI range, and Make Noise), the way sound gets there through the Sound Field. Straight, round a corner or through a doorway (heard from there: Direction points at the opening, quieter and a little muffled for the longer way), or only through the walls (much quieter, fully muffled). Values packs it for a Brain input of Size 9: loudness, muffled, direction x, z and y (local: x right, z ahead), how far round a corner it came (0 straight to 1 fully round, fading in as the noise slips behind the corner), through walls only (1 or 0), and the direction x and z of the doorway it came through (else of where it's heard from)."", ""inputs"": [{""name"": ""Ear"", ""type"": ""GameObject""}, {""name"": ""Sensitivity"", ""type"": ""float""}, {""name"": ""Kind"", ""type"": ""string""}, {""name"": ""Height"", ""type"": ""float""}], ""outputs"": [{""name"": ""Heard"", ""type"": ""bool""}, {""name"": ""Loudness"", ""type"": ""float""}, {""name"": ""Muffled"", ""type"": ""float""}, {""name"": ""Direction"", ""type"": ""Vector3""}, {""name"": ""Heard From"", ""type"": ""Vector3""}, {""name"": ""Came Round"", ""type"": ""bool""}, {""name"": ""Through Walls"", ""type"": ""bool""}, {""name"": ""Opening"", ""type"": ""Vector3""}, {""name"": ""Opening Direction"", ""type"": ""Vector3""}, {""name"": ""Noise Position"", ""type"": ""Vector3""}, {""name"": ""Distance"", ""type"": ""float""}, {""name"": ""Source"", ""type"": ""GameObject""}, {""name"": ""Kind"", ""type"": ""string""}, {""name"": ""Values"", ""type"": ""float[]""}], ""use_for"": [""AI that investigate noises"", ""stealth: guards hearing footsteps through a doorway"", ""a Brain input for hearing""], ""notes"": ""Runs when flow reaches it. Sensitivity scales every noise's range (2: hears twice as far). Kind only hears noises of that kind (a Sound Cue's noise kind is its name). An AI doesn't hear its own noises. Doorways need a Sound Field baked with this version.""}")]
    [Serializable]
    [NodeColor(NodeColors.AI)]
    [Node(Name = "Hear", Path = "AI/Senses"), Tags("Object")]
    public class HearNode : SchematicActionNode
    {
        /// <summary>How many numbers <see cref="Values"/> holds (a Brain input slot's Size).</summary>
        public const int ValueCount = 9;

        [Tooltip("Whose ears (the object running this graph if nothing's wired in).")]
        [Input("Ear")] public GameObject Ear;
        [Tooltip("How keen its hearing is: scales every noise's range (2 hears twice as far, 0.5 half as far).")]
        [Input("Sensitivity", Editable = true)] public float Sensitivity = 1f;
        [Tooltip("Only hear noises of this kind (\"footstep\", \"alarm\"; a Sound Cue's kind is its name). Empty: hears anything.")]
        [Input("Kind", Editable = true)] public string Kind;
        [Tooltip("How high its ears are above its position (meters), where the sound is checked from.")]
        [Input("Height", Editable = true)] public float Height = 1.6f;

        [Tooltip("True if it heard anything this time; the other outputs only mean something then.")]
        [Output("Heard")] public bool Heard;
        [Tooltip("How loud the loudest noise is at its ears: 1 right next to it, falling to 0 at the edge of its range.")]
        [Output("Loudness")] public float Loudness;
        [Tooltip("How muffled it is: 0 clear, a little for round a corner, 1 only through walls.")]
        [Output("Muffled")] public float Muffled;
        [Output("Direction")] public Vector3 Direction;
        [Tooltip("Where the sound reaches it from: the noise itself, or the corner or doorway it came round.")]
        [Output("Heard From")] public Vector3 HeardFrom;
        [Tooltip("True if it came round a corner or through a doorway rather than straight.")]
        [Output("Came Round")] public bool CameRound;
        [Tooltip("True if it's only heard through the walls (no way round).")]
        [Output("Through Walls")] public bool ThroughWalls;
        [Tooltip("The doorway or opening it came through (when it came round).")]
        [Output("Opening")] public Vector3 Opening;
        [Tooltip("Which way that opening is, as the AI sees it (x right, z ahead).")]
        [Output("Opening Direction")] public Vector3 OpeningDirection;
        [Tooltip("Where the noise actually is (which may be behind a wall).")]
        [Output("Noise Position")] public Vector3 NoisePosition;
        [Output("Distance")] public float Distance;
        [Tooltip("What made the noise (empty for a Make Noise with no object).")]
        [Output("Source")] public GameObject Source;
        [Tooltip("What kind of noise it was.")]
        [Output("Kind")] public string HeardKind;
        [Tooltip("Everything above as 9 numbers for a Brain input of Size 9 (see the node's help for the order).")]
        [Output("Values")] public float[] Values;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var ear = GetInputValue<GameObject>("Ear", Ear) ?? instance;
            var values = new float[ValueCount];
            if (ear == null || !SoundHearing.Loudest(ear.transform.position + ear.transform.up * GetInputValue("Height", Height),
                    GetInputValue("Sensitivity", Sensitivity), instance != null ? instance : ear, out var heard, GetInputValue("Kind", Kind)))
            {
                SetOutputValue("Heard", false);
                SetOutputValue("Loudness", 0f);
                SetOutputValue("Muffled", 0f);
                SetOutputValue("Direction", Vector3.zero);
                SetOutputValue("Came Round", false);
                SetOutputValue("Through Walls", false);
                SetOutputValue("Opening Direction", Vector3.zero);
                SetOutputValue("Source", (GameObject)null);
                SetOutputValue("Kind", (string)null);
                SetOutputValue("Values", values);
                return;
            }

            var transform = ear.transform;
            var direction = Local(transform, heard.HeardFrom);
            // The opening it came through: the doorway nearest the ear on its way, else the corner it came round.
            var opening = heard.HeardFrom;
            if (heard.Doorway >= 0 && SoundPaths.TryGetDoorway(heard.Field, heard.Doorway, out var doorway, out _, out _))
                opening = doorway;
            var openingDirection = Local(transform, opening);

            values[0] = heard.Loudness;
            values[1] = heard.Muffled;
            values[2] = direction.x;
            values[3] = direction.z;
            values[4] = direction.y;
            values[5] = heard.Route == SoundRoute.Around ? heard.Behind : 0f;
            values[6] = heard.Route == SoundRoute.Sealed ? 1f : 0f;
            values[7] = openingDirection.x;
            values[8] = openingDirection.z;

            SetOutputValue("Heard", true);
            SetOutputValue("Loudness", heard.Loudness);
            SetOutputValue("Muffled", heard.Muffled);
            SetOutputValue("Direction", direction);
            SetOutputValue("Heard From", heard.HeardFrom);
            SetOutputValue("Came Round", heard.Route == SoundRoute.Around && heard.Behind >= 0.5f);
            SetOutputValue("Through Walls", heard.Route == SoundRoute.Sealed);
            SetOutputValue("Opening", opening);
            SetOutputValue("Opening Direction", openingDirection);
            SetOutputValue("Noise Position", heard.Noise.Position);
            SetOutputValue("Distance", heard.Distance);
            SetOutputValue("Source", heard.Noise.Source);
            SetOutputValue("Kind", heard.Noise.Kind);
            SetOutputValue("Values", values);
        }

        // Which way a point is from an object's point of view (x right, y up, z ahead), unit length.
        internal static Vector3 Local(Transform from, Vector3 point)
        {
            var local = from.InverseTransformDirection(point - from.position);
            return local.sqrMagnitude > 1e-8f ? local.normalized : Vector3.zero;
        }
    }

    [SchematicDoc(@"{""name"": ""Nearest Openings"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Senses"", ""description"": ""The doorways and openings (found when the Sound Field was baked) nearest an AI that it can see straight, nearest first: the ways out of where it is. Values packs Count of them for a Brain input of Size Count x 3: each one's direction x and z (local: x right, z ahead) and how near it is (1 right here, 0 at Range); missing ones are 0."", ""inputs"": [{""name"": ""From"", ""type"": ""GameObject""}, {""name"": ""Count"", ""type"": ""int""}, {""name"": ""Range"", ""type"": ""float""}, {""name"": ""Height"", ""type"": ""float""}], ""outputs"": [{""name"": ""Found"", ""type"": ""int""}, {""name"": ""Nearest"", ""type"": ""Vector3""}, {""name"": ""Nearest Distance"", ""type"": ""float""}, {""name"": ""Values"", ""type"": ""float[]""}], ""use_for"": [""AI that know the ways out of a room"", ""a Brain input for exits""], ""notes"": ""Runs when flow reaches it. Needs a Sound Field baked with this version.""}")]
    [Serializable]
    [NodeColor(NodeColors.AI)]
    [Node(Name = "Nearest Openings", Path = "AI/Senses"), Tags("Object")]
    public class NearestOpeningsNode : SchematicActionNode
    {
        [Tooltip("Whose surroundings (the object running this graph if nothing's wired in).")]
        [Input("From")] public GameObject From;
        [Input("Count", Editable = true)] public int Count = 3;
        [Tooltip("How far to look for openings (meters).")]
        [Input("Range", Editable = true)] public float Range = 20f;
        [Tooltip("How high above its position to look from (meters), so low clutter doesn't block the view.")]
        [Input("Height", Editable = true)] public float Height = 1f;

        [Tooltip("How many openings it found (up to Count).")]
        [Output("Found")] public int Found;
        [Tooltip("Where the nearest opening is.")]
        [Output("Nearest")] public Vector3 Nearest;
        [Tooltip("How far away the nearest opening is (meters).")]
        [Output("Nearest Distance")] public float NearestDistance;
        [Tooltip("Count openings as numbers for a Brain input of Size Count × 3: each one's direction x and z and how near it is (1 here, 0 at Range).")]
        [Output("Values")] public float[] Values;

        private (SoundField field, int doorway, float distance)[] _found = Array.Empty<(SoundField, int, float)>();

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var from = (GetInputValue<GameObject>("From", From) ?? instance).transform;
            int count = Mathf.Clamp(GetInputValue("Count", Count), 1, 16);
            float range = Mathf.Max(0.1f, GetInputValue("Range", Range));
            if (_found.Length != count)
                _found = new (SoundField, int, float)[count];
            var eye = from.position + from.up * GetInputValue("Height", Height);
            int found = SoundHearing.Openings(eye, range, _found);

            var values = new float[count * 3];
            for (int i = 0; i < found; i++)
            {
                if (!SoundPaths.TryGetDoorway(_found[i].field, _found[i].doorway, out var position, out _, out _))
                    continue;
                var direction = HearNode.Local(from, position);
                values[i * 3] = direction.x;
                values[i * 3 + 1] = direction.z;
                values[i * 3 + 2] = 1f - Mathf.Clamp01(_found[i].distance / range);
                if (i == 0)
                {
                    SetOutputValue("Nearest", position);
                    SetOutputValue("Nearest Distance", _found[i].distance);
                }
            }
            if (found == 0)
            {
                SetOutputValue("Nearest", eye);
                SetOutputValue("Nearest Distance", range);
            }
            SetOutputValue("Found", found);
            SetOutputValue("Values", values);
        }
    }

    [SchematicDoc(@"{""name"": ""Make Noise"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""AI/Senses"", ""description"": ""A noise AI can hear (with Hear) without playing a sound: heard up to Range meters away in the open for Duration seconds, from At (with nothing connected, the object running the graph), following it while it moves. Kind says what it is (\""footstep\"", \""alarm\""), for Hear's Kind. Sound Cues already make noises as they play."", ""inputs"": [{""name"": ""At"", ""type"": ""GameObject""}, {""name"": ""Range"", ""type"": ""float""}, {""name"": ""Duration"", ""type"": ""float""}, {""name"": ""Kind"", ""type"": ""string""}], ""use_for"": [""a thrown object's landing that distracts guards"", ""silent gameplay noises (an alarm the player can't hear yet)""], ""notes"": ""Runs when flow reaches it. In Play Mode only.""}")]
    [Serializable]
    [NodeColor(NodeColors.AI)]
    [Node(Name = "Make Noise", Path = "AI/Senses"), Tags("Object")]
    public class MakeNoiseNode : SchematicActionNode
    {
        [Tooltip("Where the noise is (the object running this graph if nothing's wired in); it follows that object while it lasts.")]
        [Input("At")] public GameObject At;
        [Tooltip("How far it can be heard in the open (meters). Walls and corners make it reach less far.")]
        [Input("Range", Editable = true)] public float Range = 20f;
        [Input("Duration", Editable = true)] public float Duration = 0.5f;
        [Tooltip("What kind of noise it is (\"footstep\", \"alarm\"), so a Hear node can listen for just that kind.")]
        [Input("Kind", Editable = true)] public string Kind;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var at = GetInputValue<GameObject>("At", At) ?? instance;
            if (at == null)
                return;
            SoundHearing.Make(at.transform.position, GetInputValue("Range", Range), GetInputValue("Duration", Duration), at, GetInputValue("Kind", Kind), at.transform);
        }
    }
}
