using BlueGraph;
using Remedy.Schematics;
using Remedy.Surfaces;
using System;
using UnityEngine;

namespace Remedy.Audio
{
    [SchematicDoc(@"{""name"": ""Impact"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Sources"", ""description"": ""A hit, made from scratch every time: a short strike that sets a set of resonances ringing, the way a real object rings when it's struck. Its Surface Material (or the values wired in, e.g. from a Surface node) shapes it: Hardness makes the strike sharper and brighter (soft: a dull thud; hard: a click and ping), Absorption makes the high ringing die sooner, Ring is how long it rings (0 a dead thunk, 1 a bell; a material's own Ring property is used when it has one), Wetness adds a splash. Size is the object's size in meters (bigger rings lower). Strength is how hard it's hit (wire the hit speed in through a Parameter): louder, and a little brighter. It strikes when the cue starts, and again whenever Hit rises past 0.5. Every strike varies a little."", ""inputs"": [{""name"": ""Strength"", ""type"": ""float""}, {""name"": ""Size"", ""type"": ""float""}, {""name"": ""Hardness"", ""type"": ""float""}, {""name"": ""Absorption"", ""type"": ""float""}, {""name"": ""Ring"", ""type"": ""float""}, {""name"": ""Wetness"", ""type"": ""float""}, {""name"": ""Hit"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}], ""use_for"": [""collision sounds for every material without a sample library"", ""footsteps that sound like the floor"", ""dropped props, clanging pipes, wooden thunks, splashes""]}")]
    [Serializable]
    [Node(Name = "Impact", Path = "Sources"), Tags(SoundNode.Tag)]
    public class SoundImpactNode : SoundNode
    {
        /// <summary>The most resonances it rings.</summary>
        public const int MaxModes = 16;

        [Input("Strength", Editable = true)] public float Strength = 1f;
        [Input("Size", Editable = true)] public float Size = 0.5f;
        [Tooltip("How hard the surface is, 0 to 1: harder rings higher and brighter (metal, stone); softer thuds (wood, earth).")]
        [Input("Hardness", Editable = true)] public float Hardness = 0.5f;
        [Tooltip("How much it soaks up, 0 to 1: higher deadens the ring quickly (cloth, carpet).")]
        [Input("Absorption", Editable = true)] public float Absorption = 0.3f;
        [Tooltip("How long it rings on, 0 to 1: 0 a dull thud, 1 a bell.")]
        [Input("Ring", Editable = true)] public float Ring = 0.3f;
        [Tooltip("How wet it is, 0 to 1: adds a splashy, slappy edge.")]
        [Input("Wetness", Editable = true)] public float Wetness;
        [Input("Hit", Editable = true)] public float Hit;
        [Editable, Tooltip("A Surface Material to sound like: its Hardness, Absorption, Wetness (and Ring, if it has that property) are used for the inputs that aren't wired.")]
        public SurfaceMaterial Material;
        [Editable, Tooltip("How many resonances ring (more: richer, costs a little more).")]
        public int Modes = 10;
        [Editable, Tooltip("Strike when the cue starts (otherwise only when Hit rises past 0.5).")]
        public bool HitAtStart = true;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            // Unwired inputs take the material's values (when there is one).
            float hardness = Hardness, absorption = Absorption, wetness = Wetness, ring = Ring;
            if (Material != null)
            {
                hardness = Material.Hardness;
                absorption = Material.Absorption;
                wetness = Material.Wetness;
                ring = Material.Get("Ring", RingOf(Material.Hardness, Material.Absorption));
            }
            // As long as the longest ring, for Auto length.
            builder.NoteSourceLength(RingTime(builder.IsConnected(this, "Ring") ? 1f : ring) + 0.1f);
            return new Processor(builder.SampleRate)
            {
                Strength = builder.Input(this, "Strength", Strength),
                Size = builder.Input(this, "Size", Size),
                Hardness = builder.Input(this, "Hardness", hardness),
                Absorption = builder.Input(this, "Absorption", absorption),
                Ring = builder.Input(this, "Ring", ring),
                Wetness = builder.Input(this, "Wetness", wetness),
                Hit = builder.Input(this, "Hit", Hit),
                ModeCount = Mathf.Clamp(Modes, 1, MaxModes),
                HitAtStart = HitAtStart,
            };
        }

        /// <summary>How long a material rings when it hasn't said: hard, unabsorbent things ring (metal, glass), others thunk.</summary>
        public static float RingOf(float hardness, float absorption)
            => Mathf.Clamp01((1f - absorption) * (1f - absorption) * hardness * hardness * hardness);

        /// <summary>Seconds the longest resonance takes to die away, for a Ring of 0 to 1.</summary>
        public static float RingTime(float ring) => Mathf.Lerp(0.04f, 3f, Mathf.Clamp01(ring) * Mathf.Clamp01(ring));

        // Where a struck thing's resonances sit, as multiples of its lowest: a bar or bell's spread-out ones (metal,
        // glass), and a plank or box's closer-packed ones (wood, plastic, stone).
        private static readonly float[] Spread = { 1f, 2.756f, 5.404f, 8.933f, 13.34f, 18.64f, 24.81f, 31.87f, 39.81f, 48.63f, 58.3f, 68.9f, 80.4f, 92.8f, 106f, 120f };
        private static readonly float[] Packed = { 1f, 1.59f, 2.14f, 2.65f, 3.16f, 3.71f, 4.32f, 4.94f, 5.53f, 6.17f, 6.84f, 7.5f, 8.2f, 8.95f, 9.7f, 10.5f };

        private sealed class Processor : SoundProcessor
        {
            public float[] Strength, Size, Hardness, Absorption, Ring, Wetness, Hit;
            public int ModeCount;
            public bool HitAtStart;

            private readonly int _rate;
            // Each resonance: a two-pole resonator (its pull, its fade, how much the strike drives it, its last two outputs).
            private readonly float[] _a1 = new float[MaxModes], _a2 = new float[MaxModes], _drive = new float[MaxModes], _w = new float[MaxModes];
            private readonly float[] _y1 = new float[MaxModes], _y2 = new float[MaxModes];
            // The strike itself (a short burst), and the splash.
            private readonly float[] _strike;
            private int _strikeLength, _strikeAt;
            private float _click, _splash, _splashFade, _splashLow, _splashHigh;
            private float _gain, _lastHit;
            private long _quietAfter;
            private bool _started;

            public Processor(int sampleRate)
            {
                _rate = sampleRate;
                _strike = new float[(int)(0.012f * sampleRate) + 2];
            }

            public override void Reset(SoundContext context)
            {
                Array.Clear(_y1, 0, _y1.Length);
                Array.Clear(_y2, 0, _y2.Length);
                _strikeAt = _strikeLength = 0;
                _splash = 0f;
                _lastHit = 0f;
                _quietAfter = 0;
                _started = false;
            }

            // Sets everything ringing, from the inputs at this sample.
            private void Strike(SoundContext context, int i)
            {
                var random = context.Random;
                float strength = Mathf.Max(0f, Strength[i]);
                float hardness = Mathf.Clamp01(Hardness[i]), absorption = Mathf.Clamp01(Absorption[i]);
                float ring = Mathf.Clamp01(Ring[i]), wetness = Mathf.Clamp01(Wetness[i]);
                float size = Mathf.Max(0.02f, Size[i]);
                float Vary(float amount) => 1f + ((float)random.NextDouble() * 2f - 1f) * amount;

                // The lowest resonance: smaller things ring higher.
                float lowest = Mathf.Clamp(160f / size, 30f, 4000f) * Vary(0.04f);
                // Hard, unabsorbent things have a bell's spread-out resonances; others a plank's.
                float metal = Mathf.Clamp01(hardness * (1f - absorption) * 1.3f - 0.2f);
                float longest = RingTime(ring);
                // A harder material, and a harder hit, rings the high resonances more.
                float tilt = Mathf.Lerp(1.8f, 0.5f, hardness) - 0.25f * Mathf.Clamp01(strength);
                float total = 0f;
                for (int k = 0; k < ModeCount; k++)
                {
                    float frequency = lowest * Mathf.Lerp(Packed[k], Spread[k], metal) * Vary(0.02f);
                    if (frequency >= _rate * 0.45f)
                    {
                        _drive[k] = 0f;
                        continue;
                    }
                    // Higher ones die sooner, the more so the more absorbent it is.
                    float seconds = longest / (1f + k * (0.25f + 2f * absorption));
                    float fade = Mathf.Exp(-6.91f / (Mathf.Max(0.005f, seconds) * _rate));
                    float w = 2f * Mathf.PI * frequency / _rate;
                    _a1[k] = 2f * fade * Mathf.Cos(w);
                    _a2[k] = -fade * fade;
                    _w[k] = w;
                    _drive[k] = Mathf.Sin(w) / Mathf.Pow(1f + k, tilt) * Vary(0.3f);
                }

                // The strike: a burst of noise, short and sharp for hard things, long and soft (a thud) for soft ones.
                _strikeLength = Mathf.Clamp((int)(Mathf.Lerp(0.01f, 0.0006f, hardness) * _rate), 2, _strike.Length);
                float low = 0f, smooth = Mathf.Lerp(0.08f, 0.9f, hardness);
                for (int n = 0; n < _strikeLength; n++)
                {
                    float shape = Mathf.Sin(Mathf.PI * (n + 0.5f) / _strikeLength);
                    low += ((float)random.NextDouble() * 2f - 1f - low) * smooth;
                    _strike[n] = low * shape;
                }
                // The burst at full height (the click's level).
                float burst = 0f;
                for (int n = 0; n < _strikeLength; n++)
                    burst = Mathf.Max(burst, Mathf.Abs(_strike[n]));
                if (burst > 1e-6f)
                    for (int n = 0; n < _strikeLength; n++)
                        _strike[n] /= burst;
                // Every strike as loud as the next, whatever it's made of: each resonance rings as much as the burst
                // drives it at its pitch (a long soft burst drives the low ones far harder), so share that out to 1.
                for (int k = 0; k < ModeCount; k++)
                {
                    if (_drive[k] == 0f) continue;
                    float re = 0f, im = 0f;
                    for (int n = 0; n < _strikeLength; n++)
                    {
                        re += _strike[n] * Mathf.Cos(_w[k] * n);
                        im -= _strike[n] * Mathf.Sin(_w[k] * n);
                    }
                    total += Mathf.Abs(_drive[k]) * Mathf.Sqrt(re * re + im * im) / Mathf.Max(1e-4f, Mathf.Sin(_w[k]));
                }
                if (total > 1e-6f)
                    for (int k = 0; k < ModeCount; k++)
                        _drive[k] /= total;
                _strikeAt = 0;
                _click = hardness * 0.35f;
                _splash = wetness;
                _splashFade = Mathf.Exp(-1f / (Mathf.Lerp(0.06f, 0.2f, wetness) * _rate));
                _gain = Mathf.Min(2f, strength) * 1.6f;
                _quietAfter = context.SamplePosition + i + (long)((longest * 1.2f + 0.25f) * _rate);
            }

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                var random = context.Random;
                for (int i = 0; i < count; i++)
                {
                    // A strike at the start, and whenever Hit rises past 0.5.
                    float hit = Hit[i];
                    if ((!_started && HitAtStart) || (hit >= 0.5f && _lastHit < 0.5f))
                        Strike(context, i);
                    _started = true;
                    _lastHit = hit;

                    if (context.SamplePosition + i > _quietAfter)
                    {
                        o[i] = 0f;
                        continue;
                    }
                    float excite = _strikeAt < _strikeLength ? _strike[_strikeAt++] : 0f;
                    float sum = 0f;
                    for (int k = 0; k < ModeCount; k++)
                    {
                        float y = _a1[k] * _y1[k] + _a2[k] * _y2[k] + _drive[k] * excite;
                        _y2[k] = _y1[k];
                        _y1[k] = y;
                        sum += y;
                    }
                    sum += excite * _click;
                    // The splash: a fading hiss in the middle of the range.
                    if (_splash > 1e-4f)
                    {
                        float noise = (float)random.NextDouble() * 2f - 1f;
                        _splashLow += (noise - _splashLow) * 0.35f;
                        _splashHigh += (_splashLow - _splashHigh) * 0.05f;
                        sum += (_splashLow - _splashHigh) * _splash * 0.5f;
                        _splash *= _splashFade;
                    }
                    // Soft-clipped, so a very hard hit saturates rather than distorting harshly.
                    float x = sum * _gain;
                    o[i] = x / (1f + Mathf.Abs(x));
                }
            }
        }
    }
}
