using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Audio
{
    [Serializable]
    [Node(Name = "Play Sound", Path = "Audio"), Tags("Default")]
    public class PlaySound : SchematicActionNode
    {
        [Input("Clip")]
        public AudioClip Clip;

        [Input("Target")]
        public GameObject Target;

        [Input("Volume", Editable = true)]
        [Range(0f, 1f)]
        public float Volume = 1f;

        [Input("Pitch", Editable = true)]
        [Range(0.1f, 3f)]
        public float Pitch = 1f;

        [Input("Spatial Blend", Editable = true)]
        [Range(0f, 1f)]
        public float SpatialBlend = 1f;

        [Editable]
        public bool Loop;

        [Editable]
        public bool UseClipTagType = true;

        [Editable]
        public AudioType Type = AudioType.SoundEffect;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var clip = GetInputValue(nameof(Clip), Clip);
            if (clip == null)
            {
                Debug.LogWarning("[Play Sound] No AudioClip is assigned or connected.", instance);
                return;
            }

            var target = GetInputValue(nameof(Target), Target);
            if (target == null)
                target = instance;

            var type = UseClipTagType ? AudioData.TypeOf(clip) : Type;
            AudioManager.Play(
                clip,
                type,
                target,
                volume: GetInputValue(nameof(Volume), Volume),
                pitch: GetInputValue(nameof(Pitch), Pitch),
                spatialBlend: GetInputValue(nameof(SpatialBlend), SpatialBlend),
                loop: Loop);
        }
    }

    [Serializable]
    [Node(Name = "Stop Sound", Path = "Audio"), Tags("Default")]
    public class StopSound : SchematicActionNode
    {
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
