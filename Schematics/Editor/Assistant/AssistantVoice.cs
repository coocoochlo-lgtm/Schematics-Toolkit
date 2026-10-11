using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Assistant
{
    /// <summary>
    /// The assistant's ears and voice: records the microphone while push-to-talk is held (sent to Whisper on the
    /// server) and plays its replies in the chosen Kokoro voice, through the editor's audio preview.
    /// </summary>
    internal static class AssistantVoice
    {
        private const int WhisperRate = 16000;
        private const int MaxSeconds = 30;

        private static AudioClip _recording;
        private static string _device;
        private static int _rate;
        private static AudioClip _playing;

        public static bool IsRecording => _recording != null;

        #region Listening

        public static bool StartRecording()
        {
            if (_recording != null)
                return true;
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[Assistant] No microphone found.");
                return false;
            }
            _device = string.IsNullOrEmpty(AssistantSettings.Microphone) || Array.IndexOf(Microphone.devices, AssistantSettings.Microphone) < 0
                ? null : AssistantSettings.Microphone;
            Microphone.GetDeviceCaps(_device, out int min, out int max);
            // 0, 0 means any rate.
            _rate = min == 0 && max == 0 ? WhisperRate : Mathf.Clamp(WhisperRate, min, max);
            _recording = Microphone.Start(_device, false, MaxSeconds, _rate);
            return _recording != null;
        }

        /// <summary>Stops recording; the audio as 16 kHz mono WAV, or null if there was too little to send.</summary>
        public static byte[] StopRecording()
        {
            if (_recording == null)
                return null;
            int position = Microphone.GetPosition(_device);
            Microphone.End(_device);
            var clip = _recording;
            _recording = null;

            if (position <= 0)
                position = clip.samples;
            if (position < _rate / 4) // under a quarter second: a tap, not speech
            {
                UnityEngine.Object.DestroyImmediate(clip);
                return null;
            }

            var samples = new float[position * clip.channels];
            clip.GetData(samples, 0);
            int channels = clip.channels;
            UnityEngine.Object.DestroyImmediate(clip);
            return EncodeWav(Resample(Mono(samples, channels), _rate, WhisperRate), WhisperRate);
        }

        private static float[] Mono(float[] samples, int channels)
        {
            if (channels <= 1)
                return samples;
            var mono = new float[samples.Length / channels];
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                    sum += samples[i * channels + c];
                mono[i] = sum / channels;
            }
            return mono;
        }

        private static float[] Resample(float[] samples, int from, int to)
        {
            if (from == to || samples.Length == 0)
                return samples;
            var result = new float[(int)((long)samples.Length * to / from)];
            double step = (double)from / to;
            for (int i = 0; i < result.Length; i++)
            {
                double source = i * step;
                int index = (int)source;
                float t = (float)(source - index);
                float a = samples[Math.Min(index, samples.Length - 1)];
                float b = samples[Math.Min(index + 1, samples.Length - 1)];
                result[i] = a + (b - a) * t;
            }
            return result;
        }

        private static byte[] EncodeWav(float[] samples, int rate)
        {
            using var stream = new MemoryStream(44 + samples.Length * 2);
            using var writer = new BinaryWriter(stream);
            writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            writer.Write(36 + samples.Length * 2);
            writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(rate); writer.Write(rate * 2);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            writer.Write(samples.Length * 2);
            foreach (float sample in samples)
                writer.Write((short)Mathf.Clamp(sample * 32767f, -32768f, 32767f));
            writer.Flush();
            return stream.ToArray();
        }

        #endregion

        #region Speaking

        /// <summary>Says something in the chosen voice. Returns once it has finished speaking (or right away if it can't).</summary>
        public static async Task SayAsync(string text, CancellationToken cancellation = default)
        {
            if (string.IsNullOrWhiteSpace(text) || !AssistantSettings.Speak)
                return;
            try
            {
                if (!await AssistantServer.EnsureRunningAsync())
                    return;
                byte[] wav = await AssistantServer.SpeakAsync(text, AssistantSettings.Voice, AssistantSettings.SpeechSpeed, cancellation);
                var clip = wav != null ? DecodeWav(wav) : null;
                if (clip == null)
                    return;
                Play(clip);
                await Task.Delay(TimeSpan.FromSeconds(clip.length), cancellation);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Debug.LogWarning("[Assistant] Couldn't speak: " + exception.Message);
            }
        }

        public static void StopSpeaking()
        {
            AudioUtilCall("StopAllPreviewClips");
            if (_playing != null)
                UnityEngine.Object.DestroyImmediate(_playing);
            _playing = null;
        }

        private static void Play(AudioClip clip)
        {
            StopSpeaking();
            _playing = clip;
            // AudioUtil is internal; its preview player is what the Inspector's audio preview uses.
            if (!AudioUtilCall("PlayPreviewClip", clip, 0, false) && !AudioUtilCall("PlayPreviewClip", clip))
                Debug.LogWarning("[Assistant] Couldn't play audio in the editor on this Unity version.");
        }

        private static bool AudioUtilCall(string method, params object[] args)
        {
            var type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (type == null)
                return false;
            foreach (var candidate in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (candidate.Name != method || candidate.GetParameters().Length != args.Length)
                    continue;
                try
                {
                    candidate.Invoke(null, args);
                    return true;
                }
                catch (ArgumentException) { }
            }
            return false;
        }

        /// <summary>A clip from 16-bit PCM WAV bytes (what the server sends).</summary>
        private static AudioClip DecodeWav(byte[] wav)
        {
            if (wav.Length < 44)
                return null;
            int channels = BitConverter.ToInt16(wav, 22);
            int rate = BitConverter.ToInt32(wav, 24);
            int bits = BitConverter.ToInt16(wav, 34);
            if (bits != 16)
                return null;

            // Find the data chunk (usually right after the format chunk).
            int offset = 12;
            int length = 0;
            while (offset + 8 <= wav.Length)
            {
                int size = BitConverter.ToInt32(wav, offset + 4);
                if (wav[offset] == 'd' && wav[offset + 1] == 'a' && wav[offset + 2] == 't' && wav[offset + 3] == 'a')
                {
                    offset += 8;
                    length = Math.Min(size, wav.Length - offset);
                    break;
                }
                offset += 8 + size;
            }
            if (length <= 0)
                return null;

            var samples = new float[length / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(wav, offset + i * 2) / 32768f;
            var clip = AudioClip.Create("Assistant Voice", samples.Length / Math.Max(1, channels), Math.Max(1, channels), rate, false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        #endregion
    }
}
