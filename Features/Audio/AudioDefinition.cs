using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// The properties to store for a generic audioDefinition.
    /// </summary>
    [System.Serializable]
    public class AudioDefinition
    {
        [HideInInspector]
        public string Name;

        public AudioClip Clip;

        public AudioType Type = AudioType.SoundEffect;

        [Range(0f, 1f)]
        public float Volume = 1f;
        [Range(.1f, 3f)]
        public float Pitch = 1f;
        [Range(0.0f, 1f)]
        public float SpatialBlend = 1f;

        public bool Loop;

        private AudioSource _source;
        public AudioSource Source
        {
            get
            {
                return _source;
            }
            set
            {
                _source = value;
                if (_source != null)
                {
                    _source.clip = this.Clip;
                    _source.volume = this.Volume;
                    _source.pitch = this.Pitch;
                    _source.loop = this.Loop;
                    _source.spatialBlend = this.SpatialBlend;
                }
            }
        }
        public bool DontDestroyOnComplete { get; set; }
        public GameObject ObjectToFollow { get; set; }
        public float Duration { get; set; }
        public float StartTime { get; set; }

        public Action OnComplete;

        public AudioDefinition() { }

        public AudioDefinition(AudioClip clip)
        {
            Name = clip != null ? clip.name : null;
            Clip = clip;
        }

        public AudioDefinition(AudioDefinition original, Action OnComplete = null)
        {
            this.Name = original.Name;
            this.Clip = original.Clip;
            this.Type = original.Type;
            this.Duration = original.Clip.length * 1;
            this.StartTime = Time.time;
            this.Volume = original.Volume;
            this.Pitch = original.Pitch;
            this.Loop = original.Loop;
            this.SpatialBlend = original.SpatialBlend;
            this.OnComplete = OnComplete;
        }

        public bool IsComplete()
        {
            if (Loop && Source != null && Source.isPlaying)
                return false;
            if (Time.time > StartTime + Duration)
            {
                OnComplete?.Invoke();
                return true;
            }
            return false;
        }
    }

}