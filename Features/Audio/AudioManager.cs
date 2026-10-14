using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Audio
{
    public class AudioManager : Singleton<AudioManager>
    {
        private GameObject _audioSourceCollection;
        public static GameObject AudioSourceCollection => Instance._audioSourceCollection ??= new("Audio Source Collection");

        private SerializableDictionary<string, List<AudioDefinition>> _audioDefinitions => AudioData.AudioDefinitions;

        // Sources

        private AudioSource _musicSource;
        public static AudioSource MusicSource
        {
            get
            {
                AudioSource source = Instance._musicSource;
                if (source == null)
                {
                    source = Instantiate(AudioData.MusicSourcePrefab).GetComponent<AudioSource>();
                    source.transform.parent = Instance.transform;
                }
                return source;
            }
        }

        private Dictionary<string, List<AudioSource>> _sfxSources;
        public static Dictionary<string, List<AudioSource>> SFXSources => Instance._sfxSources ??= new();

        private List<AudioDefinition> _activeAudioDefinitions = new List<AudioDefinition>();
        private Dictionary<GameObject, Dictionary<string, AudioSource>> _objectSources = new();

        private void Update()
        {
            var clone = _activeAudioDefinitions.ToArray();
            foreach (var audioDefinition in clone)
            {
                if (audioDefinition.IsComplete() && audioDefinition.Source != null && !audioDefinition.DontDestroyOnComplete)
                {
                    Destroy(audioDefinition.Source.gameObject);
                    _activeAudioDefinitions.Remove(audioDefinition);
                }
                else if (audioDefinition.Source == null)
                {
                    _activeAudioDefinitions.Remove(audioDefinition);
                }
            }

            foreach (var obj in new List<KeyValuePair<GameObject, Dictionary<string, AudioSource>>>(_objectSources))
            {
                if (obj.Key == null || !obj.Key.activeSelf)
                {
                    foreach (var source in obj.Value.Values)
                    {
                        foreach (var def in _activeAudioDefinitions)
                        {
                            if (def.Source == null || def.Source == source)
                            {
                                if (def.Source == source && def.Loop && source != null)
                                    source.Stop();
                                def.DontDestroyOnComplete = false;
                            }
                        }
                    }

                    _objectSources.Remove(obj.Key);
                }
                else
                {
                    foreach (var source in obj.Value.Values)
                    {
                        source.transform.position = obj.Key.transform.position;
                    }
                }
            }
        }

        /// <summary>
        /// Uber function for playing Music and SFX. Called primarily from SoundCues, which give a modular interface to how sound playback occurs.
        /// </summary>
        /// <param name="definition"></param>
        /// <param name="type"></param>
        /// <param name="sourceObject"></param>
        /// <param name="position"></param>
        /// <param name="onComplete"></param>
        public static AudioSource Play(AudioDefinition definition, AudioType type, GameObject sourceObject = null, Vector3 position = default, Action onComplete = null)
        {
            if (definition == null) return null;
            AudioDefinition definitionInstance = new AudioDefinition(definition, onComplete);

            //Music
            if (type == AudioType.Music)
            {
                Instance._activeAudioDefinitions.Add(definitionInstance);
                definitionInstance.Source = MusicSource;
                definitionInstance.Source.Play();
            }
            else
            {
                // Attach to Object
                if (sourceObject != null && position == default)
                {
                    Instance._activeAudioDefinitions.Add(definitionInstance);

                    if (Instance._objectSources.ContainsKey(sourceObject) && Instance._objectSources[sourceObject].ContainsKey(definition.Name))
                        definitionInstance.Source = Instance._objectSources[sourceObject][definition.Name];
                    else
                    {
                        var source = Instantiate(AudioData.SFXSourcePrefab);
                        source.name = $"SFX_{definition.Name}";
                        source.transform.position = sourceObject.transform.position;
                        definitionInstance.Source = source.GetComponent<AudioSource>();

                        if (!Instance._objectSources.ContainsKey(sourceObject))
                            Instance._objectSources.Add(sourceObject, new());
                        Instance._objectSources[sourceObject].Add(definition.Name, definitionInstance.Source);
                    }

                    definitionInstance.ObjectToFollow = sourceObject;
                    definitionInstance.DontDestroyOnComplete = true;
                    if (definitionInstance.Loop)
                        definitionInstance.Source.Play();
                    else
                        definitionInstance.Source.PlayOneShot(definitionInstance.Clip);
                }
                // Positional
                else
                {
                    Instance._activeAudioDefinitions.Add(definitionInstance);

                    var source = Instantiate(AudioData.SFXSourcePrefab);
                    source.transform.position = position;
                    definitionInstance.Source = source.GetComponent<AudioSource>();
                    if (definitionInstance.Loop)
                        definitionInstance.Source.Play();
                    else
                        definitionInstance.Source.PlayOneShot(definitionInstance.Clip);
                }
            }

            definitionInstance.Source.transform.parent = AudioSourceCollection.transform;
            return definitionInstance.Source;
        }

        public static AudioSource Play(
            AudioClip clip,
            AudioType type = AudioType.SoundEffect,
            GameObject sourceObject = null,
            Vector3 position = default,
            float volume = 1f,
            float pitch = 1f,
            float spatialBlend = 1f,
            bool loop = false,
            Action onComplete = null)
        {
            if (clip == null)
                return null;

            var definition = new AudioDefinition(clip)
            {
                Type = type,
                Volume = volume,
                Pitch = pitch,
                SpatialBlend = spatialBlend,
                Loop = loop
            };
            return Play(definition, type, sourceObject, position, onComplete);
        }

        /// <summary>
        /// Returns true if the given sfxName is playing
        /// </summary>
        /// <param name="sfxName"></param>
        /// <returns></returns>
        public static bool IsPlaying(string sfxName)
        {
            bool val = false;

            foreach (var def in Instance._activeAudioDefinitions)
                if (def.Name == sfxName)
                    val = true;

            return val;
        }

        /// <summary>
        /// Stops the Audio Source with the sound with the given name
        /// </summary>
        /// <param name="sfxName"></param>
        public static void Stop(string sfxName)
        {
            foreach (var def in Instance._activeAudioDefinitions)
                if (def.Name == sfxName)
                    def.Source.Stop();
        }

        public static void Stop(GameObject sourceObject, string sfxName)
        {
            foreach (var def in Instance._activeAudioDefinitions)
                if (def.Name == sfxName && def.ObjectToFollow == sourceObject)
                    def.Source.Stop();
        }
    }
}