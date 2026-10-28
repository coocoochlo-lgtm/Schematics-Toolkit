using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// What every Play node shares: the clip, where it plays (Target, else the object running the graph), volume,
    /// pitch, how 3D it is, and looping. Each node says what kind of audio it plays.
    /// </summary>
    [Serializable]
    public abstract class PlayClipNode : SchematicActionNode
    {
        [Tooltip("The audio file to play.")]
        [Input("Clip")]
        public AudioClip Clip;

        [Input("Target")]
        public GameObject Target;

        [Input("Volume", Editable = true)]
        [Range(0f, 1f)]
        public float Volume = 1f;

        [Tooltip("How high it plays: 1 as recorded, 2 an octave up (and faster), 0.5 an octave down.")]
        [Input("Pitch", Editable = true)]
        [Range(0.1f, 3f)]
        public float Pitch = 1f;

        [Tooltip("How 3D it sounds: 1 placed in the world (panned and fading with distance), 0 flat and centred.")]
        [Input("Spatial Blend", Editable = true)]
        [Range(0f, 1f)]
        public float SpatialBlend = 1f;

        [Tooltip("Repeat it until it's stopped.")]
        [Editable]
        public bool Loop;

        /// <summary>The kind of audio this plays (its mixer group and volume settings).</summary>
        protected abstract AudioType TypeFor(AudioClip clip);

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var clip = GetInputValue(nameof(Clip), Clip);
            if (clip == null)
            {
                Debug.LogWarning($"[{Name}] No AudioClip is assigned or connected.", instance);
                return;
            }

            var target = GetInputValue(nameof(Target), Target);
            if (target == null)
                target = instance;

            AudioManager.Play(
                clip,
                TypeFor(clip),
                target,
                volume: GetInputValue(nameof(Volume), Volume),
                pitch: GetInputValue(nameof(Pitch), Pitch),
                spatialBlend: GetInputValue(nameof(SpatialBlend), SpatialBlend),
                loop: Loop);
        }
    }

    /// <summary>Plays a clip as a sound effect. Dropping an SFX clip into a graph makes one.</summary>
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Play SFX", Path = "Audio"), Tags("Default")]
    [AssetNode(typeof(AudioClip), Tags = new[] { "SFX" }, Title = "Play SFX: {0}")]
    public class PlaySFX : PlayClipNode
    {
        protected override AudioType TypeFor(AudioClip clip) => AudioType.SoundEffect;
    }

    /// <summary>Plays a clip as music (looping and not 3D to start with). Dropping a Music clip into a graph makes one.</summary>
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Play Music", Path = "Audio"), Tags("Default")]
    [AssetNode(typeof(AudioClip), Tags = new[] { "Music" }, Title = "Play Music: {0}")]
    public class PlayMusic : PlayClipNode
    {
        public PlayMusic()
        {
            Loop = true;
            SpatialBlend = 0f;
        }

        protected override AudioType TypeFor(AudioClip clip) => AudioType.Music;
    }

    /// <summary>
    /// Plays a clip as whichever kind it is (Music or SFX, by its folder), or a kind picked here. Play SFX and Play
    /// Music are the usual choices; this one is kept for graphs that use it.
    /// </summary>
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Play Sound", Path = "Audio"), Tags("Default")]
    public class PlaySound : PlayClipNode
    {
        [Tooltip("Use the kind the clip is tagged as (Audio browser). Off: use Type.")]
        [Editable]
        public bool UseClipTagType = true;

        [Tooltip("What kind of sound it is: decides its mixer group and volume setting (effects, music, voice...).")]
        [Editable]
        public AudioType Type = AudioType.SoundEffect;

        protected override AudioType TypeFor(AudioClip clip) => UseClipTagType ? AudioData.TypeOf(clip) : Type;
    }

    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Stop Sound", Path = "Audio"), Tags("Default")]
    public class StopSound : SchematicActionNode
    {
        [Tooltip("The clip to stop.")]
        [Input("Clip")]
        public AudioClip Clip;

        [Input("Target")]
        public GameObject Target;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var clip = GetInputValue(nameof(Clip), Clip);
            if (clip == null)
            {
                Debug.LogWarning("[Stop Sound] No AudioClip is assigned or connected.", instance);
                return;
            }

            var target = GetInputValue(nameof(Target), Target);
            if (target == null)
                AudioManager.Stop(clip.name);
            else
                AudioManager.Stop(target, clip.name);
        }
    }
}
