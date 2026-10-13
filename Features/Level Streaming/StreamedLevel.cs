using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Remedy.LevelStreaming
{
    /// <summary>
    /// Put one in a level scene to have its content wake up over several frames instead of all at once when the scene
    /// is added. Without it, Unity wakes every object of an additive scene in the frame the scene arrives, which is
    /// where the big loading hitch comes from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// How it works: when the scene is built into the game (and when it's loaded in Play Mode in the editor), a scene
    /// processor switches the content off and remembers what it switched off, so the scene arrives asleep. This
    /// component then wakes it top-down within the shared <see cref="StreamingBudget"/>, nearest to the player first.
    /// The scene asset itself is never changed.
    /// </para>
    /// <para>
    /// Loaded through <see cref="LevelLoader"/> (or a zone, or the Load Level node), the loader drives it and reports
    /// progress. Loaded any other way (SceneManager, the first scene of the game), it streams itself in from Start.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Level Streaming/Streamed Level")]
    public class StreamedLevel : MonoBehaviour
    {
        public enum ContentScope
        {
            /// <summary>Every root object of the scene (except this one's own), plus this object's children.</summary>
            WholeScene,
            /// <summary>Only this object's children.</summary>
            ChildrenOnly,
        }

        [Tooltip("What streams in: the whole scene, or only this object's children.")]
        public ContentScope Content = ContentScope.WholeScene;

        [Tooltip("Root objects (or children of this one) that wake straight away with the scene, e.g. lighting or a " +
                 "spawn point the player needs on the first frame.")]
        public List<GameObject> WakeImmediately = new List<GameObject>();

        [Tooltip("Wake what's nearest to a streaming target (the player, or the main camera) first.")]
        public bool NearestFirst = true;

        // Filled in by the scene processor when the scene is built or loaded in Play Mode, never saved to the asset.
        [SerializeField, HideInInspector] private bool _prepared;
        [SerializeField, HideInInspector] private List<GameObject> _sleepingRoots = new List<GameObject>();
        [SerializeField, HideInInspector] private int _objectCount;

        /// <summary>Whether the scene processor switched the content off before the scene loaded.</summary>
        public bool IsPrepared => _prepared;

        /// <summary>How many objects stream in (counted when the content was switched off).</summary>
        public int ObjectCount => _objectCount;

        /// <summary>Whether everything has woken.</summary>
        public bool IsStreamedIn { get; private set; }

        /// <summary>Whether it's waking its content right now.</summary>
        public bool IsStreaming => _completion != null && _completion.Task.Status == UniTaskStatus.Pending;

        /// <summary>0 to 1, how much of the content has woken.</summary>
        public float Progress { get; private set; }

        /// <summary>Raised once everything has woken.</summary>
        public event Action<StreamedLevel> StreamedIn;

        private bool _claimed;
        private UniTaskCompletionSource _completion;

        private static readonly HashSet<string> _warnedScenes = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _warnedScenes.Clear();

        /// <summary>The objects that stream in: the content's top-level objects, in hierarchy order.</summary>
        public void CollectContentRoots(List<GameObject> roots)
        {
            roots.Clear();
            var scene = gameObject.scene;
            if (Content == ContentScope.WholeScene && scene.IsValid() && scene.isLoaded)
            {
                var ownRoot = transform.root.gameObject;
                foreach (var root in scene.GetRootGameObjects())
                    if (root != ownRoot && !WakeImmediately.Contains(root))
                        roots.Add(root);
            }
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i).gameObject;
                if (!WakeImmediately.Contains(child))
                    roots.Add(child);
            }
        }

        /// <summary>
        /// Switches the content off and remembers what was on, so the scene arrives asleep. Called by the scene
        /// processor on the copy of the scene being built or played; calling it on a scene you're editing would switch
        /// your content off. Returns how many objects were switched off.
        /// </summary>
        public int PrepareForStreaming()
        {
            if (_prepared)
                return 0;
            var roots = new List<GameObject>();
            CollectContentRoots(roots);

            _sleepingRoots.Clear();
            _objectCount = 0;
            var transforms = new List<Transform>();
            foreach (var root in roots)
            {
                // Saved inactive: it stays that way, nothing to stream.
                if (root == null || !root.activeSelf)
                    continue;
                root.GetComponentsInChildren(true, transforms);
                _objectCount += transforms.Count;
                root.SetActive(false);
                _sleepingRoots.Add(root);
            }
            _prepared = true;
            return _sleepingRoots.Count;
        }

        private void Awake()
        {
            // Runs while the scene is being added, before the loader hears the load is done: it can take over from here.
            _claimed = LevelLoader.Claim(this);
        }

        private void Start()
        {
            if (!_claimed)
                StreamSelf().Forget();
        }

        private async UniTaskVoid StreamSelf()
        {
            try
            {
                await StreamInAsync(this.GetCancellationTokenOnDestroy());
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// Wakes the content over the next frames. Calling it again while it runs (or after) gives back the same
        /// work. Cancelling stops it where it is; it doesn't start again (the level is usually being unloaded).
        /// </summary>
        public UniTask StreamInAsync(CancellationToken token = default)
        {
            if (IsStreamedIn)
                return UniTask.CompletedTask;
            if (_completion != null)
                return _completion.Task;
            _completion = new UniTaskCompletionSource();
            Run(token).Forget();
            return _completion.Task;
        }

        private async UniTaskVoid Run(CancellationToken token)
        {
            var completion = _completion;
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, this.GetCancellationTokenOnDestroy()))
                {
                    if (!_prepared)
                        WarnUnprepared();
                    else
                        await HierarchyStreamer.ActivateAsync(_sleepingRoots, _objectCount, NearestFirst, linked.Token, p => Progress = p);
                }
                Progress = 1f;
                IsStreamedIn = true;
                StreamedIn?.Invoke(this);
                completion.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                completion.TrySetException(exception);
            }
        }

        // The content was never switched off (e.g. Play Mode entered without reloading the scene, or the processor
        // was skipped), so it has already woken the normal way. Nothing breaks; it just wasn't spread out.
        private void WarnUnprepared()
        {
            string key = gameObject.scene.path;
            if (!_warnedScenes.Add(key))
                return;
            Debug.Log($"[Level Streaming] The Streamed Level in '{gameObject.scene.name}' wasn't prepared before the scene " +
                      "loaded (its scene processor didn't run, e.g. Play Mode was entered without reloading the scene), so " +
                      "its content woke all at once instead of streaming in.", this);
        }
    }
}
