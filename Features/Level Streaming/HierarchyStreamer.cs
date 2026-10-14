using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.LevelStreaming
{
    /// <summary>
    /// Wakes a hierarchy up a few objects per frame, and takes one apart the same way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trick: an object whose parent is inactive has never woken (no Awake, no OnEnable). So the content starts
    /// switched off, and waking happens top-down. Just before an object is switched on, its children that should end up
    /// active are switched off, so switching it on wakes that one object only. Its children then wait their turn. Each
    /// object ends up with the active flag it was saved with: children that were saved inactive are never touched.
    /// </para>
    /// <para>
    /// Some objects are woken whole, children and all, because they expect to be: a Schematic Object (its Behaviors on
    /// child objects register the moment the root wakes, so the children should be there too). A Level Chunk that is
    /// going to hide itself the moment it starts gets its children held back instead, and shows them when a zone asks.
    /// </para>
    /// </remarks>
    internal static class HierarchyStreamer
    {
        // A stream that couldn't get any budget for this many frames in a row does one object anyway, so a flood of
        // other streaming work can slow it down but never stall it.
        private const int StarvationFrames = 8;

        private enum ChildHandling
        {
            /// <summary>Children wake one by one after this object.</summary>
            Stream,
            /// <summary>Children wake together with this object (left as saved).</summary>
            Together,
            /// <summary>Children are switched off and left for someone else to show (a Level Chunk).</summary>
            Hold,
        }

        /// <summary>
        /// Switches the given (inactive) roots on, top-down, within the shared <see cref="StreamingBudget"/>. Only pass
        /// roots that should end up active. Destroyed objects are skipped; cancelling stops where it is (what's awake
        /// stays awake, the rest stays off).
        /// </summary>
        /// <param name="estimate">Roughly how many objects there are, for progress. Progress reaches 1 when done regardless.</param>
        public static async UniTask ActivateAsync(IReadOnlyList<GameObject> roots, int estimate, bool nearestFirst,
            CancellationToken token, Action<float> progress)
        {
            var frontier = new Frontier(nearestFirst);
            var targets = new List<Vector3>();
            LevelStreamer.GetTargetPositions(targets);
            foreach (var root in roots)
                if (root != null)
                    frontier.Push(root.transform, targets);

            estimate = Mathf.Max(1, estimate);
            int woken = 0;
            int starved = 0;

            while (frontier.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                if (StreamingBudget.Exhausted && starved < StarvationFrames)
                {
                    starved++;
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    continue;
                }
                starved = 0;
                if (nearestFirst)
                    LevelStreamer.GetTargetPositions(targets);

                StreamingBudget.Begin();
                try
                {
                    do
                    {
                        var node = frontier.Pop();
                        // Destroyed by something that woke earlier, or unloaded from under us.
                        if (node == null)
                            continue;
                        Wake(node, frontier, targets);
                        StreamingBudget.Spend();
                        woken++;
                    }
                    while (frontier.Count > 0 && !StreamingBudget.Exhausted && !token.IsCancellationRequested);
                }
                finally
                {
                    StreamingBudget.End();
                }

                progress?.Invoke(Mathf.Min(0.99f, (float)woken / estimate));
                if (frontier.Count > 0)
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            progress?.Invoke(1f);
        }

        private static void Wake(Transform node, Frontier frontier, List<Vector3> targets)
        {
            // Something else switched it on in the meantime: it (and its children) woke the normal way already.
            if (node.gameObject.activeSelf)
                return;
            var handling = HandlingFor(node.gameObject);
            if (handling != ChildHandling.Together)
            {
                // The object hasn't woken (its parent was off until now), so neither have its children: switching them
                // off here is cheap, and it means switching this one on wakes it alone.
                int count = node.childCount;
                for (int i = 0; i < count; i++)
                {
                    var child = node.GetChild(i);
                    if (!child.gameObject.activeSelf)
                        continue;   // saved inactive: stays that way
                    child.gameObject.SetActive(false);
                    if (handling == ChildHandling.Stream)
                        frontier.Push(child, targets);
                }
            }
            node.gameObject.SetActive(true);
        }

        private static ChildHandling HandlingFor(GameObject gameObject)
        {
            if (gameObject.transform.childCount == 0)
                return ChildHandling.Together;
            if (LevelLoader.KeepSchematicObjectsWhole && gameObject.TryGetComponent<SchematicObject>(out _))
                return ChildHandling.Together;
            if (gameObject.TryGetComponent<LevelChunk>(out var chunk) && chunk.enabled
                && chunk.Hiding == LevelChunk.HideMode.Deactivate && !chunk.VisibleAtStart && !LevelStreamer.IsWanted(chunk))
                return ChildHandling.Hold;   // it'd hide them in Start anyway; a zone shows them later, spread out
            return ChildHandling.Stream;
        }

        /// <summary>
        /// Destroys everything under the given roots (and the roots), a few objects per frame, children before their
        /// parents. Schematic Objects go whole. Waits a frame at the end so Unity has finished the deferred destroys.
        /// </summary>
        public static async UniTask DestroyAsync(IReadOnlyList<GameObject> roots)
        {
            var order = new List<GameObject>();
            var stack = new Stack<Transform>();
            foreach (var root in roots)
            {
                if (root == null)
                    continue;
                stack.Push(root.transform);
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    order.Add(node.gameObject);
                    if (LevelLoader.KeepSchematicObjectsWhole && node.TryGetComponent<SchematicObject>(out _))
                        continue;
                    for (int i = 0; i < node.childCount; i++)
                        stack.Push(node.GetChild(i));
                }
            }

            // Parents were listed before their children, so going backwards destroys children first.
            int index = order.Count - 1;
            int starved = 0;
            while (index >= 0)
            {
                if (StreamingBudget.Exhausted && starved < StarvationFrames)
                {
                    starved++;
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    continue;
                }
                starved = 0;

                StreamingBudget.Begin();
                try
                {
                    do
                    {
                        var gameObject = order[index--];
                        if (gameObject == null)
                            continue;
                        // Switching it off runs OnDisable now, where the clock can see it, rather than all at once
                        // when the destroys are carried out at the end of the frame.
                        gameObject.SetActive(false);
                        UnityEngine.Object.Destroy(gameObject);
                        StreamingBudget.Spend();
                    }
                    while (index >= 0 && !StreamingBudget.Exhausted);
                }
                finally
                {
                    StreamingBudget.End();
                }
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }

        /// <summary>
        /// Objects waiting to wake. In nearest-first mode it hands out whatever is closest to a streaming target (a
        /// binary heap by distance); otherwise it goes in hierarchy order, level by level.
        /// </summary>
        private sealed class Frontier
        {
            private readonly bool _byDistance;
            private readonly Queue<Transform> _queue = new Queue<Transform>();
            private readonly List<(float distance, int order, Transform node)> _heap = new List<(float, int, Transform)>();
            private int _pushed;

            public Frontier(bool byDistance) => _byDistance = byDistance;

            public int Count => _byDistance ? _heap.Count : _queue.Count;

            public void Push(Transform node, List<Vector3> targets)
            {
                if (!_byDistance)
                {
                    _queue.Enqueue(node);
                    return;
                }
                float distance = float.MaxValue;
                Vector3 position = node.position;
                foreach (var target in targets)
                    distance = Mathf.Min(distance, (position - target).sqrMagnitude);
                // No targets: plain hierarchy order (the push order breaks ties).
                if (targets.Count == 0)
                    distance = 0f;

                _heap.Add((distance, _pushed++, node));
                int i = _heap.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!Less(_heap[i], _heap[parent]))
                        break;
                    (_heap[i], _heap[parent]) = (_heap[parent], _heap[i]);
                    i = parent;
                }
            }

            public Transform Pop()
            {
                if (!_byDistance)
                    return _queue.Dequeue();

                var top = _heap[0].node;
                int last = _heap.Count - 1;
                _heap[0] = _heap[last];
                _heap.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1, right = left + 1, smallest = i;
                    if (left < _heap.Count && Less(_heap[left], _heap[smallest])) smallest = left;
                    if (right < _heap.Count && Less(_heap[right], _heap[smallest])) smallest = right;
                    if (smallest == i)
                        break;
                    (_heap[i], _heap[smallest]) = (_heap[smallest], _heap[i]);
                    i = smallest;
                }
                return top;
            }

            private static bool Less((float distance, int order, Transform node) a, (float distance, int order, Transform node) b)
                => a.distance < b.distance || (a.distance == b.distance && a.order < b.order);
        }
    }
}
