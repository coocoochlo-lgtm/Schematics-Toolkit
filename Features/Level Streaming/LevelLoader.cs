using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Remedy.Schematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Remedy.LevelStreaming
{
    /// <summary>
    /// Adds levels on top of what's already loaded (additive scenes, or level prefabs), and takes them away again,
    /// without the hitch of everything in a big level waking up in the same frame. A level's content wakes a few objects
    /// per frame within the shared <see cref="StreamingBudget"/>, nearest to the player first; taking it away is spread
    /// out the same way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A level stays loaded while anything wants it: code (or the Load Level node) that loaded it, or an occupied
    /// <see cref="LevelStreamingZone"/> that lists it. Asking for a level that is already loaded (or on its way) gives
    /// back the same <see cref="LevelHandle"/>. Unloading a level that is still streaming in stops the streaming.
    /// </para>
    /// <para>
    /// Scenes: put a <see cref="StreamedLevel"/> in the level scene so its content arrives asleep and streams in;
    /// without one the scene still loads, but wakes all at once the way Unity normally does it. Prefabs need nothing.
    /// </para>
    /// <para>
    /// Networking: objects with a NetworkObject (Netcode for GameObjects) can't be created by a client on its own.
    /// Levels containing them should be loaded the Netcode way (its NetworkSceneManager), not through this loader.
    /// </para>
    /// </remarks>
    public static class LevelLoader
    {
        /// <summary>Wake what's nearest to a streaming target (the player, or the main camera) first. Prefab levels; scenes use their Streamed Level's setting.</summary>
        public static bool NearestFirst = true;

        /// <summary>
        /// Prefab levels: copy the prefab over several frames with Unity's InstantiateAsync, instead of all in one
        /// frame. The copy is still asleep either way; this only spreads out the copying itself.
        /// </summary>
        public static bool InstantiateInBackground = true;

        /// <summary>Take levels apart a few objects per frame before unloading them, instead of all at once.</summary>
        public static bool SliceUnload = true;

        /// <summary>
        /// Wake (and destroy) each Schematic Object together with its children, rather than one object at a time. Its
        /// Behaviors on child objects register the moment its root wakes, so they expect the children to be there.
        /// </summary>
        public static bool KeepSchematicObjectsWhole = true;

        /// <summary>Raised when a level has finished streaming in.</summary>
        public static event Action<LevelHandle> LevelLoaded;

        /// <summary>Raised when a level is gone (unloaded here, or its scene was unloaded some other way).</summary>
        public static event Action<LevelHandle> LevelUnloaded;

        /// <summary>Raised whenever a level's <see cref="LevelHandle.Progress"/> changes.</summary>
        public static event Action<LevelHandle> LevelProgress;

        // Levels that are loading, streaming or loaded, by LevelReference.Key.
        private static readonly Dictionary<string, LevelHandle> _levels = new Dictionary<string, LevelHandle>();
        // Levels being taken apart (a new load of the same level waits for them).
        private static readonly List<LevelHandle> _leaving = new List<LevelHandle>();
        // Scene levels whose scene may still be arriving: its Streamed Levels hand themselves to these.
        private static readonly List<LevelHandle> _arriving = new List<LevelHandle>();
        private static readonly HashSet<string> _warned = new HashSet<string>();
        private static bool _quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _levels.Clear();
            _leaving.Clear();
            _arriving.Clear();
            _warned.Clear();
            _quitting = false;
            LevelLoaded = null;
            LevelUnloaded = null;
            LevelProgress = null;

            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting() => _quitting = true;

        /// <summary>The levels that are loading, streaming in, or loaded.</summary>
        public static IReadOnlyCollection<LevelHandle> Levels => _levels.Values;

        /// <summary>The handle for a level that is loading, streaming in or loaded; null otherwise.</summary>
        public static LevelHandle Find(LevelReference level)
        {
            if (level == null || !level.IsValid)
                return null;
            return _levels.TryGetValue(level.Key, out var handle) ? handle : null;
        }

        /// <summary>The handle for a level (loading, streaming in or loaded) by its name; null otherwise.</summary>
        public static LevelHandle Find(string name)
        {
            foreach (var handle in _levels.Values)
                if (string.Equals(handle.Name, name, StringComparison.OrdinalIgnoreCase))
                    return handle;
            return null;
        }

        /// <summary>Whether a level has finished streaming in.</summary>
        public static bool IsLoaded(LevelReference level) => Find(level)?.IsLoaded == true;

        #region Loading

        /// <summary>
        /// Starts loading a level (or gives back the one already loading or loaded) and returns its handle straight
        /// away; watch its <see cref="LevelHandle.Progress"/> or await <see cref="LevelHandle.WhenLoaded"/>. Returns
        /// null outside Play Mode or without a level.
        /// </summary>
        public static LevelHandle Load(LevelReference level, LevelLoadOptions options = null) => Acquire(level, options, byZone: false);

        /// <summary>Starts loading a scene by name or path. See <see cref="Load(LevelReference, LevelLoadOptions)"/>.</summary>
        public static LevelHandle Load(string sceneNameOrPath) => Load(LevelReference.FromScene(sceneNameOrPath));

        /// <summary>Starts loading a level prefab. See <see cref="Load(LevelReference, LevelLoadOptions)"/>.</summary>
        public static LevelHandle Load(GameObject prefab, LevelLoadOptions options = null) => Load(LevelReference.FromPrefab(prefab), options);

        /// <summary>
        /// Loads a level and waits until it has finished streaming in (or failed, or was unloaded first: check
        /// <see cref="LevelHandle.IsLoaded"/>). The token only stops the waiting; to stop the level, unload it.
        /// </summary>
        public static async UniTask<LevelHandle> LoadAsync(LevelReference level, LevelLoadOptions options = null,
            IProgress<float> progress = null, CancellationToken token = default)
        {
            var handle = Load(level, options);
            if (handle == null)
                return null;
            while (!handle.IsDone)
            {
                progress?.Report(handle.Progress);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            progress?.Report(handle.Progress);
            return handle;
        }

        internal static LevelHandle AcquireForZone(LevelReference level) => Acquire(level, null, byZone: true);

        private static LevelHandle Acquire(LevelReference level, LevelLoadOptions options, bool byZone)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Level Streaming] Levels can only be loaded in Play Mode.");
                return null;
            }
            if (_quitting)
                return null;
            if (level == null || !level.IsValid)
            {
                Debug.LogWarning("[Level Streaming] No level to load: pick a scene or a prefab.");
                return null;
            }

            string key = level.Key;
            if (!_levels.TryGetValue(key, out var handle))
            {
                handle = new LevelHandle(level, options);
                _levels[key] = handle;
                RunLoad(handle, FindLeaving(key)).Forget();
            }
            if (byZone) handle.ZoneCount++;
            else handle.WantedByCode = true;
            return handle;
        }

        private static async UniTaskVoid RunLoad(LevelHandle handle, LevelHandle previous)
        {
            var token = handle.Cancel.Token;
            try
            {
                // The same level still on its way out: let it finish leaving, so the two copies never overlap.
                if (previous != null)
                    while (previous.State != LevelState.Unloaded)
                        await UniTask.Yield(PlayerLoopTiming.Update, token);

                if (handle.Level.Kind == LevelReference.LevelKind.Scene)
                    await LoadScene(handle, token);
                else
                    await LoadPrefab(handle, token);
                token.ThrowIfCancellationRequested();

                _arriving.Remove(handle);
                handle.State = LevelState.Loaded;
                handle.SetProgress(1f);
                handle.StartFired = true;
                handle.FinishLoad(true);
                Raise(LevelLoaded, handle);
                FireSingletonEvent<OnLevelStart>(handle.Name);
            }
            catch (OperationCanceledException)
            {
                // Unloading took over and reports the outcome. If it wasn't us that stopped it (a Streamed Level was
                // destroyed by something else), what did wake stays, and the level counts as loaded.
                if (!token.IsCancellationRequested && handle.State == LevelState.Streaming)
                {
                    _arriving.Remove(handle);
                    handle.State = LevelState.Loaded;
                    handle.SetProgress(1f);
                    handle.StartFired = true;
                    handle.FinishLoad(true);
                    Raise(LevelLoaded, handle);
                    FireSingletonEvent<OnLevelStart>(handle.Name);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Fail(handle);
            }
        }

        private static async UniTask LoadScene(LevelHandle handle, CancellationToken token)
        {
            // Already open (the editor's multi-scene setup, or loaded some other way): use it as it is.
            // If its Streamed Level is still waking it (it streams itself in), wait for that like for a fresh load.
            var open = FindLoadedScene(handle.Level);
            if (open.IsValid())
            {
                handle.Scene = open;
                foreach (var root in open.GetRootGameObjects())
                    handle.StreamedLevels.AddRange(root.GetComponentsInChildren<StreamedLevel>(true));
                await StreamIn(handle, token, warnIfNone: false);
                return;
            }

            string path = handle.ScenePath;
            AsyncOperation operation = null;
#if UNITY_EDITOR
            // In the editor any scene asset can be played, even one missing from the Build Profile's scene list.
            if (LevelReference.IsPath(path) && SceneUtility.GetBuildIndexByScenePath(path) < 0)
                operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
#endif
            if (operation == null)
                operation = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            if (operation == null)
                throw new InvalidOperationException($"[Level Streaming] Couldn't load the scene '{path}'. Is it in the Build Profile's scene list?");

            handle.Operation = operation;
            _arriving.Add(handle);
            // Unity integrates the scene during this wait: the Streamed Levels in it wake (only themselves) and hand
            // themselves to this handle through Claim.
            while (!operation.isDone)
            {
                handle.SetProgress(operation.progress * 0.5f);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            token.ThrowIfCancellationRequested();

            if (!handle.Scene.IsValid())
                handle.Scene = FindLoadedScene(handle.Level);
            if (!handle.Scene.IsValid())
                throw new InvalidOperationException($"[Level Streaming] The scene '{path}' loaded but couldn't be found afterwards.");

            await StreamIn(handle, token, warnIfNone: true);
        }

        // Wakes a scene level's content through its Streamed Levels, reporting the second half of the progress.
        private static async UniTask StreamIn(LevelHandle handle, CancellationToken token, bool warnIfNone)
        {
            handle.State = LevelState.Streaming;
            handle.SetProgress(0.5f);
            handle.StreamedLevels.RemoveAll(level => level == null || !level.isActiveAndEnabled);
            // The scene processor only prepares one Streamed Level per scene; extra ones have nothing to wake.
            if (handle.StreamedLevels.Exists(level => level.IsPrepared))
                handle.StreamedLevels.RemoveAll(level => !level.IsPrepared);
            else if (handle.StreamedLevels.Count > 1)
                handle.StreamedLevels.RemoveRange(1, handle.StreamedLevels.Count - 1);
            int count = handle.StreamedLevels.Count;
            if (count == 0)
            {
                if (warnIfNone && _warned.Add("nostream:" + handle.Scene.path))
                    Debug.Log($"[Level Streaming] '{handle.Name}' has no Streamed Level, so all of it woke in the frame it " +
                              "arrived. Add one (GameObject > Level Streaming > Streamed Level) to have it stream in over time.");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                var level = handle.StreamedLevels[i];
                if (level == null)
                    continue;
                var stream = level.StreamInAsync(token);
                while (stream.Status == UniTaskStatus.Pending)
                {
                    handle.SetProgress(0.5f + 0.5f * (i + level.Progress) / count);
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }
                await stream;   // passes on cancellation or errors
            }
        }

        private static async UniTask LoadPrefab(LevelHandle handle, CancellationToken token)
        {
            var prefab = handle.Level.Prefab;
            var options = handle.Options;
            WarnIfNetworked(prefab);

            // The level goes under a holder that stays off until it's ready: nothing inside wakes on the way in.
            var holder = new GameObject("[Level] " + handle.Name);
            holder.SetActive(false);
            if (options.Parent != null)
                holder.transform.SetParent(options.Parent, false);
            holder.transform.SetPositionAndRotation(options.Position, options.Rotation);
            handle.Root = holder;

            GameObject instance = null;
#if UNITY_6000_0_OR_NEWER
            if (InstantiateInBackground)
            {
                var operation = Object.InstantiateAsync(prefab, holder.transform);
                handle.Operation = operation;
                while (!operation.isDone)
                {
                    handle.SetProgress(operation.progress * 0.5f);
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }
                token.ThrowIfCancellationRequested();
                if (operation.Result != null && operation.Result.Length > 0)
                    instance = operation.Result[0];
            }
#endif
            if (instance == null)
                instance = Object.Instantiate(prefab, holder.transform, false);
            if (holder == null || instance == null)
                throw new InvalidOperationException($"[Level Streaming] The level '{handle.Name}' was destroyed while it was being created.");

            // Switch the copy's root off (remembering whether it was on), then the holder on: still nothing has woken.
            bool rootActive = instance.activeSelf;
            instance.SetActive(false);
            holder.SetActive(true);

            handle.State = LevelState.Streaming;
            handle.SetProgress(0.5f);
            if (!rootActive)
                return;
            int estimate = instance.GetComponentsInChildren<Transform>(true).Length;
            await HierarchyStreamer.ActivateAsync(new[] { instance }, estimate, options.NearestFirst ?? NearestFirst, token,
                progress => handle.SetProgress(0.5f + 0.5f * progress));
        }

        // Called by a Streamed Level waking in a scene that's arriving: the loader drives its streaming from here.
        internal static bool Claim(StreamedLevel level)
        {
            var scene = level.gameObject.scene;
            foreach (var handle in _arriving)
            {
                if (!handle.Level.Matches(scene))
                    continue;
                if (!handle.Scene.IsValid())
                    handle.Scene = scene;
                if (!handle.StreamedLevels.Contains(level))
                    handle.StreamedLevels.Add(level);
                return true;
            }
            return false;
        }

        private static void Fail(LevelHandle handle)
        {
            if (_levels.TryGetValue(handle.Key, out var current) && current == handle)
                _levels.Remove(handle.Key);
            _arriving.Remove(handle);
            handle.State = LevelState.Failed;
            handle.Cancel.Cancel();
            // Don't leave half a level behind.
            if (handle.Root != null)
                Object.Destroy(handle.Root);
            if (handle.Scene.IsValid() && handle.Scene.isLoaded && SceneManager.sceneCount > 1)
                SceneManager.UnloadSceneAsync(handle.Scene);
            handle.FinishUnload();
        }

        #endregion

        #region Unloading

        /// <summary>
        /// Lets go of a level that code loaded, and takes it away unless an occupied zone still wants it (force takes it
        /// away regardless). A level still streaming in stops streaming. Completes once it's gone (or straight away if
        /// it stays).
        /// </summary>
        public static UniTask UnloadAsync(LevelReference level, bool force = false)
        {
            if (level == null || !level.IsValid)
                return UniTask.CompletedTask;
            string key = level.Key;
            if (_levels.TryGetValue(key, out var handle))
                return UnloadAsync(handle, force);
            var leaving = FindLeaving(key);
            return leaving != null ? leaving.WhenUnloaded : UniTask.CompletedTask;
        }

        /// <summary>Unloads a scene level by name or path. See <see cref="UnloadAsync(LevelReference, bool)"/>.</summary>
        public static UniTask UnloadAsync(string sceneNameOrPath, bool force = false) => UnloadAsync(LevelReference.FromScene(sceneNameOrPath), force);

        /// <summary>See <see cref="UnloadAsync(LevelReference, bool)"/>.</summary>
        public static UniTask UnloadAsync(LevelHandle handle, bool force = false)
        {
            if (handle == null)
                return UniTask.CompletedTask;
            if (handle.State == LevelState.Unloading || handle.State == LevelState.Unloaded || handle.State == LevelState.Failed)
                return handle.WhenUnloaded;
            handle.WantedByCode = false;
            if (!force && handle.IsWanted)
                return UniTask.CompletedTask;   // a zone still wants it; it goes when the zone lets go
            return BeginUnload(handle);
        }

        internal static void ReleaseForZone(LevelReference level)
        {
            if (!_levels.TryGetValue(level.Key, out var handle))
                return;
            handle.ZoneCount = Mathf.Max(0, handle.ZoneCount - 1);
            if (!handle.IsWanted && !_quitting)
                BeginUnload(handle).Forget();
        }

        private static UniTask BeginUnload(LevelHandle handle)
        {
            if (handle.State == LevelState.Unloading || handle.State == LevelState.Unloaded)
                return handle.WhenUnloaded;
            handle.State = LevelState.Unloading;
            if (_levels.TryGetValue(handle.Key, out var current) && current == handle)
                _levels.Remove(handle.Key);
            _leaving.Add(handle);
            // Stops it streaming in, wherever it is.
            handle.Cancel.Cancel();
            RunUnload(handle).Forget();
            return handle.WhenUnloaded;
        }

        private static async UniTaskVoid RunUnload(LevelHandle handle)
        {
            try
            {
                // A scene load or an instantiation can't be stopped halfway: let it land, then take it apart.
                var operation = handle.Operation;
                if (operation != null)
                    while (!operation.isDone)
                        await UniTask.Yield(PlayerLoopTiming.Update);

                if (handle.Level.Kind == LevelReference.LevelKind.Scene)
                {
                    var scene = handle.Scene.IsValid() ? handle.Scene : FindLoadedScene(handle.Level);
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        if (SliceUnload)
                            await HierarchyStreamer.DestroyAsync(scene.GetRootGameObjects());
                        if (scene.isLoaded)
                        {
                            var unload = SceneManager.UnloadSceneAsync(scene);
                            if (unload != null)
                                while (!unload.isDone)
                                    await UniTask.Yield(PlayerLoopTiming.Update);
                        }
                    }
                }
                else if (handle.Root != null)
                {
                    if (SliceUnload)
                        await HierarchyStreamer.DestroyAsync(new[] { handle.Root });
                    else
                        Object.Destroy(handle.Root);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            Gone(handle);
        }

        // A level's scene was unloaded without us (SceneManager, another system): forget it.
        private static void OnSceneUnloaded(Scene scene)
        {
            List<LevelHandle> gone = null;
            foreach (var handle in _levels.Values)
            {
                bool lost = handle.Level.Kind == LevelReference.LevelKind.Scene
                    ? handle.Scene.IsValid() && handle.Scene == scene
                    : handle.State == LevelState.Loaded && handle.Root == null;   // its holder went with the scene
                if (lost)
                    (gone ??= new List<LevelHandle>()).Add(handle);
            }
            if (gone == null)
                return;
            foreach (var handle in gone)
            {
                _levels.Remove(handle.Key);
                handle.Cancel.Cancel();
                Gone(handle);
            }
        }

        private static void Gone(LevelHandle handle)
        {
            if (handle.State == LevelState.Unloaded)
                return;
            _leaving.Remove(handle);
            _arriving.Remove(handle);
            bool started = handle.StartFired;
            handle.State = LevelState.Unloaded;
            handle.SetProgress(0f);
            handle.FinishUnload();
            Raise(LevelUnloaded, handle);
            if (started)
                FireSingletonEvent<OnLevelExit>(handle.Name);
        }

        private static LevelHandle FindLeaving(string key)
        {
            foreach (var handle in _leaving)
                if (handle.Key == key)
                    return handle;
            return null;
        }

        #endregion

        #region Helpers

        private static Scene FindLoadedScene(LevelReference level)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && level.Matches(scene))
                    return scene;
            }
            return default;
        }

        internal static void RaiseProgress(LevelHandle handle) => Raise(LevelProgress, handle);

        private static void Raise(Action<LevelHandle> action, LevelHandle handle)
        {
            if (action == null)
                return;
            try { action(handle); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        // On Level Start / On Level Exit in every Singleton Graph, with the level's name.
        private static void FireSingletonEvent<TEvent>(string levelName) where TEvent : SchematicEventNode
        {
            var spawned = SingletonGraphSpawner.Spawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                var host = spawned[i];
                if (host == null || host.SchematicGraph == null)
                    continue;
                try { host.SchematicGraph.TriggerEvent<TEvent>(host.gameObject, levelName); }
                catch (Exception exception) { Debug.LogException(exception, host); }
            }
        }

        private static Type _networkObjectType;
        private static bool _networkObjectTypeLooked;

        /// <summary>
        /// Netcode for GameObjects' NetworkObject, when that package is in the project (looked up by name, so this
        /// feature doesn't depend on it). Null otherwise.
        /// </summary>
        public static Type NetworkObjectType
        {
            get
            {
                if (!_networkObjectTypeLooked)
                {
                    _networkObjectTypeLooked = true;
                    _networkObjectType = Type.GetType("Unity.Netcode.NetworkObject, Unity.Netcode.Runtime", false);
                }
                return _networkObjectType;
            }
        }

        private static void WarnIfNetworked(GameObject prefab)
        {
            var type = NetworkObjectType;
            if (type == null || prefab == null || prefab.GetComponentInChildren(type, true) == null)
                return;
            //if (_warned.Add("net:" + prefab.GetInstanceID()))
            //    Debug.LogWarning($"[Level Streaming] The level prefab '{prefab.name}' contains NetworkObjects. Clients can't " +
            //                     "create those themselves; load networked levels the Netcode way (NetworkSceneManager) instead.", prefab);
        }

        #endregion
    }
}
