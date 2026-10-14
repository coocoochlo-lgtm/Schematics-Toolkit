using Remedy.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Remedy.Audio
{
    [CreateAssetMenu(menuName = "Remedy Toolkit/Audio/Audio Data")]
    public class AudioData : SingletonData<AudioData>
    {
        [SerializeField]
        private SerializableDictionary<string, List<AudioDefinition>> _audioDefinitions = new();
        public static SerializableDictionary<string, List<AudioDefinition>> AudioDefinitions
        {
            get
            {
                foreach (var name in Instance._audioDefinitions.Keys.ToArray())
                {
                    if (Instance._audioDefinitions[name] == null)
                        continue;
                    foreach (var definition in Instance._audioDefinitions[name])
                        if (definition != null)
                            definition.Name = name;
                }
                return Instance._audioDefinitions;
            }
        }

        public static AudioType TypeOf(AudioClip clip)
        {
            if (clip == null)
                return AudioType.SoundEffect;
            foreach (var definitions in AudioDefinitions.Values)
            {
                if (definitions == null)
                    continue;
                var definition = definitions.FirstOrDefault(item => item != null && item.Clip == clip);
                if (definition != null)
                    return definition.Type;
            }
            return AudioType.SoundEffect;
        }

#if UNITY_EDITOR
        public bool RebuildDefinitions(IEnumerable<AudioClip> clips, Func<AudioClip, AudioType> typeOf)
        {
            var clipList = clips
                .Where(item => item != null)
                .Distinct()
                .OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var existing = _audioDefinitions.Values
                .Where(list => list != null)
                .SelectMany(list => list)
                .Where(definition => definition != null && definition.Clip != null)
                .GroupBy(definition => definition.Clip)
                .ToDictionary(group => group.Key, group => group.First());
            var managedClips = new HashSet<AudioClip>(clipList);
            var retained = existing.Values
                .Where(definition => !managedClips.Contains(definition.Clip))
                .ToList();

            bool changed = _audioDefinitions.Sum(pair => pair.Value?.Count ?? 0) != retained.Count + clipList.Count;
            foreach (var definition in retained)
            {
                if (definition.Name != definition.Clip.name
                    || !_audioDefinitions.TryGetValue(definition.Clip.name, out var definitions)
                    || definitions == null
                    || !definitions.Contains(definition))
                {
                    changed = true;
                    break;
                }
            }
            foreach (var clip in clipList)
            {
                var expectedType = typeOf != null ? typeOf(clip) : AudioType.SoundEffect;
                if (!existing.TryGetValue(clip, out var definition)
                    || definition.Name != clip.name
                    || definition.Type != expectedType
                    || !_audioDefinitions.TryGetValue(clip.name, out var definitions)
                    || definitions == null
                    || !definitions.Contains(definition))
                {
                    changed = true;
                    break;
                }
            }
            if (!changed)
                return false;

            _audioDefinitions.Clear();
            foreach (var definition in retained)
            {
                definition.Name = definition.Clip.name;
                AddDefinition(definition);
            }
            foreach (var clip in clipList)
            {
                if (!existing.TryGetValue(clip, out var definition))
                    definition = new AudioDefinition(clip);
                definition.Name = clip.name;
                definition.Clip = clip;
                definition.Type = typeOf != null ? typeOf(clip) : AudioType.SoundEffect;
                AddDefinition(definition);
            }

            EditorUtility.SetDirty(this);
            return true;

            void AddDefinition(AudioDefinition definition)
            {
                if (!_audioDefinitions.TryGetValue(definition.Name, out var definitions))
                {
                    definitions = new List<AudioDefinition>();
                    _audioDefinitions.Add(definition.Name, definitions);
                }
                definitions.Add(definition);
            }
        }
#endif

        [SerializeField]
        private GameObject _musicSourcePrefab;
        public static GameObject MusicSourcePrefab => Instance._musicSourcePrefab;

        [SerializeField]
        public GameObject _sfxSourcePrefab;
        public static GameObject SFXSourcePrefab => Instance._sfxSourcePrefab;
    }
}