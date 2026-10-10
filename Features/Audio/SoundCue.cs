using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// Plays an Audio Definition list from the Audio Data, on start, on collision/trigger, or when its Signal is sent.
    /// <see cref="Play()"/> and <see cref="Stop()"/> act on the selected instance.
    /// </summary>
    [SchematicBehaviorPath("Audio/Sound Cue")]
    public class SoundCue : SchematicBehavior
    {
        [Tooltip("Plays this Sound Cue, whatever the Signal carries.")]
        [InputSignal] public readonly SignalHandle Event;
        [InputSignal] public readonly SignalHandle StopEvent;

        [Tooltip("The AudioDefinition List to play, received from the AudioData Scriptable Object")]
        public readonly VariableHandle<string> AudioName = "";
        [Tooltip("Determines how the Audio Manager handles the playback of this Sound Cue. If set to Music, it will loop globally in the Music Mixer, otherwise it will be positioned and played in the SFX Mixer")]
        public readonly VariableHandle<AudioType> Type = AudioType.SoundEffect;
        [Tooltip("The way in which Audio Definitions are picked for Playback.")]
        public readonly VariableHandle<SelectionMode> SelectionMode = Audio.SelectionMode.Random;
        public readonly VariableHandle<float> SequenceDelay = 0f;
        [Tooltip("When an AudioDefinition is played")]
        public readonly VariableHandle<PlaybackMode> PlaybackMode = Audio.PlaybackMode.OnStart;
        [Tooltip("The target to play the sound at when the Collision is Entered/Exitted.")]
        public readonly VariableHandle<CollisionTargetMode> CollisionPlaybackTarget = CollisionTargetMode.HitPoint;
        [Tooltip("The target to play the sound at when the Trigger is Entered/Exitted.")]
        public readonly VariableHandle<TriggerTargetMode> TriggerPlaybackTarget = TriggerTargetMode.Other;

        [Tooltip("Multiplied into the Volume of the AudioClip that gets played by this Sound Cue")]
        public readonly VariableHandle<float> VolumeMultiplier = 1.0f;
        [Tooltip("Whether to Set the Spacial Blend of the Audio played by the Sound Cue")]
        public readonly VariableHandle<bool> SetSpacialBlend = false;
        [Tooltip("Sets the Spacial Blender of the AudioClip that gets played by this Sound Cue")]
        public readonly VariableHandle<float> SpacialBlend = 1.0f;

        [Tooltip("If above 0, will Delay the playback by that amount in seconds.")]
        public readonly VariableHandle<float> Delay = 0f;
        [Tooltip("When the Audio is told to Play, it will fade in instead of stopping immidiately")]
        public readonly VariableHandle<bool> FadeIn = false;
        public readonly VariableHandle<float> FadeInTime = 0.1f;
        [Tooltip("When the Audio is told to stop, it will fade out instead of stopping immidiately.")]
        public readonly VariableHandle<bool> FadeOut = false;
        public readonly VariableHandle<float> FadeOuttime = 0.1f;

        public bool IsMusic => Type.Value == AudioType.Music;

        /// <summary>The Audio Definitions named by AudioName, looked up when the object is created.</summary>
        private readonly VariableHandle<List<AudioDefinition>> _audioContent = (List<AudioDefinition>)null;

        protected override void OnStart(BehaviorInstance instance)
        {
            AudioData.AudioDefinitions.TryGetValue(AudioName.Value ?? "", out var content);
            _audioContent.Value = content;

            if (PlaybackMode.Value == Audio.PlaybackMode.OnStart || IsMusic)
                Play(instance);

            Event.Subscribe(() => Play(instance));
            StopEvent.Subscribe(() => Stop(instance));
        }

        /// <summary>Plays the selected Sound Cue.</summary>
        public void Play() => Play(Selected);

        /// <summary>Stops the selected Sound Cue.</summary>
        public void Stop() => Stop(Selected);

        public void Stop(BehaviorInstance instance)
        {
            AudioManager.Stop(instance.GameObject, AudioName[instance]);
        }

        public virtual void Play(BehaviorInstance instance, GameObject otherObject = null, Vector3 position = default)
        {
            var content = _audioContent[instance];
            if (content == null || content.Count == 0)
                return;

            var target = otherObject == null ? instance.GameObject : otherObject;
            var type = Type[instance];

            switch (SelectionMode[instance])
            {
                case Audio.SelectionMode.Simulaneous:
                    foreach (var definition in content)
                        AudioManager.Play(definition, type, target, position);
                    break;

                case Audio.SelectionMode.Sequential:
                case Audio.SelectionMode.LoopSequentially:
                    PlaySequence(instance, content, type, target, position, 0, SelectionMode[instance] == Audio.SelectionMode.LoopSequentially);
                    break;

                default:
                    AudioManager.Play(content[UnityEngine.Random.Range(0, content.Count)], type, target, position);
                    break;
            }
        }

        // Plays each definition when the previous one completes, while the instance is still the same spawn.
        private void PlaySequence(BehaviorInstance instance, List<AudioDefinition> content, AudioType type, GameObject target, Vector3 position, int index, bool loop)
        {
            if (index >= content.Count)
            {
                if (!loop) return;
                index = 0;
            }

            int uid = instance.UID;
            AudioManager.Play(content[index], type, target, position, () =>
            {
                if (instance.IsSpawnedAs(uid))
                    PlaySequence(instance, content, type, target, position, index + 1, loop);
            });
        }

        // Collision Playback
        protected override void OnCollisionEnter(BehaviorInstance instance, Collision collision)
        {
            if (PlaybackMode.Value == Audio.PlaybackMode.OnCollisionEnter && !IsMusic)
                PlayCollision(instance, collision);
        }
        protected override void OnCollisionExit(BehaviorInstance instance, Collision collision)
        {
            if (PlaybackMode.Value == Audio.PlaybackMode.OnCollisionExit && !IsMusic)
                PlayCollision(instance, collision);
        }
        private void PlayCollision(BehaviorInstance instance, Collision collision)
        {
            switch (CollisionPlaybackTarget.Value)
            {
                case CollisionTargetMode.HitPoint:
                    Play(instance, position: collision.contactCount > 0 ? collision.GetContact(0).point : instance.Transform.position);
                    break;
                case CollisionTargetMode.Other:
                    Play(instance, otherObject: collision.gameObject);
                    break;
                default:
                    Play(instance);
                    break;
            }
        }

        // Trigger Playback
        protected override void OnTriggerEnter(BehaviorInstance instance, Collider other)
        {
            if (PlaybackMode.Value == Audio.PlaybackMode.OnTriggerEnter && !IsMusic)
                PlayTrigger(instance, other);
        }
        protected override void OnTriggerExit(BehaviorInstance instance, Collider other)
        {
            if (PlaybackMode.Value == Audio.PlaybackMode.OnTriggerExit && !IsMusic)
                PlayTrigger(instance, other);
        }
        private void PlayTrigger(BehaviorInstance instance, Collider other)
        {
            if (TriggerPlaybackTarget.Value == TriggerTargetMode.Other)
                Play(instance, otherObject: other.gameObject);
            else
                Play(instance);
        }
    }

    public enum AudioType
    {
        Music,
        SoundEffect
    }

    public enum PlaybackMode
    {
        OnStart,
        OnCollisionEnter,
        OnCollisionExit,
        OnTriggerEnter,
        OnTriggerExit,
        External,
        ScriptableEvent
    }
    public enum SelectionMode
    {
        Random,
        Sequential,
        Simulaneous,
        LoopSequentially
    }

    public enum TriggerTargetMode
    {
        This,
        Other
    }

    public enum CollisionTargetMode
    {
        HitPoint,
        This,
        Other
    }

}