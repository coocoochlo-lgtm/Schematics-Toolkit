using System;
using BlueGraph;
using Cysharp.Threading.Tasks;
using Remedy.Framework;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.LevelStreaming
{
    /// <summary>
    /// Adds a level (a scene, or a level prefab) on top of what's loaded and lets it stream in over several frames.
    /// The nodes after it run once everything in the level has woken.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Load Level"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Levels"", ""description"": ""Adds a level on top of what's loaded (a scene by name or path, or a level prefab) and lets its content stream in over several frames. The nodes after it run once everything in the level has woken (or straight away when Wait is off)."", ""inputs"": [{""name"": ""Scene"", ""type"": ""string""}, {""name"": ""Prefab"", ""type"": ""GameObject""}, {""name"": ""Position"", ""type"": ""Vector3""}, {""name"": ""Wait"", ""type"": ""bool""}], ""outputs"": [{""name"": ""Level Name"", ""type"": ""string""}, {""name"": ""Progress"", ""type"": ""float""}, {""name"": ""Root"", ""type"": ""GameObject""}], ""use_for"": [""adding the next area of a level"", ""loading interiors and dungeons"", ""streaming big levels in without a hitch""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Loading a level that's already loaded just continues. Prefab wins over Scene when both are set. Put a Streamed Level in level scenes so they stream in; levels with NetworkObjects should be loaded the Netcode way.""}")]
    [Serializable]
    [NodeColor(NodeColors.Object)]
    [Node(Name = "Load Level", Path = "Levels"), Tags("Default")]
    public class LoadLevelNode : SchematicActionNode
    {
        [Tooltip("The scene to load, by name or path (leave empty to load a Prefab instead).")]
        [Input("Scene")] public string SceneName = "";
        [Tooltip("A level prefab to load instead of a scene.")]
        [Input("Prefab")] public GameObject Prefab;
        [Input("Position")] public Vector3 Position;
        [Tooltip("Wait until everything in the level has woken before running what's next. Off: carries straight on while it streams in.")]
        [Input("Wait")] public bool Wait = true;

        [Tooltip("The level's name, to unload it later.")]
        [Output("Level Name")] public string LevelName;
        [Output("Progress")] public float Progress;
        [Tooltip("The level's top object, once it's in.")]
        [Output("Root")] public GameObject Root;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var level = LevelNodeUtility.Resolve(GetInputValue<string>("Scene", SceneName), GetInputValue<GameObject>("Prefab", Prefab));
            var handle = LevelLoader.Load(level, new LevelLoadOptions { Position = GetInputValue<Vector3>("Position", Position) });
            if (handle == null)
                throw new InvalidOperationException("No level to load: set a Scene (name or path) or connect a Prefab.");

            SetOutputValue("Level Name", handle.Name);
            SetOutputValue("Progress", handle.Progress);
            SetOutputValue("Root", handle.Root);
            if (!GetInputValue<bool>("Wait", Wait) || handle.IsLoaded)
                return;

            // The nodes after this one run once the level is in, seeing the values they'd have seen now.
            _processChildren = false;
            ContinueWhenLoaded(instance, handle, Flow.Current?.Capture()).Forget();
        }

        private async UniTaskVoid ContinueWhenLoaded(GameObject instance, LevelHandle handle, FlowScope scope)
        {
            bool hadInstance = instance != null;
            while (!handle.IsDone)
            {
                LevelNodeUtility.Publish(this, scope, "Progress", handle.Progress);
                await UniTask.Yield();
                if (hadInstance && instance == null)
                    return;
            }
            // Failed, or unloaded before it finished: the rest doesn't run.
            if (!handle.IsLoaded)
                return;
            LevelNodeUtility.Publish(this, scope, "Progress", 1f);
            LevelNodeUtility.Publish(this, scope, "Root", handle.Root);
            Flow.StartChildren(this, instance, scope);
        }
    }

    /// <summary>
    /// Takes a level away again (a scene, or a level prefab), a few objects per frame. The nodes after it run once it's
    /// gone.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Unload Level"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Levels"", ""description"": ""Takes away a level added with Load Level (a scene by name or path, or a level prefab), a few objects per frame. A level still streaming in stops. The nodes after it run once it's gone (or straight away when Wait is off)."", ""inputs"": [{""name"": ""Scene"", ""type"": ""string""}, {""name"": ""Prefab"", ""type"": ""GameObject""}, {""name"": ""Force"", ""type"": ""bool""}, {""name"": ""Wait"", ""type"": ""bool""}], ""outputs"": [{""name"": ""Level Name"", ""type"": ""string""}], ""use_for"": [""removing areas the player has left"", ""swapping one level for another""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. A level that an occupied Level Streaming Zone still wants stays until the zone lets go, unless Force is on.""}")]
    [Serializable]
    [NodeColor(NodeColors.Object)]
    [Node(Name = "Unload Level", Path = "Levels"), Tags("Default")]
    public class UnloadLevelNode : SchematicActionNode
    {
        [Tooltip("The scene to unload, by name or path (or leave empty and give the Prefab).")]
        [Input("Scene")] public string SceneName = "";
        [Tooltip("The level prefab to unload.")]
        [Input("Prefab")] public GameObject Prefab;
        [Tooltip("Unload it even if it's still streaming in.")]
        [Input("Force")] public bool Force;
        [Tooltip("Wait until it's gone before running what's next. Off: carries straight on.")]
        [Input("Wait")] public bool Wait = true;

        [Tooltip("The name of the level that was unloaded.")]
        [Output("Level Name")] public string LevelName;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var level = LevelNodeUtility.Resolve(GetInputValue<string>("Scene", SceneName), GetInputValue<GameObject>("Prefab", Prefab));
            if (level == null)
                throw new InvalidOperationException("No level to unload: set a Scene (name or path) or connect a Prefab.");

            SetOutputValue("Level Name", level.Name);
            var unloading = LevelLoader.UnloadAsync(level, GetInputValue<bool>("Force", Force));
            if (!GetInputValue<bool>("Wait", Wait) || unloading.Status != UniTaskStatus.Pending)
                return;

            _processChildren = false;
            ContinueWhenGone(instance, unloading, Flow.Current?.Capture()).Forget();
        }

        private async UniTaskVoid ContinueWhenGone(GameObject instance, UniTask unloading, FlowScope scope)
        {
            bool hadInstance = instance != null;
            await unloading;
            if (hadInstance && instance == null)
                return;
            Flow.StartChildren(this, instance, scope);
        }
    }

    internal static class LevelNodeUtility
    {
        public static LevelReference Resolve(string scene, GameObject prefab)
        {
            if (prefab != null)
                return LevelReference.FromPrefab(prefab);
            if (!string.IsNullOrEmpty(scene))
                return LevelReference.FromScene(scene);
            return null;
        }

        // Sets an output from outside a running flow: on the node, and in the scope the nodes after it will run with.
        public static void Publish(SchematicGraphNode node, FlowScope scope, string port, Union value)
        {
            node.SetOutputValue(port, value);
            if (scope == null)
                return;
            value.LastUpdateTick = SchematicGraphNode.StampTick();
            scope.Set(node, port, value);
        }
    }
}
