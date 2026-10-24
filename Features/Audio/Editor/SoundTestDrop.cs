using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;
using Remedy.Schematics.Templates;
using UnityEditor;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// Audio dropped into the Scene view or the Hierarchy (audio files and Sound Cues) becomes a Schematic Object that
    /// plays it On Create, for trying sounds out quickly: it plays when Play Mode starts (at once, dropped while
    /// playing). Each sound gets one such prefab, in <see cref="Folder"/>, made the first time and reused after; its
    /// graph is an ordinary one (On Create into Play SFX, Play Music or Play Sound Cue), free to change. Hold Alt while
    /// dropping for Unity's own (an object with an Audio Source).
    /// </summary>
    [InitializeOnLoad]
    public static class SoundTestDrop
    {
        /// <summary>Where the sound test prefabs go.</summary>
        public const string Folder = "Assets/Content/Audio/Sound Tests";

        static SoundTestDrop()
        {
            // (Not while Unity is still starting up: a drop handler added then can leave it stuck on "Hold On...".)
            // Handlers added last are asked first, so these come before Unity's own.
            EditorApplication.delayCall += () =>
            {
#if UNITY_6000_2_OR_NEWER
                DragAndDrop.AddDropHandlerV2(OnSceneDrop);
                DragAndDrop.AddDropHandlerV2(OnHierarchyDrop);
#else
                DragAndDrop.AddDropHandler(OnSceneDrop);
                DragAndDrop.AddDropHandler(OnHierarchyDrop);
#endif
            };
        }

        // The sounds being dragged (null: something else is, or not only sounds).
        private static List<Object> Dragged()
        {
            var dragged = DragAndDrop.objectReferences;
            if (dragged == null || dragged.Length == 0 || (Event.current != null && Event.current.alt))
                return null;
            foreach (var asset in dragged)
                if (!(asset is AudioClip) && !(asset is SoundCueGraph))
                    return null;
            return dragged.ToList();
        }

        // The Scene view: where it's dropped (Unity works out the point on what's under the pointer).
        private static DragAndDropVisualMode OnSceneDrop(Object dropUpon, Vector3 worldPosition, Vector2 viewportPosition, Transform parent, bool perform)
        {
            var sounds = Dragged();
            if (sounds == null)
                return DragAndDropVisualMode.None;
            if (perform)
                Place(sounds, parent, worldPosition);
            return DragAndDropVisualMode.Copy;
        }

#if UNITY_6000_2_OR_NEWER
        private static DragAndDropVisualMode OnHierarchyDrop(EntityId target, HierarchyDropFlags flags, Transform parent, bool perform)
            => HierarchyDrop(EditorUtility.EntityIdToObject(target), flags, parent, perform);
#else
        private static DragAndDropVisualMode OnHierarchyDrop(int target, HierarchyDropFlags flags, Transform parent, bool perform)
            => HierarchyDrop(EditorUtility.InstanceIDToObject(target), flags, parent, perform);
#endif

        private static DragAndDropVisualMode HierarchyDrop(Object target, HierarchyDropFlags flags, Transform parent, bool perform)
        {
            var sounds = Dragged();
            if (sounds == null)
                return DragAndDropVisualMode.None;
            if (perform)
            {
                // Dropped on an object: under it, where it is. Between objects or below them: where the Scene view looks.
                var on = (flags & HierarchyDropFlags.DropUpon) != 0 && target is GameObject upon ? upon.transform : parent;
                var view = SceneView.lastActiveSceneView;
                Place(sounds, on, on != null ? on.position : view != null ? view.pivot : Vector3.zero);
            }
            return DragAndDropVisualMode.Copy;
        }

        // Each sound's test object, a meter apart, selected.
        private static void Place(List<Object> sounds, Transform parent, Vector3 point)
        {
            var made = new List<Object>();
            for (int i = 0; i < sounds.Count; i++)
            {
                var prefab = PrefabFor(sounds[i]);
                if (prefab == null)
                    continue;
                var scene = parent != null ? parent.gameObject.scene : UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                if (instance == null)
                    continue;
                if (parent != null)
                    instance.transform.SetParent(parent, false);
                instance.transform.position = point + Vector3.right * i;
                instance.name = GameObjectUtility.GetUniqueNameForSibling(instance.transform.parent, prefab.name);
                Undo.RegisterCreatedObjectUndo(instance, $"Add {prefab.name}");
                made.Add(instance);
            }
            if (made.Count > 0)
                Selection.objects = made.ToArray();
        }

        /// <summary>The sound's test prefab: the one made for it before, or a new one.</summary>
        public static GameObject PrefabFor(Object sound)
        {
            if (sound == null)
                return null;
            string path = $"{Folder}/{sound.name} (Sound Test).prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                if (Plays(SchematicObjectFactory.GraphOf(existing), sound))
                    return existing;
                path = AssetDatabase.GenerateUniqueAssetPath(path); // another sound with the same name has it
            }
            SchematicObjectFactory.EnsureFolder(Folder);
            var prefab = SchematicObjectFactory.CreateEmptySchematicPrefab(path);
            var graph = SchematicObjectFactory.GraphOf(prefab);
            if (graph == null)
                return prefab;

            var onCreate = NodeReflection.GetNodeType(typeof(OnCreate))?.CreateInstance();
            var play = PlayNodeFor(sound);
            if (onCreate == null || play == null)
                return prefab;
            onCreate.Name = "On Create";
            onCreate.Position = new Vector2(0f, 0f);
            play.Position = new Vector2(260f, 0f);
            graph.AddNode(onCreate);
            graph.AddNode(play);
            var from = onCreate.GetPort("▶");
            var to = play.GetPort("▷");
            if (from != null && to != null)
                graph.AddEdge(from, to);
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssetIfDirty(graph);
            return prefab;
        }

        // What a Schematic Graph plays it with: what dropping it into the graph would make (Play SFX for an SFX clip,
        // Play Music for a Music one, Play Sound Cue for a cue), else Play Sound (which goes by the clip's folder).
        private static Node PlayNodeFor(Object sound)
        {
            var match = AssetNodes.For(sound).FirstOrDefault(m => typeof(SchematicActionNodeBase).IsAssignableFrom(m.NodeType));
            if (match != null)
                return AssetNodes.Create(match, sound);
            if (!(sound is AudioClip clip))
                return null;
            var node = NodeReflection.GetNodeType(typeof(PlaySound))?.CreateInstance() as PlaySound;
            if (node == null)
                return null;
            node.Clip = clip;
            node.Name = $"Play Sound: {clip.name}";
            return node;
        }

        // Whether a graph has a node playing this sound.
        private static bool Plays(SchematicGraph graph, Object sound)
        {
            if (graph == null)
                return false;
            foreach (var node in graph.Nodes)
                if ((node is PlayClipNode clip && clip.Clip == sound) || (node is PlaySoundCueNode cue && cue.Cue == sound))
                    return true;
            return false;
        }
    }
}
