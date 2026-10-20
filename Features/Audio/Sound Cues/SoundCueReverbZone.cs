using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// The reverb Sound Cues' Shared Reverbs send to: Unity's Reverb Zones, which run once for every sound in range of
    /// them however many are playing. A scene's own Reverb Zones are used when it has any (place them to give rooms,
    /// caves and halls their sound); otherwise one scene-wide zone is made (a medium room), so shared reverb is always
    /// heard.
    /// </summary>
    public static class SoundCueReverbZone
    {
        private static AudioReverbZone _fallback;

        /// <summary>Makes the scene-wide zone if no Reverb Zone exists (Play Mode only).</summary>
        public static void Ensure()
        {
            if (!Application.isPlaying || _fallback != null)
                return;
            if (Object.FindFirstObjectByType<AudioReverbZone>() != null)
                return;
            var host = new GameObject("Sound Cue Reverb (scene-wide)");
            Object.DontDestroyOnLoad(host);
            _fallback = host.AddComponent<AudioReverbZone>();
            _fallback.reverbPreset = AudioReverbPreset.Room;
            // Everywhere: the listener is always inside it.
            _fallback.minDistance = 100000f;
            _fallback.maxDistance = 100001f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void NewSession() => _fallback = null;
    }
}
