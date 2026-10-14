using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// Breakpoints and error stops for graphs. In the editor during Play Mode, a flow reaching a node with a breakpoint
    /// (or a node that throws) stops there and pauses the game at the end of the frame; the Schematic Graph window shows
    /// where and lets you continue, step or stop. In builds, a node that throws is logged and its flow carries on
    /// without that node's children.
    /// </summary>
    public static class FlowDebugger
    {
        /// <summary>A node that threw: kept so the graph window can show it after the flow is gone.</summary>
        public sealed class FlowError
        {
            public SchematicGraph Graph;
            public string NodeId;
            public string NodeName;
            public string InstanceName;
            public Exception Exception;
            public DateTime Time;
        }

        private static readonly List<Flow> _paused = new();
        private static readonly List<FlowError> _errors = new();

        /// <summary>Flows stopped at a breakpoint or error, oldest first. The first is the one being looked at.</summary>
        public static IReadOnlyList<Flow> PausedFlows => _paused;

        public static Flow Current => _paused.Count > 0 ? _paused[0] : null;

        /// <summary>Errors since entering Play Mode, newest last.</summary>
        public static IReadOnlyList<FlowError> Errors => _errors;

        /// <summary>Raised when a flow pauses or resumes, or an error is recorded or dismissed.</summary>
        public static event Action Changed;

        /// <summary>Whether a node that throws stops its flow (editor, Play Mode). Otherwise it's logged and skipped.</summary>
        public static bool PauseOnErrors = true;

        private static bool CanPause => Application.isEditor && Application.isPlaying;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            _paused.Clear();
            _errors.Clear();
            Changed?.Invoke();
        }

        internal static bool ShouldBreak(Flow flow, SchematicGraphNode node, out Flow.PauseReason reason)
        {
            reason = Flow.PauseReason.None;
            if (!CanPause)
                return false;
            if (flow.Stepping)
                reason = Flow.PauseReason.Step;
            else if (node.Breakpoint)
                reason = Flow.PauseReason.Breakpoint;
            return reason != Flow.PauseReason.None;
        }

        /// <summary>Records the error; true if the flow should stop at it.</summary>
        internal static bool PauseOnError(Flow flow, SchematicGraphNode node, Exception exception)
        {
            Debug.LogException(new SchematicNodeException(flow, node, exception), flow.Instance);

            _errors.Add(new FlowError
            {
                Graph = flow.Graph,
                NodeId = node.ID,
                NodeName = node.Name,
                InstanceName = flow.Instance != null ? flow.Instance.name : "(no object)",
                Exception = exception,
                Time = DateTime.Now,
            });
            if (_errors.Count > 50)
                _errors.RemoveAt(0);

            bool pause = CanPause && PauseOnErrors;
            if (!pause)
                Changed?.Invoke();
            return pause;
        }

        internal static void OnPaused(Flow flow)
        {
            _paused.Add(flow);
            // Pauses at the end of this frame; the rest of the frame (other scripts and flows) still runs.
            Debug.Break();
            Changed?.Invoke();
        }

        /// <summary>Lets the current paused flow carry on.</summary>
        public static void Continue() => Resume(step: false);

        /// <summary>Runs the current paused flow's next node, then stops again.</summary>
        public static void Step() => Resume(step: true);

        private static void Resume(bool step)
        {
            var flow = Current;
            if (flow == null)
                return;
            _paused.RemoveAt(0);
            flow.Resume(step);
            AfterResume();
        }

        /// <summary>Drops the rest of the current paused flow.</summary>
        public static void Stop()
        {
            var flow = Current;
            if (flow == null)
                return;
            _paused.RemoveAt(0);
            flow.Abandon();
            AfterResume();
        }

        /// <summary>Lets every paused flow carry on (each still stops at its next breakpoint).</summary>
        public static void ContinueAll()
        {
            var flows = _paused.ToArray();
            _paused.Clear();
            foreach (var flow in flows)
                flow.Resume(step: false);
            AfterResume();
        }

        public static void DismissError(FlowError error)
        {
            if (_errors.Remove(error))
                Changed?.Invoke();
        }

        public static void DismissErrors(SchematicGraph graph)
        {
            if (_errors.RemoveAll(e => e.Graph == graph) > 0)
                Changed?.Invoke();
        }

        // The game stays paused while any flow is still stopped; otherwise it runs again.
        private static void AfterResume()
        {
#if UNITY_EDITOR
            if (_paused.Count == 0 && Application.isPlaying)
                UnityEditor.EditorApplication.isPaused = false;
#endif
            Changed?.Invoke();
        }
    }

    /// <summary>An exception thrown by a graph node, naming the node, graph and object it happened on.</summary>
    public sealed class SchematicNodeException : Exception
    {
        public SchematicNodeException(Flow flow, SchematicGraphNode node, Exception inner)
            : base($"{inner.GetType().Name} in node \"{node.Name}\" of graph \"{(flow.Graph != null ? flow.Graph.name : "?")}\" " +
                   $"on {(flow.Instance != null ? flow.Instance.name : "(no object)")}: {inner.Message}", inner)
        { }
    }
}
