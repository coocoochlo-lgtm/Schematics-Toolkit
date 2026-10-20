using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// Built voices kept for playing again, so a cue played often (footsteps, gunfire) is built once and then reused:
    /// playing it allocates nothing. A voice handed back waits a moment before it's reused, since the audio thread may
    /// still be finishing its last block (main thread only).
    /// </summary>
    public static class SoundCueVoicePool
    {
        /// <summary>The most idle voices kept for each cue.</summary>
        public static int MaxIdlePerCue = 16;

        // Long enough for the audio thread to be done with a voice's last block (a block is a few milliseconds).
        private const double CoolDownSeconds = 0.25;

        private static readonly Dictionary<(SoundCueGraph, int), Stack<SoundCueVoice>> _idle = new();
        private static readonly List<(SoundCueVoice voice, double readyAt)> _cooling = new();
        private static readonly Stopwatch _clock = Stopwatch.StartNew();

        /// <summary>The cue's state for telling built voices apart: its edit count in the editor, 0 in builds.</summary>
        public static int Version(SoundCueGraph cue)
        {
#if UNITY_EDITOR
            return cue != null ? UnityEditor.EditorUtility.GetDirtyCount(cue) : 0;
#else
            return 0;
#endif
        }

        /// <summary>A voice for a cue, ready to play from the start: an idle one if there is one, else a new one.</summary>
        public static SoundCueVoice Rent(SoundCueGraph cue, int sampleRate)
        {
            if (cue == null)
                return null;
            Promote();
            if (_idle.TryGetValue((cue, sampleRate), out var stack))
            {
                int version = Version(cue);
                while (stack.Count > 0)
                {
                    var voice = stack.Pop();
                    if (voice.BuiltVersion != version)
                        continue; // built before the cue was edited
                    voice.Restart();
                    return voice;
                }
            }
            return SoundCueVoice.Create(cue, sampleRate);
        }

        /// <summary>Hands a voice back once it's done (it's reused after a short wait).</summary>
        public static void Return(SoundCueVoice voice)
        {
            if (voice?.Cue == null)
                return;
            voice.Culled = true; // in case the audio thread reads it once more: silence, cheaply
            _cooling.Add((voice, _clock.Elapsed.TotalSeconds + CoolDownSeconds));
        }

        /// <summary>Builds voices for a cue ahead of time, so its first plays don't build them.</summary>
        public static void Prewarm(SoundCueGraph cue, int sampleRate, int count)
        {
            if (cue == null)
                return;
            var stack = StackFor(cue, sampleRate);
            int version = Version(cue);
            int ready = 0;
            foreach (var voice in stack)
                if (voice.BuiltVersion == version)
                    ready++;
            for (int i = ready; i < count && stack.Count < MaxIdlePerCue; i++)
                stack.Push(SoundCueVoice.Create(cue, sampleRate));
        }

        /// <summary>Forgets idle voices: one cue's, or every cue's.</summary>
        public static void Clear(SoundCueGraph cue = null)
        {
            if (cue == null)
            {
                _idle.Clear();
                _cooling.Clear();
                return;
            }
            var keys = new List<(SoundCueGraph, int)>();
            foreach (var key in _idle.Keys)
                if (key.Item1 == cue)
                    keys.Add(key);
            foreach (var key in keys)
                _idle.Remove(key);
            _cooling.RemoveAll(c => c.voice.Cue == cue);
        }

        /// <summary>How many idle voices are kept (all cues).</summary>
        public static int IdleCount
        {
            get
            {
                int count = 0;
                foreach (var stack in _idle.Values)
                    count += stack.Count;
                return count;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void NewSession() => Clear();

        // Voices whose wait is over become idle (keeping at most MaxIdlePerCue per cue).
        private static void Promote()
        {
            if (_cooling.Count == 0)
                return;
            double now = _clock.Elapsed.TotalSeconds;
            for (int i = _cooling.Count - 1; i >= 0; i--)
            {
                var (voice, readyAt) = _cooling[i];
                if (readyAt > now)
                    continue;
                _cooling.RemoveAt(i);
                var stack = StackFor(voice.Cue, voice.SampleRate);
                if (stack.Count < MaxIdlePerCue && voice.BuiltVersion == Version(voice.Cue))
                    stack.Push(voice);
            }
        }

        private static Stack<SoundCueVoice> StackFor(SoundCueGraph cue, int sampleRate)
        {
            if (!_idle.TryGetValue((cue, sampleRate), out var stack))
                _idle[(cue, sampleRate)] = stack = new Stack<SoundCueVoice>();
            return stack;
        }
    }

    public partial class SoundCueGraph
    {
        /// <summary>
        /// Gets the cue ready to play without a hitch (main thread): reads its audio files, makes its Generators' sounds
        /// and builds <paramref name="voices"/> voices ahead of time. Do it while a level loads (Preload Sound Cue
        /// node); players in a scene do it for their cue when they're created.
        /// </summary>
        public void Preload(int voices = 1)
        {
            foreach (var node in Nodes)
            {
                if (node is SoundWaveNode wave && wave.Clip != null)
                    SoundClipCache.Get(wave.Clip);
                else if (node is SoundGeneratorNode generator)
                    _ = generator.Rendered;
            }
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            SoundCueVoicePool.Prewarm(this, rate, Mathf.Max(0, voices));
        }
    }
}
