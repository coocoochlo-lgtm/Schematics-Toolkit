using System;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// The settings of an sfxr sound (DrPetter's little retro sound generator): a wave, its pitch and how the pitch
    /// slides, an envelope, filters, a phaser, vibrato, arpeggio and repeat. Most values go 0 to 1 (slides and sweeps
    /// -1 to 1), as in sfxr, so presets and mutations from it carry over.
    /// </summary>
    [Serializable]
    public class SfxrSettings
    {
        public enum WaveShape { Square, Sawtooth, Sine, Noise }

        public WaveShape Wave = WaveShape.Square;

        [Range(0f, 1f), Tooltip("Pitch.")] public float BaseFrequency = 0.3f;
        [Range(0f, 1f), Tooltip("The sound stops when a falling pitch reaches this (0: never).")] public float FrequencyLimit;
        [Range(-1f, 1f), Tooltip("Pitch slides up (+) or down (-).")] public float FrequencySlide;
        [Range(-1f, 1f), Tooltip("The slide itself speeds up (+) or slows (-).")] public float DeltaSlide;

        [Range(0f, 1f), Tooltip("Square wave only: how thin the pulse is.")] public float Duty;
        [Range(-1f, 1f), Tooltip("Square wave only: the duty changing over time.")] public float DutySweep;

        [Range(0f, 1f), Tooltip("How far the pitch wobbles.")] public float VibratoDepth;
        [Range(0f, 1f), Tooltip("How fast the pitch wobbles.")] public float VibratoSpeed;

        [Range(0f, 1f), Tooltip("Fade-in time.")] public float Attack;
        [Range(0f, 1f), Tooltip("How long it holds at full volume.")] public float Sustain = 0.3f;
        [Range(0f, 1f), Tooltip("An extra kick at the start of the sustain.")] public float Punch;
        [Range(0f, 1f), Tooltip("Fade-out time.")] public float Decay = 0.4f;

        [Range(0f, 1f), Tooltip("Low-pass filter: lower is duller (1: off).")] public float LowPassCutoff = 1f;
        [Range(-1f, 1f), Tooltip("The low-pass cutoff moving over time.")] public float LowPassSweep;
        [Range(0f, 1f), Tooltip("Low-pass filter resonance.")] public float LowPassResonance;
        [Range(0f, 1f), Tooltip("High-pass filter: higher is thinner (0: off).")] public float HighPassCutoff;
        [Range(-1f, 1f), Tooltip("The high-pass cutoff moving over time.")] public float HighPassSweep;

        [Range(-1f, 1f), Tooltip("Phaser: a copy of the sound mixed in slightly late (0: off).")] public float PhaserOffset;
        [Range(-1f, 1f), Tooltip("The phaser's delay changing over time.")] public float PhaserSweep;

        [Range(0f, 1f), Tooltip("Restarts the pitch and arpeggio this often (0: never).")] public float RepeatSpeed;
        [Range(0f, 1f), Tooltip("How soon the arpeggio jumps (0: never).")] public float ArpeggioSpeed;
        [Range(-1f, 1f), Tooltip("How far the arpeggio jumps the pitch: up (+) or down (-).")] public float ArpeggioChange;

        [Range(0f, 1f)] public float Volume = 0.5f;

        [Tooltip("Noise and presets use this, so the same seed always makes the same sound.")]
        public int Seed = 1;

        public SfxrSettings Clone() => (SfxrSettings)MemberwiseClone();

        /// <summary>A number that changes whenever any setting does (to know when to make the sound again).</summary>
        public int Signature()
        {
            unchecked
            {
                int h = 17;
                foreach (var field in typeof(SfxrSettings).GetFields())
                    h = h * 31 + (field.GetValue(this)?.GetHashCode() ?? 0);
                return h;
            }
        }

        #region Presets (as in sfxr)

        public enum Preset { PickupCoin, LaserShoot, Explosion, PowerUp, HitHurt, Jump, BlipSelect, Random }

        /// <summary>Settings for a kind of sound, picked at random within it (as sfxr's buttons do).</summary>
        public static SfxrSettings FromPreset(Preset preset, int seed)
        {
            var r = new System.Random(seed);
            float Frnd(float range) => (float)r.NextDouble() * range;
            bool Coin() => r.Next(2) == 1;
            int Rnd(int n) => r.Next(n + 1);

            var s = new SfxrSettings { Seed = seed };
            switch (preset)
            {
                case Preset.PickupCoin:
                    s.BaseFrequency = 0.4f + Frnd(0.5f);
                    s.Sustain = Frnd(0.1f);
                    s.Decay = 0.1f + Frnd(0.4f);
                    s.Punch = 0.3f + Frnd(0.3f);
                    if (Coin())
                    {
                        s.ArpeggioSpeed = 0.5f + Frnd(0.2f);
                        s.ArpeggioChange = 0.2f + Frnd(0.4f);
                    }
                    break;

                case Preset.LaserShoot:
                    s.Wave = (WaveShape)Rnd(2);
                    if (s.Wave == WaveShape.Sine && Coin())
                        s.Wave = (WaveShape)Rnd(1);
                    s.BaseFrequency = 0.5f + Frnd(0.5f);
                    s.FrequencyLimit = Mathf.Max(0.2f, s.BaseFrequency - 0.2f - Frnd(0.6f));
                    s.FrequencySlide = -0.15f - Frnd(0.2f);
                    if (Rnd(2) == 0)
                    {
                        s.BaseFrequency = 0.3f + Frnd(0.6f);
                        s.FrequencyLimit = Frnd(0.1f);
                        s.FrequencySlide = -0.35f - Frnd(0.3f);
                    }
                    if (Coin())
                    {
                        s.Duty = Frnd(0.5f);
                        s.DutySweep = Frnd(0.2f);
                    }
                    else
                    {
                        s.Duty = 0.4f + Frnd(0.5f);
                        s.DutySweep = -Frnd(0.7f);
                    }
                    s.Sustain = 0.1f + Frnd(0.2f);
                    s.Decay = Frnd(0.4f);
                    if (Coin())
                        s.Punch = Frnd(0.3f);
                    if (Rnd(2) == 0)
                    {
                        s.PhaserOffset = Frnd(0.2f);
                        s.PhaserSweep = -Frnd(0.2f);
                    }
                    if (Coin())
                        s.HighPassCutoff = Frnd(0.3f);
                    break;

                case Preset.Explosion:
                    s.Wave = WaveShape.Noise;
                    if (Coin())
                    {
                        s.BaseFrequency = 0.1f + Frnd(0.4f);
                        s.FrequencySlide = -0.1f + Frnd(0.4f);
                    }
                    else
                    {
                        s.BaseFrequency = 0.2f + Frnd(0.7f);
                        s.FrequencySlide = -0.2f - Frnd(0.2f);
                    }
                    s.BaseFrequency *= s.BaseFrequency;
                    if (Rnd(4) == 0)
                        s.FrequencySlide = 0f;
                    if (Rnd(2) == 0)
                        s.RepeatSpeed = 0.3f + Frnd(0.5f);
                    s.Sustain = 0.1f + Frnd(0.3f);
                    s.Decay = Frnd(0.5f);
                    if (!Coin())
                    {
                        s.PhaserOffset = -0.3f + Frnd(0.9f);
                        s.PhaserSweep = -Frnd(0.3f);
                    }
                    s.Punch = 0.2f + Frnd(0.6f);
                    if (Coin())
                    {
                        s.VibratoDepth = Frnd(0.7f);
                        s.VibratoSpeed = Frnd(0.6f);
                    }
                    if (Rnd(2) == 0)
                    {
                        s.ArpeggioSpeed = 0.6f + Frnd(0.3f);
                        s.ArpeggioChange = 0.8f - Frnd(1.6f);
                    }
                    break;

                case Preset.PowerUp:
                    if (Coin())
                        s.Wave = WaveShape.Sawtooth;
                    else
                        s.Duty = Frnd(0.6f);
                    s.BaseFrequency = 0.2f + Frnd(0.3f);
                    if (Coin())
                    {
                        s.FrequencySlide = 0.1f + Frnd(0.4f);
                        s.RepeatSpeed = 0.4f + Frnd(0.4f);
                    }
                    else
                    {
                        s.FrequencySlide = 0.05f + Frnd(0.2f);
                        if (Coin())
                        {
                            s.VibratoDepth = Frnd(0.7f);
                            s.VibratoSpeed = Frnd(0.6f);
                        }
                    }
                    s.Sustain = Frnd(0.4f);
                    s.Decay = 0.1f + Frnd(0.4f);
                    break;

                case Preset.HitHurt:
                    s.Wave = (WaveShape)Rnd(2);
                    if (s.Wave == WaveShape.Sine)
                        s.Wave = WaveShape.Noise;
                    if (s.Wave == WaveShape.Square)
                        s.Duty = Frnd(0.6f);
                    s.BaseFrequency = 0.2f + Frnd(0.6f);
                    s.FrequencySlide = -0.3f - Frnd(0.4f);
                    s.Sustain = Frnd(0.1f);
                    s.Decay = 0.1f + Frnd(0.2f);
                    if (Coin())
                        s.HighPassCutoff = Frnd(0.3f);
                    break;

                case Preset.Jump:
                    s.Duty = Frnd(0.6f);
                    s.BaseFrequency = 0.3f + Frnd(0.3f);
                    s.FrequencySlide = 0.1f + Frnd(0.2f);
                    s.Sustain = 0.1f + Frnd(0.3f);
                    s.Decay = 0.1f + Frnd(0.2f);
                    if (Coin())
                        s.HighPassCutoff = Frnd(0.3f);
                    if (Coin())
                        s.LowPassCutoff = 1f - Frnd(0.6f);
                    break;

                case Preset.BlipSelect:
                    s.Wave = (WaveShape)Rnd(1);
                    if (s.Wave == WaveShape.Square)
                        s.Duty = Frnd(0.6f);
                    s.BaseFrequency = 0.2f + Frnd(0.4f);
                    s.Sustain = 0.1f + Frnd(0.1f);
                    s.Decay = Frnd(0.2f);
                    s.HighPassCutoff = 0.1f;
                    break;

                default: // Random: everything (sfxr's "Randomize")
                    float Pow3(float x) => x * x * x;
                    float Signed() => Frnd(2f) - 1f;
                    s.Wave = (WaveShape)Rnd(3);
                    s.BaseFrequency = Coin() ? Pow3(Signed()) + 0.5f : Mathf.Pow(Frnd(1f), 2f);
                    s.FrequencySlide = Pow3(Signed());
                    if (s.BaseFrequency > 0.7f && s.FrequencySlide > 0.2f) s.FrequencySlide = -s.FrequencySlide;
                    if (s.BaseFrequency < 0.2f && s.FrequencySlide < -0.05f) s.FrequencySlide = -s.FrequencySlide;
                    s.DeltaSlide = Pow3(Signed());
                    s.Duty = Signed();
                    s.DutySweep = Pow3(Signed());
                    s.VibratoDepth = Pow3(Signed());
                    s.VibratoSpeed = Signed();
                    s.Attack = Pow3(Signed());
                    s.Sustain = Mathf.Pow(Signed(), 2f);
                    s.Decay = Signed();
                    s.Punch = Mathf.Pow(Frnd(0.8f), 2f);
                    if (s.Attack + s.Sustain + s.Decay < 0.2f)
                    {
                        s.Sustain += 0.2f + Frnd(0.3f);
                        s.Decay += 0.2f + Frnd(0.3f);
                    }
                    s.LowPassResonance = Signed();
                    s.LowPassCutoff = 1f - Pow3(Frnd(1f));
                    s.LowPassSweep = Pow3(Signed());
                    if (s.LowPassCutoff < 0.1f && s.LowPassSweep < -0.05f) s.LowPassSweep = -s.LowPassSweep;
                    s.HighPassCutoff = Mathf.Pow(Frnd(1f), 5f);
                    s.HighPassSweep = Mathf.Pow(Signed(), 5f);
                    s.PhaserOffset = Pow3(Signed());
                    s.PhaserSweep = Pow3(Signed());
                    s.RepeatSpeed = Signed();
                    s.ArpeggioSpeed = Signed();
                    s.ArpeggioChange = Signed();
                    break;
            }
            s.Clamp();
            return s;
        }

        /// <summary>Nudges every setting a little at random (sfxr's "Mutate").</summary>
        public void Mutate(int seed)
        {
            var r = new System.Random(seed);
            float Nudge() => r.Next(2) == 1 ? (float)r.NextDouble() * 0.1f - 0.05f : 0f;
            BaseFrequency += Nudge(); FrequencySlide += Nudge(); DeltaSlide += Nudge();
            Duty += Nudge(); DutySweep += Nudge();
            VibratoDepth += Nudge(); VibratoSpeed += Nudge();
            Attack += Nudge(); Sustain += Nudge(); Decay += Nudge(); Punch += Nudge();
            LowPassResonance += Nudge(); LowPassCutoff += Nudge(); LowPassSweep += Nudge();
            HighPassCutoff += Nudge(); HighPassSweep += Nudge();
            PhaserOffset += Nudge(); PhaserSweep += Nudge();
            RepeatSpeed += Nudge(); ArpeggioSpeed += Nudge(); ArpeggioChange += Nudge();
            Clamp();
        }

        /// <summary>Keeps every setting in its range.</summary>
        public void Clamp()
        {
            static float U(float v) => Mathf.Clamp01(v);
            static float S(float v) => Mathf.Clamp(v, -1f, 1f);
            BaseFrequency = U(BaseFrequency); FrequencyLimit = U(FrequencyLimit);
            FrequencySlide = S(FrequencySlide); DeltaSlide = S(DeltaSlide);
            Duty = U(Duty); DutySweep = S(DutySweep);
            VibratoDepth = U(VibratoDepth); VibratoSpeed = U(VibratoSpeed);
            Attack = U(Attack); Sustain = U(Sustain); Punch = U(Punch); Decay = U(Decay);
            LowPassCutoff = U(LowPassCutoff); LowPassSweep = S(LowPassSweep); LowPassResonance = U(LowPassResonance);
            HighPassCutoff = U(HighPassCutoff); HighPassSweep = S(HighPassSweep);
            PhaserOffset = S(PhaserOffset); PhaserSweep = S(PhaserSweep);
            RepeatSpeed = U(RepeatSpeed); ArpeggioSpeed = U(ArpeggioSpeed); ArpeggioChange = S(ArpeggioChange);
            Volume = U(Volume);
        }

        #endregion
    }

    /// <summary>
    /// sfxr's synth: renders <see cref="SfxrSettings"/> into samples (mono, 44.1 kHz), the same way sfxr does, so its
    /// sounds come out the same.
    /// </summary>
    public static class SfxrSynth
    {
        public const int SampleRate = 44100;
        private const int MaxSeconds = 20;

        public static float[] Render(SfxrSettings p)
        {
            var random = new System.Random(p.Seed);
            float Frnd(float range) => (float)random.NextDouble() * range;

            // State (names as in sfxr).
            int phase = 0;
            double fperiod = 0, fmaxperiod = 0, fslide = 0, fdslide = 0;
            int period = 0;
            float squareDuty = 0, squareSlide = 0;
            double arpMod = 0;
            int arpTime = 0, arpLimit = 0;
            float fltp = 0, fltdp = 0, fltw = 0, fltwD = 0, fltdmp = 0, fltphp = 0, flthp = 0, flthpD = 0;
            float vibPhase = 0, vibSpeed = 0, vibAmp = 0;
            float envVol = 0;
            int envStage = 0, envTime = 0;
            var envLength = new int[3];
            float fphase = 0, fdphase = 0;
            int iphase = 0, ipp = 0;
            var phaserBuffer = new float[1024];
            var noiseBuffer = new float[32];
            int repTime = 0, repLimit = 0;

            void Reset(bool restart)
            {
                if (!restart)
                    phase = 0;
                fperiod = 100.0 / (p.BaseFrequency * p.BaseFrequency + 0.001);
                period = (int)fperiod;
                fmaxperiod = 100.0 / (p.FrequencyLimit * p.FrequencyLimit + 0.001);
                fslide = 1.0 - Math.Pow(p.FrequencySlide, 3.0) * 0.01;
                fdslide = -Math.Pow(p.DeltaSlide, 3.0) * 0.000001;
                squareDuty = 0.5f - p.Duty * 0.5f;
                squareSlide = -p.DutySweep * 0.00005f;
                arpMod = p.ArpeggioChange >= 0f
                    ? 1.0 - Math.Pow(p.ArpeggioChange, 2.0) * 0.9
                    : 1.0 + Math.Pow(p.ArpeggioChange, 2.0) * 10.0;
                arpTime = 0;
                arpLimit = (int)(Math.Pow(1f - p.ArpeggioSpeed, 2.0) * 20000 + 32);
                if (p.ArpeggioSpeed >= 1f)
                    arpLimit = 0;
                if (restart)
                    return;

                fltp = 0f;
                fltdp = 0f;
                fltw = Mathf.Pow(p.LowPassCutoff, 3f) * 0.1f;
                fltwD = 1f + p.LowPassSweep * 0.0001f;
                fltdmp = 5f / (1f + Mathf.Pow(p.LowPassResonance, 2f) * 20f) * (0.01f + fltw);
                if (fltdmp > 0.8f) fltdmp = 0.8f;
                fltphp = 0f;
                flthp = Mathf.Pow(p.HighPassCutoff, 2f) * 0.1f;
                flthpD = 1f + p.HighPassSweep * 0.0003f;
                vibPhase = 0f;
                vibSpeed = Mathf.Pow(p.VibratoSpeed, 2f) * 0.01f;
                vibAmp = p.VibratoDepth * 0.5f;
                envVol = 0f;
                envStage = 0;
                envTime = 0;
                envLength[0] = (int)(p.Attack * p.Attack * 100000f);
                envLength[1] = (int)(p.Sustain * p.Sustain * 100000f);
                envLength[2] = (int)(p.Decay * p.Decay * 100000f);
                fphase = Mathf.Pow(p.PhaserOffset, 2f) * 1020f;
                if (p.PhaserOffset < 0f) fphase = -fphase;
                fdphase = Mathf.Pow(p.PhaserSweep, 2f);
                if (p.PhaserSweep < 0f) fdphase = -fdphase;
                iphase = Math.Abs((int)fphase);
                ipp = 0;
                Array.Clear(phaserBuffer, 0, phaserBuffer.Length);
                for (int i = 0; i < noiseBuffer.Length; i++)
                    noiseBuffer[i] = Frnd(2f) - 1f;
                repTime = 0;
                repLimit = (int)(Math.Pow(1f - p.RepeatSpeed, 2.0) * 20000 + 32);
                if (p.RepeatSpeed <= 0f)
                    repLimit = 0;
            }

            Reset(false);

            int capacity = Math.Min(SampleRate * MaxSeconds, envLength[0] + envLength[1] + envLength[2] + 16);
            var output = new float[Math.Max(1, capacity)];
            int written = 0;
            const float masterVolume = 0.05f;

            while (written < output.Length)
            {
                repTime++;
                if (repLimit != 0 && repTime >= repLimit)
                {
                    repTime = 0;
                    Reset(true);
                }

                arpTime++;
                if (arpLimit != 0 && arpTime >= arpLimit)
                {
                    arpLimit = 0;
                    fperiod *= arpMod;
                }

                fslide += fdslide;
                fperiod *= fslide;
                if (fperiod > fmaxperiod)
                {
                    fperiod = fmaxperiod;
                    if (p.FrequencyLimit > 0f)
                        break; // the pitch fell to the limit: the sound ends
                }

                double rfperiod = fperiod;
                if (vibAmp > 0f)
                {
                    vibPhase += vibSpeed;
                    rfperiod = fperiod * (1.0 + Math.Sin(vibPhase) * vibAmp);
                }
                period = Math.Max(8, (int)rfperiod);
                squareDuty = Mathf.Clamp(squareDuty + squareSlide, 0f, 0.5f);

                envTime++;
                if (envTime > envLength[envStage])
                {
                    envTime = 0;
                    envStage++;
                    if (envStage == 3)
                        break; // the envelope is over
                }
                if (envStage == 0) envVol = envLength[0] > 0 ? (float)envTime / envLength[0] : 1f;
                if (envStage == 1) envVol = 1f + (1f - (envLength[1] > 0 ? (float)envTime / envLength[1] : 1f)) * 2f * p.Punch;
                if (envStage == 2) envVol = 1f - (envLength[2] > 0 ? (float)envTime / envLength[2] : 1f);

                fphase += fdphase;
                iphase = Math.Min(1023, Math.Abs((int)fphase));
                if (flthpD != 0f)
                    flthp = Mathf.Clamp(flthp * flthpD, 0.00001f, 0.1f);

                float sum = 0f;
                for (int si = 0; si < 8; si++) // 8x supersampling
                {
                    float sample = 0f;
                    phase++;
                    if (phase >= period)
                    {
                        phase %= period;
                        if (p.Wave == SfxrSettings.WaveShape.Noise)
                            for (int i = 0; i < noiseBuffer.Length; i++)
                                noiseBuffer[i] = Frnd(2f) - 1f;
                    }
                    float fp = (float)phase / period;
                    switch (p.Wave)
                    {
                        case SfxrSettings.WaveShape.Square: sample = fp < squareDuty ? 0.5f : -0.5f; break;
                        case SfxrSettings.WaveShape.Sawtooth: sample = 1f - fp * 2f; break;
                        case SfxrSettings.WaveShape.Sine: sample = Mathf.Sin(fp * 2f * Mathf.PI); break;
                        case SfxrSettings.WaveShape.Noise: sample = noiseBuffer[phase * 32 / period]; break;
                    }

                    // Low-pass filter.
                    float pp = fltp;
                    fltw = Mathf.Clamp(fltw * fltwD, 0f, 0.1f);
                    if (p.LowPassCutoff < 1f)
                    {
                        fltdp += (sample - fltp) * fltw;
                        fltdp -= fltdp * fltdmp;
                    }
                    else
                    {
                        fltp = sample;
                        fltdp = 0f;
                    }
                    fltp += fltdp;

                    // High-pass filter.
                    fltphp += fltp - pp;
                    fltphp -= fltphp * flthp;
                    sample = fltphp;

                    // Phaser.
                    phaserBuffer[ipp & 1023] = sample;
                    sample += phaserBuffer[(ipp - iphase + 1024) & 1023];
                    ipp = (ipp + 1) & 1023;

                    sum += sample * envVol;
                }

                // sfxr's own output, with the x4 gain it adds when exporting (so it's as loud as sfxr's .wav files).
                float value = sum / 8f * masterVolume * 2f * p.Volume * 4f;
                output[written++] = Mathf.Clamp(value, -1f, 1f);
            }

            if (written < output.Length)
                Array.Resize(ref output, Math.Max(1, written));
            return output;
        }
    }
}
