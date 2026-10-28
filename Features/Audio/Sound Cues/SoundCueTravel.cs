using BlueGraph;
using Remedy.Schematics;
using System;
using System.Threading;
using UnityEngine;

namespace Remedy.Audio
{
    [SchematicDoc(@"{""name"": ""Travel Time"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Sound takes time to arrive: the audio is heard later the farther away it is (343 m a second), and as that changes, its pitch bends: up as it comes closer, down as it goes away (the Doppler effect: a passing car, a bullet's whizz, thunder after the flash). Doppler scales it (0 off, 1 real, 2 and up exaggerated). Max Distance is the farthest it allows for. Echoes, rooms and corners from Spatial effects and Around Corners before it arrive later to match."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Doppler"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}], ""use_for"": [""cars and planes passing by"", ""distant explosions and thunder arriving late"", ""whizzing projectiles""], ""notes"": ""Put it last. The cue goes on a little past its length while its sound is still on its way.""}")]
    [Serializable]
    [Node(Name = "Travel Time", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundTravelNode : SoundNode
    {
        /// <summary>How fast the pitch can bend, as how much the delay can change per second (0.5: up or down a half).</summary>
        public const float MaxBend = 0.5f;

        [Input("Audio", Editable = false)] public float AudioIn;
        [Tooltip("How much the pitch bends as it comes and goes (Doppler): 1 real, 0 none, 2 exaggerated.")]
        [Input("Doppler", Editable = true)] public float Doppler = 1f;
        [Editable, Tooltip("The farthest away it allows for, in meters (farther is heard as if this far).")]
        public float MaxDistance = 300f;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            var audio = builder.Input(this, "Audio", 0f);
            // How much it can stretch: the typed Doppler, or up to 4x when something drives it.
            float doppler = builder.IsConnected(this, "Doppler") ? 4f : Mathf.Clamp(Doppler, 0f, 4f);
            float longest = Mathf.Max(0.01f, MaxDistance) / SoundSpaces.SpeedOfSound * doppler;
            builder.NoteTail(longest);
            var travel = new TravelDelay();
            // Spatial effects and Around Corners before it: what they send is heard later too.
            foreach (var processor in builder.Upstream(this, "Audio"))
                if (processor is ISpatialSends sends)
                    sends.Sender.Travel = travel;
            return new Processor
            {
                In = audio,
                Doppler = builder.Input(this, "Doppler", Doppler),
                Line = new float[(int)(longest * builder.SampleRate) + 8],
                MaxDistance = Mathf.Max(0.01f, MaxDistance),
                Travel = travel,
            };
        }

        private sealed class Processor : SoundProcessor
        {
            public float[] In, Doppler, Line;
            public float MaxDistance;
            public TravelDelay Travel;
            private int _write;
            private double _delay;
            private bool _primed;

            public override void Reset(SoundContext context)
            {
                Array.Clear(Line, 0, Line.Length);
                _write = 0;
                _primed = false;
            }

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                int rate = context.SampleRate, length = Line.Length;
                float distance = Mathf.Min(context.Distance, MaxDistance);
                // The listener's distance changes a frame at a time: the delay follows it smoothly (over about 50 ms),
                // no faster than MaxBend, so the pitch glides rather than steps.
                double follow = 1.0 / (0.05 * rate), most = MaxBend;
                for (int i = 0; i < count; i++)
                {
                    double target = distance / SoundSpaces.SpeedOfSound * Mathf.Clamp(Doppler[i], 0f, 4f) * rate;
                    if (double.IsNaN(target) || double.IsInfinity(target))
                        target = _primed ? _delay : 0.0; // a glitch upstream mustn't throw the read off the line
                    if (!_primed)
                    {
                        _delay = target;
                        _primed = true;
                    }
                    _delay += Math.Max(-most, Math.Min(most, (target - _delay) * follow));
                    _delay = Math.Max(0.0, Math.Min(length - 3, _delay));

                    Line[_write] = In[i];
                    double read = _write - _delay;
                    if (read < 0.0) read += length;
                    int a = (int)read;
                    if (a >= length) a -= length;
                    float f = (float)(read - Math.Floor(read));
                    int b = a + 1 == length ? 0 : a + 1;
                    o[i] = Line[a] + (Line[b] - Line[a]) * f;
                    _write = _write + 1 == length ? 0 : _write + 1;
                }
                Volatile.Write(ref Travel.Samples, (int)_delay);
            }
        }
    }
}
