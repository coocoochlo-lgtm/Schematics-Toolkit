using System;
using System.Threading;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// Renders a Sound Cue as it's heard rather than ahead of time: its AudioSource plays a clip of ones (placed and
    /// faded by Unity as any sound) and this multiplies the cue's samples in, a block at a time from the audio thread.
    /// Used for cues that react as they play (a Trigger), so what fires them is heard at once, not after the second or
    /// so a streamed clip renders ahead.
    /// </summary>
    [AddComponentMenu("")]
    [ExecuteAlways] // the editor's preview plays these too
    internal sealed class SoundCueLiveRender : MonoBehaviour
    {
        /// <summary>How much later than this block sends to the world's spaces go: past what they're playing now.</summary>
        public const int SendLead = 2048;

        private SoundCueVoice _voice;
        private float[] _mono = new float[2048];

        public SoundCueVoice Voice => Volatile.Read(ref _voice);

        /// <summary>
        /// The last block's measure (for Log Direct Sound): how loud Unity made the clip of ones in each ear (its
        /// placement and falloff: about 0.7 each straight ahead, 1 and 0 hard to a side), and the cue's own level.
        /// </summary>
        public float PlacedLeft, PlacedRight, CueLevel;

        public void Begin(SoundCueVoice voice)
        {
            voice.Context.SendLead = SendLead;
            Volatile.Write(ref _voice, voice);
        }

        public void End() => Volatile.Write(ref _voice, null);

        private void OnAudioFilterRead(float[] data, int channels)
        {
            var voice = Volatile.Read(ref _voice);
            if (voice == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            int frames = data.Length / channels;
            if (_mono.Length < frames)
                _mono = new float[frames];
            // This block is heard now, so that's when the cue's sample here is heard (for its sends to rooms and walls).
            var context = voice.Context;
            context.HeardStart = AudioSettings.dspTime - context.SamplePosition / (double)Mathf.Max(1, context.SampleRate);
            Array.Clear(_mono, 0, frames);
            voice.Render(_mono, 0, frames);
            if (SoundCuePlayer.LogDirect && frames > 0)
            {
                float left = 0f, right = 0f, level = 0f;
                for (int f = 0; f < frames; f++)
                {
                    left += Math.Abs(data[f * channels]);
                    right += Math.Abs(data[f * channels + (channels > 1 ? 1 : 0)]);
                    level = Math.Max(level, Math.Abs(_mono[f]));
                }
                Volatile.Write(ref PlacedLeft, left / frames);
                Volatile.Write(ref PlacedRight, right / frames);
                Volatile.Write(ref CueLevel, level);
            }
            for (int f = 0; f < frames; f++)
            {
                float sample = _mono[f];
                for (int c = 0; c < channels; c++)
                    data[f * channels + c] *= sample; // the ones, placed and faded by Unity, times the cue
            }
        }
    }
}
