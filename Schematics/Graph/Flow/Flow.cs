using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// One run through a graph: started by an event node (or a node continuing later, like Wait), it works through the
    /// nodes to execute one at a time instead of nesting C# calls, so it can stop before any node (a breakpoint) or after
    /// one that threw, and pick up again later. Port values written during the run live in the flow, so two objects
    /// running the same graph never read each other's values, even when one of them is paused.
    /// </summary>
    public sealed class Flow
    {
        public enum PauseReason { None, Breakpoint, Step, Error }

        private struct Step
        {
            public SchematicGraphNode Node;
            public FlowScope Scope;
            public Union[] Arguments;
        }

        // Guards against a graph wired into a loop, which would otherwise spin forever and freeze the editor.
        public const int MaxStepsPerRun = 100000;

        /// <summary>The flow running a node right now (null outside of graph execution).</summary>
        public static Flow Current { get; private set; }

        /// <summary>The object running the graph.</summary>
        public GameObject Instance { get; private set; }

        /// <summary>The graph the flow started in.</summary>
        public SchematicGraph Graph { get; private set; }

        /// <summary>The node the flow is stopped at (before it runs for a breakpoint or step, after it threw for an error).</summary>
        public SchematicGraphNode PausedNode { get; private set; }

        public PauseReason Paused { get; private set; }

        /// <summary>The exception that stopped the flow, when it stopped for an error.</summary>
        public Exception Error { get; private set; }

        /// <summary>The values the paused node sees.</summary>
        public FlowScope PausedScope { get; private set; }

        internal bool Stepping;

        private readonly List<Step> _stack = new();
        private readonly List<Step> _pending = new();
        private readonly FlowScope _rootScope = new();
        private Step _executing;
        private bool _isExecuting;

        // Identifies the step running now, so query (pure) nodes evaluated several times during one step are only
        // computed once (see SchematicGraphNode). Every step gets a new one, so the next step recomputes them.
        private int _stepStamp;
        private static int _lastStamp;

        /// <summary>
        /// The stamp of the step running right now, or 0 outside of one. Query nodes remember their result for this
        /// stamp; 0 means "don't remember, always compute".
        /// </summary>
        internal static int CurrentStamp
        {
            get
            {
                var flow = Current;
                return flow != null && flow._isExecuting ? flow._stepStamp : 0;
            }
        }
        private bool _skipBreakOnce;

        private static readonly Stack<Flow> _pool = new();

        private Flow() { }

        #region Starting

        /// <summary>Runs an event node and everything it leads to.</summary>
        public static void Start(SchematicEventNode node, GameObject instance, Union[] arguments)
        {
            // A networked graph's events run on the object's authority only.
            if (!GraphNetwork.RunsEventsFor(node.Graph as SchematicGraph, instance))
                return;
            var flow = Rent(node, instance);
            flow._stack.Add(new Step { Node = node, Scope = flow._rootScope, Arguments = arguments });
            flow.Run();
        }

        /// <summary>Runs an action node and everything after it.</summary>
        public static void Start(SchematicActionNodeBase node, GameObject instance, FlowScope scope = null)
        {
            var flow = Rent(node, instance);
            flow._stack.Add(new Step { Node = node, Scope = flow.Adopt(scope) });
            flow.Run();
        }

        /// <summary>
        /// Runs an action node with the given input values (instead of what's connected to it) and everything after it.
        /// Used to run a node replicated from another peer.
        /// </summary>
        public static void StartWithInputs(SchematicActionNodeBase node, GameObject instance, string[] ports, Union[] values)
        {
            var scope = new FlowScope();
            for (int i = 0; ports != null && i < ports.Length && i < values.Length; i++)
                scope.SetInput(node, ports[i], values[i]);
            var flow = Rent(node, instance);
            flow._remote = true;
            flow._stack.Add(new Step { Node = node, Scope = flow.Adopt(scope) });
            flow.Run();
        }

        // Started from a replicated node: it mustn't be sent back out.
        private bool _remote;

        /// <summary>
        /// Runs a node's children (and everything after them), e.g. once a Wait is over: those on its main flow output,
        /// or on the named one.
        /// </summary>
        public static void StartChildren(SchematicGraphNode node, GameObject instance, FlowScope scope = null, string port = null)
        {
            var children = node.FlowChildrenOf(port);
            if (children.Count == 0)
                return;
            var flow = Rent(node, instance);
            var childScope = flow.Adopt(scope);
            for (int i = children.Count - 1; i >= 0; i--)
                flow._stack.Add(new Step { Node = children[i], Scope = childScope });
#if UNITY_EDITOR
            ReportTraversed(instance, node, children);
#endif
            flow.Run();
        }

        private static Flow Rent(SchematicGraphNode node, GameObject instance)
        {
            var flow = _pool.Count > 0 ? _pool.Pop() : new Flow();
            flow.Instance = instance;
            flow.Graph = node.Graph as SchematicGraph;
            return flow;
        }

        // A captured scope continues from where it was taken; otherwise the flow starts with a fresh one.
        private FlowScope Adopt(FlowScope scope)
        {
            if (scope == null)
                return _rootScope;
            _rootScope.Parent = scope;
            return _rootScope;
        }

        private void Release()
        {
            _stack.Clear();
            _pending.Clear();
            _rootScope.Clear();
            Instance = null;
            Graph = null;
            PausedNode = null;
            PausedScope = null;
            Error = null;
            Paused = PauseReason.None;
            Stepping = false;
            _skipBreakOnce = false;
            _remote = false;
            _stepStamp = 0;
            _executing = default;
            if (_pool.Count < 64)
                _pool.Push(this);
        }

        #endregion

        #region Running

        private void Run()
        {
            var previous = Current;
            Current = this;
            int steps = 0;
            try
            {
                while (_stack.Count > 0)
                {
                    var step = _stack[_stack.Count - 1];

                    if (!_skipBreakOnce && FlowDebugger.ShouldBreak(this, step.Node, out var reason))
                    {
                        Pause(step, reason, null);
                        return;
                    }
                    _skipBreakOnce = false;

                    _stack.RemoveAt(_stack.Count - 1);

                    if (++steps > MaxStepsPerRun)
                    {
                        _stack.Clear();
                        Fail(step, new InvalidOperationException(
                            $"The graph ran more than {MaxStepsPerRun} nodes in a row without stopping. Its nodes are probably wired into a loop."));
                        if (Paused != PauseReason.None)
                            return;
                        break;
                    }

                    if (!Execute(step))
                    {
                        // Stopped by an error (in the editor while playing); everything else in the flow waits.
                        if (Paused != PauseReason.None)
                            return;
                    }
                }
            }
            finally
            {
                Current = previous;
            }

            Release();
        }

        // Runs one node and schedules whatever it leads to. False if it threw.
        private bool Execute(Step step)
        {
            _executing = step;
            _isExecuting = true;
            if (++_lastStamp == 0)
                _lastStamp = 1;
            _stepStamp = _lastStamp;
            _pending.Clear();
            try
            {
                // Nodes set to run on Everyone are sent to the other peers as the authority reaches them.
                if (!_remote && step.Node.NetworkMode == NodeNetworkMode.Everyone)
                    GraphNetwork.ReplicateIfNeeded(step.Node, Instance, step.Scope);

                if (step.Node.ExecuteInFlow(this, step.Scope, step.Arguments))
                    Schedule(step.Node, step.Scope, fork: false);
            }
            catch (Exception exception)
            {
                _pending.Clear();
                Fail(step, exception);
                return false;
            }
            finally
            {
                _isExecuting = false;
            }

            // First scheduled runs first, and each one's own followers run before the next (depth first).
            for (int i = _pending.Count - 1; i >= 0; i--)
                _stack.Add(_pending[i]);
            _pending.Clear();
            return true;
        }

        /// <summary>
        /// Queues a node's children to run after it. Called once a node is done, and by nodes that run their children
        /// themselves (a loop calling ProcessChildren per item): those get a snapshot of the node's outputs as they are
        /// at that moment, so each item's children see their own item.
        /// </summary>
        internal void Schedule(SchematicGraphNode node, FlowScope scope, bool fork, string port = null)
        {
            var children = node.FlowChildrenOf(port);
            if (children.Count == 0)
                return;

            if (fork)
            {
                var forked = new FlowScope { Parent = scope };
                node.CopyOutputs(scope, forked);
                scope = forked;
            }

            for (int i = 0; i < children.Count; i++)
                _pending.Add(new Step { Node = children[i], Scope = scope });
#if UNITY_EDITOR
            ReportTraversed(Instance, node, children);
#endif
        }

        /// <summary>True while this flow is in the middle of running the given node.</summary>
        internal bool IsExecuting(SchematicGraphNode node) => _isExecuting && _executing.Node == node;

        /// <summary>The values the running node reads and writes.</summary>
        internal FlowScope ExecutingScope => _isExecuting ? _executing.Scope : null;

        /// <summary>Values as they are now, detached from this flow, for continuing later (e.g. after a Wait).</summary>
        public FlowScope Capture() => (ExecutingScope ?? _rootScope).Flatten();

        #endregion

#if UNITY_EDITOR
        #region Flow visualizer (editor only)

        /// <summary>
        /// Editor only: raised as a flow goes along a flow arrow (the object running it, the node it leaves, the node it
        /// goes to), for the Schematic Graph window's flow visualizer to animate a ball along that arrow. Nobody listens
        /// unless the visualizer is switched on, so with it off this costs a single null check per node.
        /// </summary>
        public static event Action<GameObject, SchematicGraphNode, SchematicGraphNode> EditorEdgeTraversed;

        private static void ReportTraversed(GameObject instance, SchematicGraphNode from, List<SchematicActionNodeBase> children)
        {
            var handler = EditorEdgeTraversed;
            if (handler == null)
                return;
            // A listener's mistake must never break the game's flow.
            try
            {
                for (int i = 0; i < children.Count; i++)
                    handler(instance, from, children[i]);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        #endregion
#endif

        #region Pausing

        private void Pause(Step step, PauseReason reason, Exception exception)
        {
            Paused = reason;
            PausedNode = step.Node;
            PausedScope = step.Scope;
            Error = exception;
            Stepping = false;
            FlowDebugger.OnPaused(this);
        }

        private void Fail(Step step, Exception exception)
        {
            if (FlowDebugger.PauseOnError(this, step.Node, exception))
                Pause(step, PauseReason.Error, exception);
        }

        /// <summary>Carries on from where the flow stopped. A breakpoint's node runs now; a failed node's children are skipped.</summary>
        internal void Resume(bool step)
        {
            if (Paused == PauseReason.None)
                return;
            _skipBreakOnce = Paused != PauseReason.Error;
            Paused = PauseReason.None;
            PausedNode = null;
            PausedScope = null;
            Error = null;
            Stepping = step;
            Run();
        }

        /// <summary>Drops the rest of the flow.</summary>
        internal void Abandon()
        {
            if (Paused == PauseReason.None)
                return;
            Release();
        }

        #endregion
    }

    /// <summary>
    /// Port values written during a flow. A scope can sit on top of another (a loop's per-item values over the rest),
    /// reading through to it for anything it doesn't have.
    /// </summary>
    public sealed class FlowScope
    {
        public FlowScope Parent;
        private readonly Dictionary<(SchematicGraphNode, string), Union> _values = new();

        public bool TryGet(SchematicGraphNode node, string port, out Union value)
        {
            for (var scope = this; scope != null; scope = scope.Parent)
                if (scope._values.TryGetValue((node, port), out value))
                    return true;
            value = default;
            return false;
        }

        public void Set(SchematicGraphNode node, string port, Union value) => _values[(node, port)] = value;

        // Input values given to the flow from outside, read before what's connected.
        private Dictionary<(SchematicGraphNode, string), Union> _inputs;

        public void SetInput(SchematicGraphNode node, string port, Union value)
            => (_inputs ??= new Dictionary<(SchematicGraphNode, string), Union>())[(node, port)] = value;

        public bool TryGetInput(SchematicGraphNode node, string port, out Union value)
        {
            for (var scope = this; scope != null; scope = scope.Parent)
                if (scope._inputs != null && scope._inputs.TryGetValue((node, port), out value))
                    return true;
            value = default;
            return false;
        }

        internal void Clear()
        {
            _values.Clear();
            _inputs?.Clear();
            Parent = null;
        }

        /// <summary>A standalone copy of every value visible from here.</summary>
        internal FlowScope Flatten()
        {
            var copy = new FlowScope();
            var chain = new List<FlowScope>();
            for (var scope = this; scope != null; scope = scope.Parent)
                chain.Add(scope);
            // Outermost first, so nearer scopes win.
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                foreach (var pair in chain[i]._values)
                    copy._values[pair.Key] = pair.Value;
                if (chain[i]._inputs != null)
                    foreach (var pair in chain[i]._inputs)
                        copy.SetInput(pair.Key.Item1, pair.Key.Item2, pair.Value);
            }
            return copy;
        }
    }
}
