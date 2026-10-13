using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.LevelStreaming
{
    /// <summary>Where an additive level is in its life.</summary>
    public enum LevelState
    {
        /// <summary>The scene is loading, or the prefab is being instantiated (nothing is awake yet).</summary>
        Loading,
        /// <summary>It's there, and its content is waking up a few objects per frame.</summary>
        Streaming,
        /// <summary>Everything has woken.</summary>
        Loaded,
        /// <summary>It's being taken apart.</summary>
        Unloading,
        /// <summary>Gone.</summary>
        Unloaded,
        /// <summary>It couldn't be loaded (the error was logged).</summary>
        Failed,
    }

    /// <summary>Optional settings for <see cref="LevelLoader.Load"/>.</summary>
    public sealed class LevelLoadOptions
    {
        /// <summary>Prefab levels: where the level is placed (the level's holder object goes here).</summary>
        public Vector3 Position = Vector3.zero;

        /// <summary>Prefab levels: how the level is turned.</summary>
        public Quaternion Rotation = Quaternion.identity;

        /// <summary>Prefab levels: an object to put the level under (else it goes into the active scene).</summary>
        public Transform Parent;

        /// <summary>Prefab levels: wake what's nearest to a streaming target first. Null uses <see cref="LevelLoader.NearestFirst"/>.</summary>
        public bool? NearestFirst;
    }

    /// <summary>
    /// One additive level the <see cref="LevelLoader"/> is looking after: its state, progress, and what it made (the
    /// scene, or the prefab level's holder object). Asking for an already loaded level gives back the same handle.
    /// </summary>
    public sealed class LevelHandle
    {
        internal LevelHandle(LevelReference level, LevelLoadOptions options)
        {
            Level = level;
            Options = options ?? new LevelLoadOptions();
            Key = level.Key;
            ScenePath = level.Kind == LevelReference.LevelKind.Scene ? level.ResolveScenePath() : "";
        }

        /// <summary>What was asked for.</summary>
        public LevelReference Level { get; }

        /// <summary>The level's name (scene file name or prefab name).</summary>
        public string Name => Level.Name;

        /// <summary>Where it is in its life.</summary>
        public LevelState State { get; internal set; } = LevelState.Loading;

        /// <summary>
        /// 0 to 1: loading the scene (or instantiating the prefab) is the first half, waking its content the second.
        /// Back to 0 once unloaded.
        /// </summary>
        public float Progress { get; private set; }

        /// <summary>Whether everything has woken.</summary>
        public bool IsLoaded => State == LevelState.Loaded;

        /// <summary>Whether it has finished one way or another (loaded, unloaded, or failed).</summary>
        public bool IsDone => State == LevelState.Loaded || State == LevelState.Unloaded || State == LevelState.Failed;

        /// <summary>The scene, for scene levels (once it has arrived).</summary>
        public Scene Scene { get; internal set; }

        /// <summary>For prefab levels: the holder object the level was placed under.</summary>
        public GameObject Root { get; internal set; }

        /// <summary>Completes with true once loaded, or false if it failed or was unloaded before it finished.</summary>
        public UniTask<bool> WhenLoaded => _loaded.Task;

        /// <summary>Completes once the level is gone.</summary>
        public UniTask WhenUnloaded => _unloaded.Task;

        // Whether code (Load, the Load Level node) wants it; and how many occupied zones do.
        internal bool WantedByCode;
        internal int ZoneCount;
        internal bool StartFired;

        internal readonly string Key;
        internal readonly string ScenePath;
        internal readonly LevelLoadOptions Options;
        internal readonly CancellationTokenSource Cancel = new CancellationTokenSource();
        internal readonly List<StreamedLevel> StreamedLevels = new List<StreamedLevel>();

        // The scene load or prefab instantiation in flight: it can't be stopped, so unloading waits for it.
        internal AsyncOperation Operation;

        private readonly UniTaskCompletionSource<bool> _loaded = new UniTaskCompletionSource<bool>();
        private readonly UniTaskCompletionSource _unloaded = new UniTaskCompletionSource();

        internal bool IsWanted => WantedByCode || ZoneCount > 0;

        internal void SetProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (Mathf.Approximately(progress, Progress))
                return;
            Progress = progress;
            LevelLoader.RaiseProgress(this);
        }

        internal void FinishLoad(bool loaded) => _loaded.TrySetResult(loaded);

        internal void FinishUnload()
        {
            _loaded.TrySetResult(false);
            _unloaded.TrySetResult();
        }

        public override string ToString() => $"{Name} ({State}, {Progress:P0})";
    }
}
